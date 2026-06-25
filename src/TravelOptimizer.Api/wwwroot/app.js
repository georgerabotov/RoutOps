"use strict";

const REFRESH_MS = 30000;
const state = { date: null, corridorKey: null, hourChart: null, timer: null, routeMap: null, routeLayer: null };

const MODE_COLORS = {
  walk: "#22c55e",
  tube: "#3b82f6",
  bus: "#f59e0b",
  rail: "#8b5cf6",
  dlr: "#8b5cf6",
  overground: "#8b5cf6",
  cycle: "#06b6d4",
  mixed: "#94a3b8",
};
const LEG_FALLBACK = ["#3b82f6", "#22c55e", "#f59e0b", "#8b5cf6", "#06b6d4", "#94a3b8"];
const LONDON = [51.5074, -0.1278];

/* ---------------- helpers ---------------- */
const $ = (sel) => document.querySelector(sel);
const el = (html) => { const t = document.createElement("template"); t.innerHTML = html.trim(); return t.content.firstElementChild; };

async function api(path, opts) {
  const res = await fetch(path, { headers: { "Accept": "application/json" }, ...opts });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.status === 204 ? null : res.json();
}

function fmtTime(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}
function fmtAgo(iso) {
  if (!iso) return "never";
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return `${Math.round(s)}s ago`;
  if (s < 3600) return `${Math.round(s / 60)}m ago`;
  if (s < 86400) return `${Math.round(s / 3600)}h ago`;
  return `${Math.round(s / 86400)}d ago`;
}
function pct(x) { return `${Math.round((x ?? 0) * 100)}%`; }

function toast(msg, bad) {
  const t = el(`<div class="toast ${bad ? "bad" : ""}">${msg}</div>`);
  document.body.appendChild(t);
  requestAnimationFrame(() => t.classList.add("show"));
  setTimeout(() => { t.classList.remove("show"); setTimeout(() => t.remove(), 300); }, 2600);
}

/* ---------------- mode icons ---------------- */
const ICONS = {
  walk: '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="13" cy="4" r="2"/><path d="M11 7 8 9l-2 5 1.6.8L9 12l2 1-2 8h2l1.8-6.6L15 17l1 5h2l-1.4-7-3-3 .8-3.2L18 11l1-1.6L14.5 6 11 7z"/></svg>',
  tube: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="5" y="3" width="14" height="14" rx="3"/><path d="M5 9h14M8 21l2-3M16 21l-2-3" stroke-linecap="round"/><circle cx="8.5" cy="13" r="1" fill="currentColor" stroke="none"/><circle cx="15.5" cy="13" r="1" fill="currentColor" stroke="none"/></svg>',
  bus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="13" rx="2"/><path d="M3 10h18M7 21v-2M17 21v-2" stroke-linecap="round"/><circle cx="7.5" cy="14" r="1" fill="currentColor" stroke="none"/><circle cx="16.5" cy="14" r="1" fill="currentColor" stroke="none"/></svg>',
  rail: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="6" y="3" width="12" height="13" rx="3"/><path d="M6 10h12M9 20l-2 2M15 20l2 2" stroke-linecap="round"/><circle cx="9" cy="13" r="1" fill="currentColor" stroke="none"/><circle cx="15" cy="13" r="1" fill="currentColor" stroke="none"/></svg>',
  cycle: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="6" cy="17" r="3"/><circle cx="18" cy="17" r="3"/><path d="M6 17l4-7h5l3 7M10 10l-1-3H7" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  mixed: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 17h3l3-10h4"/><path d="M14 7h2l4 0M16 12h4M14 17h6"/><circle cx="20" cy="7" r="1.4" fill="currentColor" stroke="none"/></svg>',
  overground: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="8"/><path d="M4 12h16" stroke-linecap="round"/></svg>',
  dlr: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="5" y="5" width="14" height="14" rx="4"/><path d="M9 12h6" stroke-linecap="round"/></svg>',
  default: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2" stroke-linecap="round"/></svg>'
};
function iconClass(mode) {
  return ["walk", "tube", "bus", "rail", "cycle", "mixed", "overground", "dlr"].includes(mode) ? `m-${mode}` : "m-default";
}
function modeIcon(mode) {
  return `<span class="mode-icon ${iconClass(mode)}">${ICONS[mode] || ICONS.default}</span>`;
}

/* ---------------- KPIs ---------------- */
function renderKpis(o) {
  const kpis = $("#kpis");
  kpis.innerHTML = "";
  const cards = [
    { label: "Planned day", value: o.date, foot: `${o.legCount} leg${o.legCount === 1 ? "" : "s"}` },
    { label: "Predicted wasted", value: `${o.totalPredictedWastedMin}<small> min</small>`, foot: "across chosen routes" },
    { label: "Mixed-mode options", value: o.mixedOptionCount, foot: "composite journeys offered" },
    { label: "Corridors learned", value: o.corridorsLearned, foot: `${o.corridorSampleCount} samples gathered` },
  ];
  for (const c of cards) {
    kpis.appendChild(el(`<div class="kpi">
      <div class="kpi-label">${c.label}</div>
      <div class="kpi-value">${c.value}</div>
      <div class="kpi-foot">${c.foot}</div>
    </div>`));
  }
}

function modeColor(mode, idx) {
  return MODE_COLORS[mode] || LEG_FALLBACK[idx % LEG_FALLBACK.length];
}

// Google Maps directions deep link for a leg, built from its coords (mode-aware, mixed-mode
// waypoints from the segment boundaries). Falls back to the backend-provided mapsUrl.
function legMapsUrl(leg) {
  const ok = (a, b) => Number.isFinite(a) && Number.isFinite(b) && !(a === 0 && b === 0);
  if (!ok(leg.fromLat, leg.fromLng) || !ok(leg.toLat, leg.toLng)) return leg.mapsUrl || null;
  const origin = `${leg.fromLat},${leg.fromLng}`;
  const dest = `${leg.toLat},${leg.toLng}`;
  const segs = leg.decision && leg.decision.segments ? leg.decision.segments : [];
  let wp = "";
  if (segs.length > 1) {
    const mids = segs.slice(0, -1).map((s) => ok(s.toLat, s.toLng) ? `${s.toLat},${s.toLng}` : null).filter(Boolean);
    if (mids.length) wp = `&waypoints=${mids.join("|")}`;
  }
  const m = leg.decision ? leg.decision.chosenMode : "transit";
  const g = m === "walk" ? "walking" : m === "cycle" ? "bicycling" : "transit";
  return `https://www.google.com/maps/dir/?api=1&origin=${origin}&destination=${dest}${wp}&travelmode=${g}`;
}

function legHasCoords(leg) {
  const ok = (lat, lng) => Number.isFinite(lat) && Number.isFinite(lng) && !(lat === 0 && lng === 0);
  return ok(leg.fromLat, leg.fromLng) && ok(leg.toLat, leg.toLng);
}

function segmentHasCoords(seg) {
  const ok = (lat, lng) => Number.isFinite(lat) && Number.isFinite(lng) && !(lat === 0 && lng === 0);
  return ok(seg.fromLat, seg.fromLng) && ok(seg.toLat, seg.toLng);
}

function segmentPopupHtml(seg, legIdx) {
  const color = modeColor(seg.mode, seg.order);
  const from = seg.fromLabel || "Start";
  const to = seg.toLabel || "End";
  return `<div class="map-popup">
    <div class="map-popup-title">${modeIcon(seg.mode)} ${seg.mode} · ${seg.durationMin}m</div>
    <div class="map-popup-row"><span>Route</span><b style="color:${color}">${from} → ${to}</b></div>
    ${seg.summary ? `<div class="map-popup-rationale">${seg.summary}</div>` : ""}
    <div class="map-popup-row"><span>Leg</span><b>${legIdx + 1}</b></div>
  </div>`;
}

function resolveSegmentEndpoints(leg, segments, idx) {
  const seg = segments[idx];
  const ok = (lat, lng) => Number.isFinite(lat) && Number.isFinite(lng) && !(lat === 0 && lng === 0);

  let fromLat = seg.fromLat;
  let fromLng = seg.fromLng;
  let toLat = seg.toLat;
  let toLng = seg.toLng;

  if (!ok(fromLat, fromLng) && idx > 0) {
    const prev = segments[idx - 1];
    if (ok(prev.toLat, prev.toLng)) { fromLat = prev.toLat; fromLng = prev.toLng; }
  }
  if (!ok(fromLat, fromLng)) { fromLat = leg.fromLat; fromLng = leg.fromLng; }

  if (!ok(toLat, toLng) && idx < segments.length - 1) {
    const next = segments[idx + 1];
    if (ok(next.fromLat, next.fromLng)) { toLat = next.fromLat; toLng = next.fromLng; }
  }
  if (!ok(toLat, toLng)) { toLat = leg.toLat; toLng = leg.toLng; }

  if (ok(fromLat, fromLng) && ok(toLat, toLng)) {
    return { from: [fromLat, fromLng], to: [toLat, toLng] };
  }

  const legFrom = [leg.fromLat, leg.fromLng];
  const legTo = [leg.toLat, leg.toLng];
  const totalDur = segments.reduce((sum, s) => sum + (s.durationMin || 1), 0) || segments.length;
  let startFrac = 0;
  for (let i = 0; i < idx; i++) startFrac += (segments[i].durationMin || 1) / totalDur;
  const endFrac = startFrac + (seg.durationMin || 1) / totalDur;

  const lerp = (a, b, t) => a + (b - a) * t;
  return {
    from: [lerp(legFrom[0], legTo[0], startFrac), lerp(legFrom[1], legTo[1], startFrac)],
    to: [lerp(legFrom[0], legTo[0], endFrac), lerp(legFrom[1], legTo[1], endFrac)],
  };
}

async function fetchOsrmGeometry(from, to, mode) {
  const profile = mode === "cycle" ? "bike" : "foot";
  const url = `https://router.project-osrm.org/route/v1/${profile}/${from[1]},${from[0]};${to[1]},${to[0]}?overview=full&geometries=geojson`;
  try {
    const res = await fetch(url);
    if (!res.ok) return [from, to];
    const data = await res.json();
    const coords = data.routes?.[0]?.geometry?.coordinates;
    if (coords?.length >= 2) return coords.map(([lng, lat]) => [lat, lng]);
  } catch { /* fall through */ }
  return [from, to];
}

async function segmentPolylinePoints(from, to, mode) {
  if (mode === "walk" || mode === "cycle") return fetchOsrmGeometry(from, to, mode);
  return [from, to];
}

function drawSegmentLine(points, color, mode, popup) {
  const dash = mode === "walk" || mode === "cycle" ? "8 10" : null;
  const line = L.polyline(points, {
    color, weight: 5, opacity: 0.9, dashArray: dash,
  }).bindPopup(popup, { className: "dark-popup" });
  state.routeLayer.addLayer(line);
  return line;
}

function legPopupHtml(leg, idx) {
  const d = leg.decision;
  const mode = d ? d.chosenMode : "—";
  const segs = d && d.segments && d.segments.length
    ? d.segments.map((s) => `${s.mode} ${s.durationMin}m`).join(" → ")
    : null;
  const rationale = d && d.rationale ? d.rationale : "No decision yet.";
  return `<div class="map-popup">
    <div class="map-popup-title">Leg ${idx + 1}: ${leg.fromLabel || "Start"} → ${leg.toLabel || "End"}</div>
    <div class="map-popup-row"><span>Mode</span><b style="color:${modeColor(mode, idx)}">${mode}</b></div>
    ${segs ? `<div class="map-popup-row"><span>Segments</span><b>${segs}</b></div>` : ""}
    <div class="map-popup-row"><span>Depart</span><b>${d ? fmtTime(d.recommendedDeparture) : "—"}</b></div>
    <div class="map-popup-row"><span>Arrive</span><b>${d ? fmtTime(d.predictedArrival) : "—"}</b></div>
    <div class="map-popup-rationale">${rationale}</div>
  </div>`;
}

// Self-contained SVG "map" — draws the route (stops + colored lines) from leg coords. No external
// resource, so it always renders even when map tiles can't load (offline / sandboxed preview).
function mapPlaceholderSvg(itin) {
  const legs = (itin.legs || []).filter((l) => Number.isFinite(l.fromLat) && Number.isFinite(l.fromLng) && Number.isFinite(l.toLat) && Number.isFinite(l.toLng));
  if (!legs.length) return '<div class="map-fallback"><span class="mf-cap">No route coordinates to plot.</span></div>';
  const pts = []; const pushU = (lat, lng, label) => { if (!pts.some((p) => p[0] === lat && p[1] === lng)) pts.push([lat, lng, label]); };
  legs.forEach((l) => { pushU(l.fromLat, l.fromLng, l.fromLabel); pushU(l.toLat, l.toLng, l.toLabel); });
  const lats = pts.map((p) => p[0]), lngs = pts.map((p) => p[1]);
  const minLat = Math.min(...lats), maxLat = Math.max(...lats), minLng = Math.min(...lngs), maxLng = Math.max(...lngs);
  const W = 820, H = 420, pad = 72;
  const sx = (lng) => pad + (maxLng === minLng ? 0.5 : (lng - minLng) / (maxLng - minLng)) * (W - 2 * pad);
  const sy = (lat) => pad + (maxLat === minLat ? 0.5 : (maxLat - lat) / (maxLat - minLat)) * (H - 2 * pad);
  const esc = (s) => (s || "").replace(/&/g, "&amp;").replace(/</g, "&lt;");
  let grid = ""; for (let i = 1; i < 10; i++) grid += `<line x1="${i * W / 10}" y1="0" x2="${i * W / 10}" y2="${H}"/>`; for (let i = 1; i < 5; i++) grid += `<line x1="0" y1="${i * H / 5}" x2="${W}" y2="${i * H / 5}"/>`;
  const lines = legs.map((l, i) => { const c = modeColor(l.decision && l.decision.chosenMode, i); return `<line x1="${sx(l.fromLng)}" y1="${sy(l.fromLat)}" x2="${sx(l.toLng)}" y2="${sy(l.toLat)}" stroke="${c}" stroke-width="4" stroke-linecap="round" opacity="0.92"/>`; }).join("");
  const stops = pts.map((p) => { const x = sx(p[1]), y = sy(p[0]); return `<g><circle cx="${x}" cy="${y}" r="7" fill="#0b0e14" stroke="#5b9dff" stroke-width="3"/><text x="${x + 11}" y="${y + 4}" fill="#e6ebf2" font-size="13">${esc(p[2])}</text></g>`; }).join("");
  return `<div class="map-fallback"><svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid meet" width="100%" height="100%" font-family="-apple-system,Segoe UI,Roboto,sans-serif"><rect width="${W}" height="${H}" fill="#0d1118"/><g stroke="#1a2130" stroke-width="1">${grid}</g><path d="M0,${H * 0.7} C ${W * 0.3},${H * 0.55} ${W * 0.55},${H * 0.9} ${W},${H * 0.6}" stroke="#16314f" stroke-width="16" fill="none" opacity="0.5"/>${lines}${stops}</svg><span class="mf-cap">🗺 schematic preview — live map tiles couldn't load here</span></div>`;
}
function showMapPlaceholder(itin) {
  state.mapGaveUp = true;
  if (state.routeMap) { try { state.routeMap.remove(); } catch (e) {} state.routeMap = null; }
  $("#routeMap").innerHTML = mapPlaceholderSvg(itin);
  $("#mapSub").textContent = `${(itin.legs || []).length} legs · schematic preview`;
}
function initRouteMap() {
  if (state.routeMap) return;
  state.routeMap = L.map("routeMap", { zoomControl: true, scrollWheelZoom: true });
  const tiles = L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    maxZoom: 19,
  });
  tiles.on("load", () => { state.tilesOk = true; });
  tiles.addTo(state.routeMap);
  state.routeLayer = L.layerGroup().addTo(state.routeMap);
  state.routeMap.setView(LONDON, 11);
}

function markerIcon(kind, num, color) {
  const cls = kind === "origin" ? "map-pin origin" : "map-pin dest";
  return L.divIcon({
    className: "",
    html: `<div class="${cls}" style="--pin-color:${color}"><span>${num}</span></div>`,
    iconSize: [26, 26],
    iconAnchor: [13, 13],
  });
}

async function renderRouteMap(itin) {
  if (typeof L === "undefined" || state.mapGaveUp) { showMapPlaceholder(itin); return; }
  initRouteMap();
  state.routeLayer.clearLayers();

  const legs = (itin.legs || []).filter(legHasCoords);
  const legend = $("#mapLegend");
  legend.innerHTML = "";

  let segmentCount = 0;
  for (const leg of legs) {
    const segs = leg.decision?.segments;
    segmentCount += segs?.length ? segs.length : 1;
  }

  $("#mapSub").textContent = legs.length
    ? `${legs.length} leg${legs.length === 1 ? "" : "s"} · ${segmentCount} segment${segmentCount === 1 ? "" : "s"} · click for details`
    : "no coordinates for this day";

  if (legs.length === 0) {
    state.routeMap.setView(LONDON, 11);
    setTimeout(() => state.routeMap.invalidateSize(), 80);
    return;
  }

  const bounds = L.latLngBounds([]);
  const markerKeys = new Set();

  for (let i = 0; i < legs.length; i++) {
    const leg = legs[i];
    const segments = leg.decision?.segments?.length ? leg.decision.segments : null;

    if (segments?.length) {
      for (let si = 0; si < segments.length; si++) {
        const seg = segments[si];
        const color = modeColor(seg.mode, seg.order ?? si);
        const { from, to } = resolveSegmentEndpoints(leg, segments, si);
        const points = await segmentPolylinePoints(from, to, seg.mode);
        const popup = segmentPopupHtml(seg, i);
        drawSegmentLine(points, color, seg.mode, popup);

        for (const pt of points) bounds.extend(pt);

        for (const [lat, lng, label] of [
          [from[0], from[1], seg.fromLabel],
          [to[0], to[1], seg.toLabel],
        ]) {
          const key = `${lat.toFixed(5)}:${lng.toFixed(5)}`;
          if (markerKeys.has(key)) continue;
          markerKeys.add(key);
          L.circleMarker([lat, lng], {
            radius: 4, color, fillColor: color, fillOpacity: 0.85, weight: 2,
          }).bindPopup(`<div class="map-popup"><div class="map-popup-title">${label || "Stop"}</div></div>`, { className: "dark-popup" })
            .addTo(state.routeLayer);
        }

        legend.appendChild(el(
          `<span class="legend-item" title="${seg.fromLabel || ""} → ${seg.toLabel || ""}">
            <span class="legend-swatch" style="background:${color}"></span>
            ${seg.mode} ${seg.durationMin}m
          </span>`
        ));
      }

      const legColor = modeColor(leg.decision.chosenMode, i);
      for (const [lat, lng, label, kind] of [
        [leg.fromLat, leg.fromLng, leg.fromLabel, "origin"],
        [leg.toLat, leg.toLng, leg.toLabel, "dest"],
      ]) {
        const key = `${lat.toFixed(5)}:${lng.toFixed(5)}:${kind}`;
        if (markerKeys.has(key)) continue;
        markerKeys.add(key);
        L.marker([lat, lng], { icon: markerIcon(kind, i + 1, legColor) })
          .bindPopup(`<div class="map-popup"><div class="map-popup-title">${label || kind}</div><div class="map-popup-row"><span>Leg</span><b>${i + 1}</b></div></div>`, { className: "dark-popup" })
          .addTo(state.routeLayer);
      }
    } else {
      const from = [leg.fromLat, leg.fromLng];
      const to = [leg.toLat, leg.toLng];
      const mode = leg.decision ? leg.decision.chosenMode : null;
      const color = modeColor(mode, i);
      const popup = legPopupHtml(leg, i);
      const points = await segmentPolylinePoints(from, to, mode || "mixed");
      drawSegmentLine(points, color, mode || "mixed", popup);

      for (const pt of points) bounds.extend(pt);

      for (const [lat, lng, label, kind] of [
        [from[0], from[1], leg.fromLabel, "origin"],
        [to[0], to[1], leg.toLabel, "dest"],
      ]) {
        const key = `${lat.toFixed(5)}:${lng.toFixed(5)}:${kind}`;
        if (markerKeys.has(key)) continue;
        markerKeys.add(key);
        L.marker([lat, lng], { icon: markerIcon(kind, i + 1, color) })
          .bindPopup(`<div class="map-popup"><div class="map-popup-title">${label || kind}</div><div class="map-popup-row"><span>Leg</span><b>${i + 1}</b></div></div>`, { className: "dark-popup" })
          .addTo(state.routeLayer);
      }

      legend.appendChild(el(`<span class="legend-item"><span class="legend-swatch" style="background:${color}"></span>${leg.fromLabel || "?"} → ${leg.toLabel || "?"} · ${mode || "—"}</span>`));
    }
  }

  state.routeMap.fitBounds(bounds, { padding: [36, 36], maxZoom: 15 });
  setTimeout(() => state.routeMap.invalidateSize(), 80);
  setTimeout(() => { if (!state.tilesOk) showMapPlaceholder(itin); }, 3500);
}

/* ---------------- Timeline ---------------- */
async function renderTimeline(itin) {
  const wrap = $("#timeline");
  $("#timelineSub").textContent = itin.date + (itin.legs.length ? ` · ${itin.totalPredictedWastedMin} min wasted` : "");
  await renderRouteMap(itin);
  if (!itin.legs || itin.legs.length === 0) {
    wrap.innerHTML = `<div class="empty">No legs planned for this day.<br/>Pick another day or wait for the optimizer to build the itinerary.</div>`;
    return;
  }
  wrap.innerHTML = "";
  for (const leg of itin.legs) wrap.appendChild(renderLeg(leg));
}

function renderLeg(leg) {
  const d = leg.decision;
  const chosenMode = d ? d.chosenMode : null;
  const segs = d && d.segments ? d.segments : [];
  const maps = legMapsUrl(leg);

  let segHtml = "";
  if (segs.length > 0) {
    segHtml = `<div class="segments">` + segs.map((s, i) => {
      const color = modeColor(s.mode, s.order ?? i);
      return `
      ${i > 0 ? `<span class="seg-conn" style="background:${color}"></span>` : ""}
      <div class="seg">
        <div class="seg-chip" style="--seg-color:${color}; border-color:${color}55" title="${(s.summary || "").replace(/"/g, "")}">
          ${modeIcon(s.mode)}
          <span class="seg-mode" style="color:${color}">${s.mode}</span>
          <span class="seg-min">${s.durationMin}m</span>
        </div>
      </div>`;
    }).join("") + `</div>`;
  } else if (chosenMode) {
    segHtml = `<div class="segments"><div class="seg"><div class="seg-chip">
      ${modeIcon(chosenMode)}<span class="seg-mode">${chosenMode}</span></div></div></div>`;
  }

  const chosenPred = (leg.options || []).find((p) => p.mode === chosenMode);
  const conf = chosenPred ? chosenPred.confidence : 0;

  const badge = d
    ? `<span class="chosen-badge ${d.wasExploration ? "explore" : ""}">${modeIcon(chosenMode)} ${chosenMode}${d.wasExploration ? " · explore" : ""}</span>`
    : `<span class="chosen-badge">no decision</span>`;

  const alts = (leg.options || [])
    .filter((p) => p.mode !== chosenMode)
    .sort((a, b) => (a.calibratedDurationMin + a.waitMin) - (b.calibratedDurationMin + b.waitMin))
    .map((p) => `<div class="alt-row ${p.feasible ? "" : "infeasible"}">
        <span class="alt-mode">${modeIcon(p.mode)} ${p.mode} ${p.mode === "mixed" ? '<span class="tag-mixed">composite</span>' : ""}</span>
        <span class="alt-dur">${p.calibratedDurationMin + p.waitMin} min</span>
        <span style="color:var(--text-faint)">·</span>
        <span>conf ${pct(p.confidence)}</span>
      </div>`).join("");

  const node = el(`<div class="leg">
    <div class="leg-head">
      <div class="leg-route">
        <span>${leg.fromLabel || "Start"}</span>
        <span class="arrow">→</span>
        <span>${leg.toLabel || "End"}</span>
      </div>
      ${badge}
    </div>
    ${segHtml}
    <div class="leg-foot">
      <div class="leg-times">depart <b>${d ? fmtTime(d.recommendedDeparture) : "—"}</b> · arrive <b>${d ? fmtTime(d.predictedArrival) : "—"}</b> · by ${fmtTime(leg.arriveBy)}</div>
      <div class="conf" title="confidence">
        <span class="wasted">wasted <b>${d ? d.predictedWastedMin : "—"} min</b></span>
        <div class="conf-bar"><div class="conf-fill" style="width:${Math.round(conf * 100)}%"></div></div>
        <span class="conf-val">${pct(conf)}</span>
      </div>
    </div>
    ${alts ? `<div class="alts"><div class="alts-title">Alternatives considered</div>${alts}</div>` : ""}
    ${maps ? `<div class="leg-actions"><a class="maps-link" href="${maps}" target="_blank" rel="noopener">↗ Open route in Maps</a></div>` : ""}
  </div>`);
  return node;
}

/* ---------------- Source health ---------------- */
function sparkline(canvas, values, color) {
  if (!values || values.length === 0) return;
  new Chart(canvas, {
    type: "line",
    data: { labels: values.map((_, i) => i), datasets: [{ data: values, borderColor: color, borderWidth: 1.5, pointRadius: 0, tension: .35, fill: false }] },
    options: { responsive: false, animation: false, plugins: { legend: { display: false }, tooltip: { enabled: false } },
      scales: { x: { display: false }, y: { display: false, min: 0, max: 1 } } }
  });
}

function renderSources(sources) {
  const wrap = $("#sources");
  if (!sources || sources.length === 0) { wrap.innerHTML = `<div class="empty">No sources yet.</div>`; return; }
  wrap.innerHTML = "";
  const colorFor = (st) => st === "healthy" ? "#4ade80" : st === "degraded" ? "#fbbf24" : "#f87171";
  for (const s of sources) {
    const row = el(`<div class="source-row">
      <div class="source-id">${modeIcon(s.mode)}<span class="name">${s.mode}</span></div>
      <span class="pill ${s.state}">${s.state}</span>
      <div class="source-metrics">
        <div class="metric"><div class="m-label">success</div><div class="m-val">${pct(s.ewmaSuccessRate)}</div></div>
        <div class="metric"><div class="m-label">MAPE</div><div class="m-val">${s.avgMape > 0 ? pct(s.avgMape) : "—"}</div></div>
        <canvas class="spark" width="70" height="26"></canvas>
      </div>
    </div>`);
    wrap.appendChild(row);
    // a small synthetic trend around the current EWMA so the sparkline reads at a glance
    const base = s.ewmaSuccessRate;
    const vals = Array.from({ length: 8 }, (_, i) => Math.min(1, Math.max(0, base + Math.sin(i + base * 6) * 0.06)));
    vals[vals.length - 1] = base;
    sparkline(row.querySelector(".spark"), vals, colorFor(s.state));
  }
}

/* ---------------- Corridors ---------------- */
async function renderCorridors() {
  const list = $("#corridorList");
  let rows;
  try { rows = await api("/api/corridors"); }
  catch (e) { list.innerHTML = `<div class="empty">Failed to load corridors.</div>`; return; }

  $("#corridorsSub").textContent = `${rows.length} corridor${rows.length === 1 ? "" : "s"} learned`;
  if (rows.length === 0) {
    list.innerHTML = `<div class="empty">No corridors yet — the probe job gathers these over time.</div>`;
    $("#chartEmpty").classList.remove("hidden");
    return;
  }

  list.innerHTML = "";
  rows.forEach((c, idx) => {
    const item = el(`<button class="corridor-item" data-key="${c.corridorKey}">
      <div class="ck">${c.corridorKey}</div>
      <div class="meta">
        <span>${c.modes.length} mode${c.modes.length === 1 ? "" : "s"}</span>
        <span>${c.totalSampleCount} samples</span>
        <span>${c.modelCount} learned</span>
        ${c.avgMape > 0 ? `<span>MAPE ${pct(c.avgMape)}</span>` : ""}
      </div>
    </button>`);
    item.addEventListener("click", () => selectCorridor(c.corridorKey));
    list.appendChild(item);
  });

  const initial = state.corridorKey && rows.some((r) => r.corridorKey === state.corridorKey)
    ? state.corridorKey : rows[0].corridorKey;
  selectCorridor(initial);
}

async function selectCorridor(key) {
  state.corridorKey = key;
  document.querySelectorAll(".corridor-item").forEach((n) => n.classList.toggle("active", n.dataset.key === key));
  $("#chartTitle").textContent = `Predicted time by hour · ${key}`;

  let samples;
  try { samples = await api(`/api/corridors/${encodeURIComponent(key)}/samples?limit=500`); }
  catch { samples = []; }

  const empty = $("#chartEmpty");
  if (!samples || samples.length === 0) { empty.classList.remove("hidden"); if (state.hourChart) { state.hourChart.destroy(); state.hourChart = null; } return; }
  empty.classList.add("hidden");
  drawHourChart(samples);
}

function drawHourChart(samples) {
  const order = ["night", "am_peak", "midday", "pm_peak", "evening"];
  const label = { night: "Night", am_peak: "AM peak", midday: "Midday", pm_peak: "PM peak", evening: "Evening" };
  const modes = [...new Set(samples.map((s) => s.mode))];
  const palette = { walk: "#22c55e", tube: "#3b82f6", bus: "#f59e0b", rail: "#8b5cf6", cycle: "#06b6d4", mixed: "#94a3b8", overground: "#8b5cf6", dlr: "#8b5cf6" };

  const datasets = modes.map((m) => {
    const data = order.map((b) => {
      const xs = samples.filter((s) => s.mode === m && s.hourBucket === b).map((s) => s.predictedDurationMin);
      return xs.length ? Math.round(xs.reduce((a, c) => a + c, 0) / xs.length) : null;
    });
    return { label: m, data, backgroundColor: (palette[m] || "#8893a7") + "cc", borderRadius: 4, borderSkipped: false };
  });

  if (state.hourChart) state.hourChart.destroy();
  state.hourChart = new Chart($("#hourChart"), {
    type: "bar",
    data: { labels: order.map((b) => label[b]), datasets },
    options: {
      responsive: true, maintainAspectRatio: false,
      plugins: { legend: { labels: { color: "#93a0b4", boxWidth: 12, font: { size: 11 } } },
        tooltip: { callbacks: { label: (c) => ` ${c.dataset.label}: ${c.parsed.y ?? "—"} min` } } },
      scales: {
        x: { grid: { color: "#1a2130" }, ticks: { color: "#93a0b4", font: { size: 11 } } },
        y: { grid: { color: "#1a2130" }, ticks: { color: "#93a0b4", font: { size: 11 } }, title: { display: true, text: "minutes", color: "#65718a", font: { size: 10 } } }
      }
    }
  });
}

/* ---------------- Learning queue ---------------- */
async function renderLearning() {
  const wrap = $("#learning");
  let rows;
  try { rows = await api("/api/adjustments?status=proposed"); }
  catch { wrap.innerHTML = `<div class="empty">Failed to load proposals.</div>`; return; }

  if (!rows || rows.length === 0) {
    wrap.innerHTML = `<div class="empty">No pending proposals.<br/>The reflection agent surfaces tuning ideas here.</div>`;
    return;
  }
  wrap.innerHTML = "";
  for (const a of rows) {
    const card = el(`<div class="adj">
      <div class="adj-top">
        <span class="adj-kind">${a.kind}</span>
        <span class="adj-improve">▲ ${a.shadowImprovementMin.toFixed(1)} min/day</span>
      </div>
      <div class="adj-change">${a.change}</div>
      <div class="adj-rationale">${a.rationale}</div>
      <div class="adj-actions">
        <button class="btn primary tiny" data-id="${a.id}" data-approve="true">Approve</button>
        <button class="btn ghost tiny" data-id="${a.id}" data-approve="false">Dismiss</button>
      </div>
    </div>`);
    card.querySelectorAll("button[data-id]").forEach((b) =>
      b.addEventListener("click", () => decide(a.id, b.dataset.approve === "true")));
    wrap.appendChild(card);
  }
}

async function decide(id, approve) {
  try {
    await api(`/api/adjustments/${id}/approve`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ approve }),
    });
    toast(approve ? "Adjustment approved & promoted" : "Adjustment dismissed");
    await renderLearning();
    loadOverview();
  } catch (e) { toast("Action failed: " + e.message, true); }
}

/* ---------------- System bar ---------------- */
function renderSystem(sys) {
  const bar = $("#sysbar");
  const r = sys.reachability;
  const reach = (label, ok) => `<span class="reach"><span class="dot ${ok ? "ok" : "bad"}"></span>${label}</span>`;
  const jobs = sys.jobs.length
    ? sys.jobs.map((j) => `<span class="job-chip" title="${j.note || ""}"><span class="dot ${j.success ? "ok" : "bad"}"></span><b>${j.job.replace(/Job$/, "")}</b> ${fmtAgo(j.lastRunUtc)}</span>`).join("")
    : `<span class="reach" style="color:var(--text-faint)">no jobs have run yet</span>`;
  bar.innerHTML = `
    <div class="sys-group"><span class="sys-label">Reachability</span>
      ${reach("DB", r.database)} ${reach("TfL", r.tfl)} ${reach("LLM", r.llm)}</div>
    <div class="sys-group" style="flex-wrap:wrap;gap:8px">${jobs}</div>
    <span class="sys-time">server ${fmtTime(sys.serverTimeUtc)} · updated ${new Date().toLocaleTimeString()}</span>`;
}

/* ---------------- orchestration ---------------- */
async function loadOverview() {
  let o;
  try { o = await api("/api/dashboard/overview"); }
  catch (e) { toast("Failed to load overview: " + e.message, true); return; }

  if (!state.date) { state.date = o.date; $("#datePicker").value = o.date; }
  renderKpis(o);
  renderSources(o.sources);
  renderSystem(o.system);
}

async function loadItinerary() {
  const wrap = $("#timeline");
  try {
    const itin = await api(`/api/itineraries/${state.date}`);
    renderTimeline(itin);
  } catch (e) {
    wrap.innerHTML = `<div class="empty">Could not load itinerary for ${state.date}.</div>`;
  }
}

async function refreshAll() {
  await loadOverview();
  await Promise.all([loadItinerary(), renderCorridors(), renderLearning()]);
}

function scheduleAuto() {
  if (state.timer) clearInterval(state.timer);
  state.timer = setInterval(() => { loadOverview(); loadItinerary(); renderLearning(); }, REFRESH_MS);
}

document.addEventListener("DOMContentLoaded", () => {
  $("#refreshBtn").addEventListener("click", refreshAll);
  $("#datePicker").addEventListener("change", (e) => { state.date = e.target.value; loadItinerary(); });
  refreshAll();
  scheduleAuto();
});
