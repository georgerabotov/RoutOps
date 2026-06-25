# 11 — Team roles

Split for 2–3 people. Each person owns a vertical slice that can be built and demoed independently.

## Person A — Calendar + routing

- Google OAuth (`calendar.readonly`) + `events.list`
- Geocoding + geocode cache
- Routes legs + mode selection (`04-travel-modes.md`)
- Baseline / anomaly math + seasonal multipliers (`05-baseline-anomaly.md`)

## Person B — Data + trust

- TfL poller (line status + road disruption)
- Weather poller (Open-Meteo / Met Office)
- Event normalizer + clustering (`06-event-taxonomy.md`)
- Trust scorer + leg attribution (`08-trust-scoring.md`)

## Person C — Product surface

- Supabase schema (`09-database-schema.md`)
- Dashboard timeline UI
- Wassist WhatsApp templates (leave-by, escalation)
- Loom demo + optional PayPal sandbox

## Solo-builder order

If building alone, follow the never-cut path first, then widen:

1. Calendar OAuth + read today (Person A)
2. Geocode + Routes legs + leave-by (Person A)
3. Baseline vs anomaly (Person A)
4. One TfL feed + simple trust score + attribution (Person B)
5. One dashboard timeline view (Person C)
6. One WhatsApp leave-by (Person C)
7. Polish / optional PayPal

## Mapping the 5 agents to people

See `12-five-agents.md` for full specs.

| Agent | Owner |
| --- | --- |
| **Orchestrator** | You |
| **Route Engineer** | You |
| **Calendar Scout** | Partner |
| **Horizon Watcher** | Partner |
| **Comms Dispatcher** | Either (with dashboard) |

## Copy-paste partner onboarding message

```
Hey! We're building RouteOps for the Cursor Hands-Off Hackathon tonight.

It's an autonomous day-routing agent: reads your Google Calendar, compares each
journey leg vs a seasonal "normal", verifies disruptions across TfL/weather/news,
and WhatsApps you when to leave — escalating to a human only when the day breaks.

Repo: https://github.com/analogue-tools/RoutOps
Start here:
  1. Read docs/01-concept.md and docs/03-build-plan.md
  2. cp .env.example .env  (I'll send keys privately — never commit .env)
  3. Pick a lane in docs/11-team-roles.md (I've got Orchestrator + Route Engineer;
     can you take Calendar Scout + Horizon Watcher?)

Code freeze is 21:30. Shout if anything's unclear!
```

## Partner checklist

- [ ] Cloned the repo, read `01-concept.md` + `03-build-plan.md`
- [ ] `.env` created from `.env.example` (keys received privately, **not committed**)
- [ ] Picked a lane / agent(s) in this doc
- [ ] Can run the project locally
- [ ] Knows the never-cut path and the 21:30 freeze
