# 05 — Baseline & anomaly

## Honest v1

This is **NOT** a multi-year machine-learning model trained on historical traffic. It's a transparent, defensible heuristic we can build tonight and explain to judges:

- **baseline** = Routes API with `routingPreference: TRAFFIC_UNAWARE` (free-flow, no congestion).
- **observed** = Routes API with `routingPreference: TRAFFIC_AWARE` (or `TRAFFIC_AWARE_OPTIMAL`) plus `departureTime`.
- **seasonal_expected** = `free_flow × hour_multiplier × dow_multiplier × month_multiplier`.

## Anomaly math

```
seasonal_expected = free_flow * hour_mult * dow_mult * month_mult
anomaly_min       = observed - seasonal_expected
anomaly_pct       = anomaly_min / seasonal_expected
```

`anomaly_min` is "how many minutes worse than a normal day for this slot." This is the number we surface to users.

## Status thresholds

| `anomaly_min` | Status |
| --- | --- |
| < 5 | **normal** |
| 5–15 | **elevated** |
| > 15 | **severe** |

## Seasonal multiplier — starting table

Tune during the event; these are sane defaults. Multipliers combine multiplicatively.

| Factor | Condition | Multiplier |
| --- | --- | --- |
| Hour | Weekday AM peak 08:00–09:30 | ×1.25 |
| Hour | Weekday PM peak 17:00–19:00 | ×1.30 |
| Day-of-week | Saturday, central | ×1.15 |
| Month | June | ×1.05 |
| Calendar | School holidays | ×1.10 |
| Month | December (pre-Christmas) | ×1.20 |

Factors not listed default to ×1.00.

## Ground-truth sanity checks

These prevent embarrassing false alarms:

- **Feed says closed but `anomaly_min` < 5** → the disruption isn't actually biting this leg. **Downgrade / suppress the stale alert.**
- **`anomaly_min` > 15 but no event match** → label **`unexplained_congestion`** rather than guessing a cause.
- **Never invent a cause from a single unverified headline.** Attribution requires the trust pipeline (`08-trust-scoring.md`).
