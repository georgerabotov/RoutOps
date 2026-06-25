# Route Output Module — Build Spec

> Scope: **the output stage only.** Route optimisation, disruption detection, and
> scheduling already exist. This module takes a finished route and (1) builds a Google
> Maps deep link, (2) writes it back onto the corresponding Google Calendar event,
> idempotently, every cycle (~30 min). It does **not** decide routes.

---

## 1. Input contract (what the existing logic hands this module)

The upstream logic must provide one object per event it has planned a route for:

```jsonc
{
  "calendarId": "primary",                 // or the specific calendar
  "eventId": "abc123",                     // Google Calendar event id
  "origin":      { "lat": 51.5045, "lng": -0.0865, "placeId": "ChIJ..." },  // placeId optional
  "destination": { "lat": 51.5081, "lng": -0.1281, "placeId": "ChIJ..." },
  "waypoints":  [                          // ordered; the optimiser's chosen sequence + any forced detour
    { "lat": 51.5155, "lng": -0.1426 },
    { "lat": 51.5237, "lng": -0.1585 }
  ],
  "travelMode": "driving",                 // driving | walking | bicycling | transit | two-wheeler
  "summary": {                             // human-readable bits for the event description
    "leaveBy": "09:40",
    "durationMin": 22,
    "distanceKm": 8.4,
    "disruptionNote": "Northern line part-suspended — driving recommended"
  },
  "routeHash": "sha1-of-the-route"         // stable hash of origin+dest+waypoints+mode; used to skip no-op writes
}
```

> `routeHash` is the contract that keeps writes idempotent. Upstream computes it; this
> module only compares it. If upstream can't produce it, this module derives it from
> the coordinate sequence + travelMode (see §4).

---

## 2. Google Maps deep-link builder

**No API key. No Routes/Maps quota consumed.** It's just a URL.

### Template
```
https://www.google.com/maps/dir/?api=1
  &origin=<lat,lng>
  &destination=<lat,lng>
  &waypoints=<lat,lng>|<lat,lng>|...
  &travelmode=<mode>
  &dir_action=navigate        // optional: launches turn-by-turn immediately on mobile
```

### Builder (pseudocode)
```js
function buildMapsLink(route) {
  const base = "https://www.google.com/maps/dir/";
  const p = new URLSearchParams();
  p.set("api", "1");

  p.set("origin", `${route.origin.lat},${route.origin.lng}`);
  p.set("destination", `${route.destination.lat},${route.destination.lng}`);

  if (route.waypoints?.length) {
    // pipe-separated; URLSearchParams encodes the "|" to %7C automatically
    p.set("waypoints", route.waypoints.map(w => `${w.lat},${w.lng}`).join("|"));
  }

  p.set("travelmode", route.travelMode ?? "driving");
  // p.set("dir_action", "navigate");  // enable if you want instant nav

  return `${base}?${p.toString()}`;
}
```

### Place IDs (more stable than lat/lng for named venues)
If you have Place IDs, pass **both** the text/coords *and* the place_id — Google uses the
place_id when both are present:
- `origin` + `origin_place_id`
- `destination` + `destination_place_id`
- `waypoints` + `waypoint_place_ids` (same `|` order)

### Constraints / gotchas
- **Waypoint cap:** Google documents support for **up to 9 intermediate waypoints** via the
  URL API. If the optimiser ever produces a longer chain, the link may be truncated or
  rejected — **flag it upstream**, don't silently drop stops.
- **No polyline / no `avoid=`:** the URL API can't force a specific road or pass `avoid`
  flags. A chosen detour must already be encoded as a **waypoint** by the optimiser.
- **Encoding:** always build with `URLSearchParams` (or equivalent). The `|` must become
  `%7C`; never hand-concatenate.
- **Travel mode `two-wheeler`** is only honoured in supported regions; default to `driving`.

---

## 3. Calendar write-back

### Auth / scope
- OAuth2, scope: `https://www.googleapis.com/auth/calendar.events` (read+write).
- Read-only (`calendar.events.readonly`) is **not** enough — we patch events.

### Idempotency marker block
Wrap our content in sentinels so we replace our own block each run instead of stacking
duplicates. Everything outside the block (the user's own notes) is left untouched.

```
<user's existing notes — never modified>

<!-- ROUTE-AGENT:START -->
🗺️ Optimised route → <a href="{MAPS_URL}">Open in Google Maps</a>
⏱️ Leave by {leaveBy} · {durationMin} min · {distanceKm} km
{disruptionNote ? "⚠️ " + disruptionNote : "✅ No disruptions on route"}
<!-- ROUTE-AGENT:END -->
```

> Google Calendar descriptions render a **limited** HTML subset — `<a href>` works, plain
> text + emoji work. Don't rely on tables/styling.

### Patch logic (pseudocode)
```js
function applyRouteToEvent(route) {
  const event = Calendar.events.get(route.calendarId, route.eventId);

  // Skip if nothing changed since last write.
  const lastHash = event.extendedProperties?.private?.routeHash;
  if (lastHash === route.routeHash) return { skipped: true };

  const mapsUrl = buildMapsLink(route);
  const block   = renderAgentBlock(route, mapsUrl);          // the START..END block above
  const newDesc = upsertBlock(event.description ?? "", block); // strip old block, append fresh

  Calendar.events.patch(route.calendarId, route.eventId, {
    description: newDesc,
    extendedProperties: {
      private: { routeHash: route.routeHash, routeUpdatedAt: <pass-in timestamp> }
    }
  });
  return { skipped: false };
}

// Replace any existing ROUTE-AGENT block; otherwise append.
function upsertBlock(desc, block) {
  const re = /<!-- ROUTE-AGENT:START -->[\s\S]*?<!-- ROUTE-AGENT:END -->/;
  return re.test(desc)
    ? desc.replace(re, block)
    : (desc.trimEnd() + "\n\n" + block);
}
```

Why `extendedProperties.private.routeHash`:
- Survives across cycles, invisible to the user, queryable.
- Lets the 30-min loop **skip the patch entirely** when the route is unchanged — no
  pointless writes, no edit-notification spam, no quota.

---

## 4. The pull-and-update structure (general wiring)

This is the loop skeleton around the two functions above. Disruption/optimisation calls
are shown only as the boundary where your existing logic plugs in.

```js
// runs every ~30 min
async function cycle({ now }) {                       // pass `now` in; don't call Date.now() internally if you need determinism
  // 1. PULL — events in the planning window (free, Calendar API)
  const events = await Calendar.events.list({
    calendarId: "primary",
    timeMin: now,
    timeMax: now + WINDOW,         // e.g. next 24h
    singleEvents: true,
    orderBy: "startTime",
    // only events that have a location worth routing to
  }).then(r => r.items.filter(e => e.location));

  // 2. HAND OFF to existing logic — returns RouteOutput[] (see §1).
  //    (route optimisation + disruption checks already built; this is the seam.)
  const routes = await existingRouteEngine.plan(events);

  // 3. OUTPUT — build link + patch event, idempotently
  const results = [];
  for (const route of routes) {
    try {
      results.push(await applyRouteToEvent(route));
    } catch (err) {
      results.push({ eventId: route.eventId, error: err.message });
    }
  }

  return summarise(results);       // {written, skipped, errored}
}
```

### Derive `routeHash` here if upstream doesn't supply it
```js
function deriveHash(route) {
  const seq = [route.origin, ...route.waypoints, route.destination]
    .map(p => `${p.lat},${p.lng}`).join(">");
  return sha1(`${route.travelMode}|${seq}`);
}
```

---

## 5. What this module guarantees

- **Zero Maps/Routes quota** — output is a plain URL; quota is spent only by the existing
  optimiser, upstream of here.
- **Idempotent** — re-running the cycle on an unchanged route is a no-op (hash compare).
- **Non-destructive** — only ever touches its own `ROUTE-AGENT` block; user notes preserved.
- **Self-healing** — if a user deletes the block, next cycle re-adds it (hash unchanged →
  it would skip; so on block-deletion, also clear/ignore the stored hash, or always
  re-assert if the block is missing — see note below).

> **Edge note:** the skip-on-equal-hash optimisation means a user-deleted block won't be
> restored until the route changes. If you want the block to always be present, change the
> skip condition to: `skip only if hash matches AND block still present in description`.

---

## 6. Open items for the implementer

- [ ] Confirm OAuth consent screen has `calendar.events` scope and (if multi-user)
      per-user token storage.
- [ ] Decide block-presence policy (§5 edge note): "skip on hash" vs "always assert block".
- [ ] Confirm the optimiser never emits >9 waypoints; if it can, define the fallback
      (split link, or link to first leg only + note).
- [ ] Decide whether to also set the event `location` field (native Directions button) in
      addition to the description link.
