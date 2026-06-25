# RouteOps

> **Autonomous day-routing agents: Google Calendar → seasonal baseline vs live traffic → verified TfL/weather events → WhatsApp leave-by alerts.**

RouteOps is a self-running "day routing company" built for the **Cursor Hands-Off Hackathon (London, June 2026)**. An AI agent system reads your Google Calendar, compares each journey leg against a seasonally normal baseline, detects abnormal delay, attributes causes using cross-verified TfL/weather/news events, and proactively texts you when to leave — with human oversight only when the day breaks.

> **Pitch:** *"We don't just predict your ETA — we tell you when today is abnormal for this time of year, and why, before you miss the meeting."*

---

## Docs

| Doc | Topic |
| --- | --- |
| [docs/01-concept.md](docs/01-concept.md) | Product vision, judging-criteria fit, business angle |
| [docs/00-AS-BUILT.md](docs/00-AS-BUILT.md) | **As-built architecture (authoritative) — start here** |
| [docs/02-architecture.md](docs/02-architecture.md) | ⚠️ superseded — early Next.js/Supabase design |
| [docs/03-build-plan.md](docs/03-build-plan.md) | Step-by-step build order, tonight timeline, cut list |
| [docs/04-travel-modes.md](docs/04-travel-modes.md) | Travel modes, taxi buffers, leave-by formula |
| [docs/05-baseline-anomaly.md](docs/05-baseline-anomaly.md) | Normal vs abnormal, seasonal multipliers |
| [docs/06-event-taxonomy.md](docs/06-event-taxonomy.md) | Event categories and normalized schema |
| [docs/07-data-sources.md](docs/07-data-sources.md) | APIs, endpoints, free-tier notes, cost control |
| [docs/08-trust-scoring.md](docs/08-trust-scoring.md) | Cross-source verification / anti-fake-news |
| [docs/09-database-schema.md](docs/09-database-schema.md) | Supabase schema (SQL) |
| [docs/10-demo-script.md](docs/10-demo-script.md) | 2-minute demo script |
| [docs/11-team-roles.md](docs/11-team-roles.md) | Team split, solo order, onboarding message |
| [docs/12-five-agents.md](docs/12-five-agents.md) | Five self-evaluating, self-healing agents |

---

## MVP scope

- [ ] Google Calendar OAuth + read today's events
- [ ] Google Routes API legs (chronological, fixed times)
- [ ] Travel modes: TRANSIT / DRIVE + taxi buffer / WALK
- [ ] Baseline free-flow vs traffic-aware anomaly + seasonal multipliers
- [ ] TfL + Met Office/Open-Meteo event feeds
- [ ] Trust scoring (Tier1 auto-act, Tier2 corroboration)
- [ ] WhatsApp outbound via Wassist
- [ ] Simple dashboard
- [ ] Optional: one PayPal sandbox payment

---

## Sponsor stack

| Sponsor | Role in RouteOps |
| --- | --- |
| **Google Cloud** | Calendar API, Routes API, Geocoding |
| **Supabase** | State store / message bus |
| **Wassist** | WhatsApp outbound alerts |
| **PayPal Sandbox** | Billing demo (one sandbox payment) |
| **Cursor / Modal** | Build & deploy (hands-off agent dev, cron/webhooks) |
| **Manus AI** | Optional QA agent |

---

## Getting started as a partner

1. Read [docs/01-concept.md](docs/01-concept.md) for the vision and [docs/03-build-plan.md](docs/03-build-plan.md) for the build order.
2. Copy `.env.example` to `.env` and fill in your keys. **Get keys privately — never commit `.env`.**

   ```bash
   cp .env.example .env
   ```

3. Pick a lane in [docs/11-team-roles.md](docs/11-team-roles.md) and grab the matching agent(s) in [docs/12-five-agents.md](docs/12-five-agents.md).
