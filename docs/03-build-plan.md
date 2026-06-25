# 03 — Build plan

Step-by-step build order. Each phase is shippable; stop wherever the clock forces you and you still have a working demo (see the never-cut path).

## Phases

- **Phase 0 — Scope lock.** Agree the MVP (see README checklist). Confirm one test Google account, one WhatsApp number, home postcode. Lock "no TSP — fixed times" decision.
- **Phase 1 — Accounts & keys.** GCP project + Calendar/Routes/Geocoding enabled + OAuth consent; Supabase project; Wassist token + number; (optional) TfL app_key; PayPal sandbox. Fill `.env`.
- **Phase 2 — Database.** Apply the Supabase schema from `09-database-schema.md`.
- **Phase 3 — Connect Google Calendar.** OAuth (`calendar.readonly`), `events.list` for today, store in `events_cache`.
- **Phase 4 — Geocode & enrich.** Geocode each address (cache one row per address), tag `is_virtual`, extract `location_tokens`.
- **Phase 5 — Routing engine.** Build legs chronologically; call Routes per leg with mode selection (`04-travel-modes.md`); compute `total_leg` and `leave_by`.
- **Phase 6 — Baseline & anomaly.** Add traffic-unaware baseline + seasonal multipliers; compute `anomaly_min`, `anomaly_pct`, status (`05-baseline-anomaly.md`).
- **Phase 7 — Event feeds.** TfL poller; weather (Open-Meteo); optional news RSS. Normalize to the event schema (`06-event-taxonomy.md`).
- **Phase 8 — Trust scoring & attribution.** Cluster events, score trust, match to legs, write `leg_attributions` (`08-trust-scoring.md`).
- **Phase 9 — Agent loop.** Wire the 5-minute orchestrator loop (`02-architecture.md`, `12-five-agents.md`) with self-eval + retry.
- **Phase 10 — WhatsApp alerts.** Wassist outbound leave-by templates; anti-spam (only on change); escalation message.
- **Phase 11 — Dashboard.** Timeline of legs with status badges, verified attribution, leave-by, escalations.
- **Phase 12 — PayPal demo (optional).** One sandbox payment to prove billing.
- **Phase 13 — Demo prep.** Seed calendar, rehearse the 2-minute script (`10-demo-script.md`), record Loom backup.

## Tonight timeline

| Time | Goal |
| --- | --- |
| 18:20 | Phase 0–1: scope locked, keys in `.env`, repos cloned |
| 18:50 | Phase 2–3: DB up, Calendar OAuth reading today's events |
| 19:20 | Phase 4–5: geocoding cached, Routes legs + leave-by working |
| 19:50 | Phase 6: baseline vs anomaly with seasonal multipliers |
| 20:15 | Phase 7–8: TfL feed in, clustered + trust-scored + attributed |
| 20:40 | Phase 9–10: agent loop running, one WhatsApp leave-by sent |
| 21:00 | Phase 11: dashboard timeline view |
| 21:15 | Phase 12–13: optional PayPal, seed demo data, rehearse |
| **21:30** | **Code freeze** |

## Cut in this order if behind

1. PayPal sandbox payment
2. News RSS feed
3. Map UI (keep a plain timeline list)
4. Met Office (Open-Meteo is enough)
5. LLM headline classifier (hard-code categories / keyword match)

## Never cut — minimum path

**Calendar → Routes → leave-by → one TfL alert → one dashboard view.** If only this works, you still have a complete, demoable product.
