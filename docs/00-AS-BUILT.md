# 00 — As-built architecture (authoritative)

> **This is the source of truth for what RouteOps actually is.** Several earlier docs
> (02, 03, 07, 09) describe an abandoned Next.js / Vercel / Supabase / WhatsApp design that was
> **never built**. Where they conflict with this doc or the code, the code and this doc win.

## Stack (actual)

| Concern | Built with |
| --- | --- |
| Language / runtime | **.NET 10** (C#), ASP.NET Core Web API |
| Architecture | Clean: `TravelOptimizer.Domain` / `.Persistence` / `.Api` |
| Database | **PostgreSQL** via EF Core + Npgsql (**not** Supabase) |
| Scheduler | **Coravel** in-process jobs (**not** Vercel cron) |
| Travel data | **TfL Unified API** (anonymous; optional app key) + Haversine fallback |
| LLM | **OpenAI `gpt-4o-mini`** behind `IChatCompletionService` (reflection + geocoding) |
| Frontend | Build-free **dashboard SPA** in `wwwroot` (Leaflet + Chart.js, vanilla JS) |
| Output | **Ops dashboard** + per-leg Google Maps deep link (**not** WhatsApp/Wassist) |

## Scheduled jobs (Coravel, `Program.cs`)

| Job | Cadence | Role |
| --- | --- | --- |
| `OptimizeDayJob` | every minute (+ `RunOnceAtStart`) | build today/tomorrow legs → predict → decide |
| `MonitorJob` | every 30 min | re-optimize in-flight legs (3h lookahead) |
| `CalendarSyncJob` | every 30 min | pull Google Calendar events (read-only) |
| `CalibrationJob` | hourly | fold outcomes into corridor models (Layer 1) |
| `ReflectionJob` | hourly, gated to local 2 AM | LLM proposes weight adjustments (Layer 3) |
| `ProbeJob` | every 20 min | corridor probing to gather samples |
| `HealthJob` | every 5 min | source-health self-healing |

## Data flow

```
Google Calendar ──(CalendarSync, read-only)──▶ CalendarEvent
        │  (or seeded London demo events with coords)
        ▼
OptimizeDayJob ─▶ pair consecutive events into TravelLegs (corridor/dayType/hourBucket key)
        ├─ fan out to source agents (tube/bus/rail/walk/cycle + mixed-mode composites) → live TfL
        ├─ Layer-1 calibrate each estimate against the learned CorridorModel (EWMA)
        └─ PolicyService selects mode + leave-by → persists TravelDecision (+ segments)
        ▼
User travels ─▶ POST /api/legs/{id}/outcome ─▶ LegOutcome
        ▼
CalibrationJob (Layer 1)  +  ReflectionJob (Layer 3: LLM proposes → backtest → human approve)
        ▼
Versioned PolicyWeights tune the next decisions.  ProbeJob + HealthJob keep corridors and
source health fresh in the background.
```

## Three-layer learning

1. **Calibration** — per-corridor EWMA correction of actual/predicted (clamped; auto).
2. **Policy** — cost-minimising selection (greedy default; optional Thompson bandit).
3. **Reflection** — LLM proposes weight changes, **shadow-backtested**, auto-promoted only if
   low-risk; buffer/feasibility changes always require a human tap. See `docs/AUDIT.md` for the
   open correctness items on this path.

## API surface

- `GET /api/dashboard/overview` — KPIs, sources, system health (dashboard home)
- `GET /api/itineraries/{date}` · `POST /api/itineraries/optimize`
- `POST /api/legs/{decisionId}/outcome`
- `GET /api/corridors` · `GET /api/corridors/{key}/samples`
- `GET /api/adjustments?status=…` · `POST /api/adjustments/{id}/approve`
- `GET /api/sources` · `GET /api/health`
- `GET/POST /api/calendar/google/{connect,callback,status,sync}` (read-only scope)

## Config / secrets (`appsettings`, `Travel:` section)

- `ConnectionStrings:Default` — Postgres (**required**)
- `Travel:OpenAI:ApiKey` — optional for the seeded demo (blank is safe); needed for reflection /
  address geocoding
- `Travel:Google:*` — optional; only for live calendar connect
- `Travel:Policy:*`, `Travel:Reflection:*` — tuning knobs
- TfL — anonymous; optional app key via `TflAppKeyHandler`

## Running it

- **Engine:** `docker compose up -d` → `dotnet run --project src/TravelOptimizer.Api` →
  dashboard at `http://localhost:5252/`. Seeds 4 London demo events. See `QUICKSTART.md`.
- **Zero-install demo:** open `demo.html` (standalone mirror of the dashboard, sample data).

## What the old docs got wrong (superseded by this one)

- **Not** Next.js / Vercel / Supabase → it's **.NET 10 / Coravel / Postgres**.
- **Not** WhatsApp / Wassist alerts → output is the **ops dashboard** + a per-leg **Maps deep link**.
- Google Calendar is **read-only** (`calendar.events.readonly`).
- The driving/car mode and PayPal "made money" flow in the old PRD are **not built**.
