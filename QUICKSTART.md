# QUICKSTART — run the demo in minutes

Fastest path to a **running, autonomous** TravelOptimizer demo. Deliberately sidesteps the
slow dependencies: by seeding calendar events that **already have coordinates**, you skip
**both** Google OAuth **and** the LLM geocoder. Only Postgres is a hard requirement.

## Prerequisites
- **.NET 10 SDK** (`dotnet --version` → 10.x). The solution targets `net10.0`.
- **Docker** (for Postgres) — or a local Postgres matching the connection string below.

## 1. Start Postgres
```bash
docker compose up -d        # from repo root; uses docker-compose.yml
```
This serves Postgres on `localhost:5432` with `admin/root` + DB `TravelOptimizer`,
matching `appsettings.example.json`.

## 2. Configure the app
```bash
cd src/TravelOptimizer.Api
cp appsettings.example.json appsettings.json
```
Then set **a non-empty** `Travel:OpenAI:ApiKey` in `appsettings.json`. A placeholder
(e.g. `"sk-demo-placeholder"`) is enough to get the app to start — with seeded coordinates
the LLM geocoder is never called. Use a **real** OpenAI key only if you want the nightly
Reflection loop or address geocoding to actually run.

> Google (`Travel:Google:*`) can stay blank for this demo — calendar sync is skipped, and we
> seed events directly instead (step 3).

## 3. Seed demo events (no OAuth)
The seeder creates a dev user (`founder@example.com`) on first boot. The demo seed also
inserts a handful of **London `CalendarEvent` rows with lat/lng** for today, e.g.:

| Time | Place | Lat, Lng |
|------|-------|----------|
| 09:00 | King's Cross | 51.5308, -0.1238 |
| 11:00 | Canary Wharf | 51.5054, -0.0235 |
| 14:00 | Soho Square | 51.5152, -0.1322 |
| 16:30 | Liverpool Street | 51.5178, -0.0823 |

(Seeded via `TravelSeeder` so it runs automatically on boot — see step 5 status.)

## 4. Run
```bash
dotnet run --project src/TravelOptimizer.Api
# API on http://localhost:5252  (https on 7029)
```
On boot it auto-migrates the DB and seeds. Within ~1 minute `OptimizeDayJob` fires:
builds legs between the seeded events → queries **live TfL** → calibrates → picks mode +
leave-by, persisting a `TravelDecision` per leg.

## 5. See it work
```bash
# today's optimised itinerary (chosen mode, leave-by, predicted arrival per leg)
curl http://localhost:5252/api/itineraries/2026-06-25 | jq

# log an actual outcome → watch Layer-1 calibration update the corridor model
curl -X POST http://localhost:5252/api/legs/{decisionId}/outcome \
  -H 'Content-Type: application/json' \
  -d '{"actualDurationMin": 34, "arrivedOnTime": true}'

# proposed self-improvement adjustments (populated by the nightly Reflection job)
curl 'http://localhost:5252/api/adjustments?status=proposed' | jq
```

## The autonomy story (what to show judges)
1. **Jobs run unattended** — `OptimizeDayJob` (every min), `MonitorJob` + `CalendarSync`
   (30 min), `CalibrationJob` (hourly), `ReflectionJob` (nightly 2 AM). Console logs prove it.
2. **It learns** — log an outcome, watch `CorridorModel.CorrectionFactor` move and the next
   prediction shift toward reality.
3. **It self-improves with a safety gate** — Reflection proposes weight changes, backtests
   them, auto-promotes low-risk ones and queues the rest for a one-tap human approval.

## Status / not-yet-wired
- **Calendar-link output** (write a "Leave by" event + Google Maps deep link back to the
  calendar) — in progress; needs the OAuth scope upgraded to `calendar.events` (read+write).
  Until then output is the JSON API above.
- **Driving mode, PayPal "made money"** — not built (decisions pending).

## Troubleshooting
- `dotnet` not found / wrong version → install the **.NET 10** SDK.
- DB connection refused → `docker compose ps` (is Postgres healthy?); check the connection string.
- App won't start, OpenAI error → set a non-empty `Travel:OpenAI:ApiKey`.
- Empty itinerary → confirm seeded events exist for the date you're querying and have coordinates.
