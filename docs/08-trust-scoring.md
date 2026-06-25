# 08 — Trust scoring (cross-source verification / anti-fake-news)

The trust layer is what lets RouteOps act autonomously without amplifying rumours. **Trust is a deterministic scoring function — not an LLM judgment.**

## Pipeline

```
ingest feeds → normalize → cluster (category + place + 2h window) → trust score → action
```

## Clustering rule

Two normalized events join the same cluster when **all** hold:

- **same category**, and
- within a **2-hour** window, and
- **distance < 500 m** **OR** **≥ 2 shared `location_tokens`**.

Each cluster gets a `cluster_id` and is scored as a unit.

## Trust score formula

Start at **0**, then add:

| Signal | Delta |
| --- | --- |
| Any **Tier 1** source present | **+0.5** |
| **≥ 2 distinct Tier 2** allowlist sources | **+0.4** |
| Routes **anomaly on the matched leg > 10 min** | **+0.2** |

Caps / floors:

- If the cluster is **Tier 3 only** → **cap at 0.3**.
- **Tier 3 only with no Tier 1/2** → **0.1** (never auto-act).

## Actions

| Trust score | Action |
| --- | --- |
| **≥ 0.7** | **Auto-add delay** to the leg + send WhatsApp |
| **0.4 – 0.69** | **FYI only** — "possible disruption", no auto-adjust |
| **< 0.4** | **Dashboard log only** |

## Matching events to legs

A cluster attaches to a leg when:

- **Corridor proximity** — event within the leg's geographic corridor / radius.
- **Keyword overlap** — shared `location_tokens` (line name, road, area).
- **Mode relevance** — transit_failure → TRANSIT legs; road_closure/traffic → DRIVE legs; weather → all.

## Reputable outlet allowlist (Tier 2)

`BBC`, `The Guardian`, `Evening Standard`, `Reuters`, `The Independent`. Only these count toward the "≥ 2 Tier 2" bonus.

## Demo badges

- **"Verified (3 sources)"** — green, used for routing.
- **"Unverified — ignored for routing"** — grey, logged only.

These badges make the safety story legible to judges in one glance.

## LLM role (kept small)

The LLM is used **only** to:

1. **Classify** a raw headline → `{ category, location_tokens }`.
2. **Dedupe** near-identical headlines.

**It never decides trust or whether to act.** Trust is the deterministic function above.
