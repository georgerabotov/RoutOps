# 07 — Data sources

> ⚠️ **Partly superseded.** The source-tier / trust ideas still inform the design, but the wiring
> (Supabase, Wassist/WhatsApp) is not what shipped. Actual integrations: **TfL Unified API, Postgres,
> OpenAI** — see [`00-AS-BUILT.md`](00-AS-BUILT.md).

## Source tiers

| Tier | Sources | Trust action |
| --- | --- | --- |
| **Tier 1 — official** | TfL, Met Office, National Highways, police.gov.uk | **Auto-adjust** routing on a single source |
| **Tier 2 — reputable news** | BBC, Guardian, Evening Standard, Reuters, Independent | Act only if **≥ 2 agree** |
| **Tier 3 — social / aggregators** | X/Twitter, Reddit, unverified blogs | **Never alone** — log only |

## Endpoints to wire

- **Google Calendar** — scope `calendar.readonly`, `events.list` for today's window, **poll every 5 min**.
- **Google Routes** — `computeRoutes` with both `TRAFFIC_AWARE` (observed) and `TRAFFIC_UNAWARE` (baseline); **Geocoding API** for addresses.
- **TfL** — `https://api.tfl.gov.uk/Line/Mode/tube/Status` and `https://api.tfl.gov.uk/Road/disruption`. Free; optional `app_key` raises limit to **500 req/min**. Register at **api-portal.tfl.gov.uk**.
- **Met Office DataHub** *or* **Open-Meteo** — Open-Meteo needs **no key**, ~**10k calls/day**: `https://api.open-meteo.com/v1/forecast`.
- **News RSS** — BBC London + Evening Standard. **Never auto-act on RSS alone** (Tier 2/3 corroboration required).
- **National Rail** — wire only if time permits.

## Free-tier summary

| Service | Cost note |
| --- | --- |
| TfL | Free |
| News RSS | Free |
| Open-Meteo | Free, no key (~10k/day) |
| Supabase | Free tier sufficient |
| Wassist | Sponsor-provided |
| PayPal Sandbox | Fully free |
| **Google Maps Platform** | **Changed March 2025** — see below |

### Google Maps Platform pricing (post–March 2025)

- **No more flat $200/mo credit.** Replaced by **per-SKU monthly free caps**:
  - Routes **Essentials**: ~**10k/mo** free
  - Routes **Pro**: ~**5k/mo** free
  - **Geocoding**: ~**10k/mo** free
- A **GCP billing account is required**, but **hackathon-scale usage stays within free caps**.
- **New GCP accounts get a $300 trial credit** as additional headroom.

## Cost-control tips

- **Cache geocoding** — one row per address, reuse forever.
- **Cache route legs** — don't re-call unchanged legs every cycle.
- Budget ~**2 Routes calls per leg per cycle** (traffic-aware + traffic-unaware).
- **Poll every 5 min**, not continuously.
- Set a **GCP budget alert at £5** so a runaway loop can't surprise you.
