# 01 — Concept

## The problem

People with back-to-back London meetings repeatedly face four questions, and existing tools answer at most one:

1. **Can I make it?** — Is the next meeting even feasible given where I am now and where I need to be?
2. **When do I leave?** — What's the exact "leave-by" time for the next leg, accounting for door-to-door reality (station entry, taxi pickup, exit slack)?
3. **Is today worse than normal for this season?** — A 35-minute trip might be perfectly normal at 08:30 in June, but a disaster at 11:00. Raw ETAs hide this.
4. **Why?** — If today is abnormal, what is actually causing it, and is the cause *verified* or just a scary headline?

Maps apps give you a single ETA. Calendars give you fixed times. **Nobody connects the two and tells you, proactively, that *today* is abnormal and *why* — before you're already late.**

## The solution

**RouteOps is an autonomous day-routing agent.** It:

- **Reads your calendar** and builds your day as an ordered set of journey **legs**. Meeting times are **FIXED**, so this is *feasibility + ETA prediction*, **NOT** a travelling-salesman reordering problem. We never reshuffle your meetings; we tell you whether the schedule holds.
- **Computes traffic-aware travel vs a seasonal baseline** for each leg — comparing live conditions against what's *normal for this hour, day-of-week, and month*.
- **Ingests verified disruptions** from TfL, weather, and news feeds.
- **Attributes excess delay** to specific, cross-verified causes — never inventing a reason from a single unverified headline.
- **Sends proactive WhatsApp nudges** ("leave by 14:12 for your 15:00") and updates them as conditions change.
- **Escalates to a human** only when the day becomes genuinely impossible (e.g. no feasible departure makes the next meeting).

The differentiator is the **seasonal baseline + verified attribution**: we don't just say "26 min." We say *"normal for this leg right now is 26 minutes; today it's 48; here's the verified cause."*

## Hackathon fit

| Judging criterion | How RouteOps scores |
| --- | --- |
| **Technical execution** | Real Google Calendar + Routes integration, dual baseline/traffic-aware routing, multi-source event clustering, deterministic trust scoring. |
| **Product thinking** | Answers the four real questions (feasible? leave-by? abnormal? why?) instead of a bare ETA; clear B2B wedge. |
| **Agent autonomy** | Five self-evaluating, self-healing agents run a 5-minute loop end-to-end, acting without prompting. |
| **UX clarity** | One dashboard timeline + a single WhatsApp line: "leave by X". Confidence badges make trust legible. |
| **Real-world applicability** | Solves a daily pain for field-based professionals in a real city with real feeds. |
| **Safety & oversight** | Tiered trust (auto-act only on verified sources), human escalation when the day breaks, never graphic/unverified claims. |

## Business angle

RouteOps is a **B2B micro-SaaS** for people whose job *is* moving around a city to fixed appointments:

- Field sales reps
- Estate agents
- Locum clinicians and home-visit healthcare
- Field service / inspections

**Pricing:** ~**£2–5/day** pay-as-you-go, or **£29/mo** per seat. For the hackathon we demo billing live with **one PayPal sandbox payment** to prove the loop end-to-end.
