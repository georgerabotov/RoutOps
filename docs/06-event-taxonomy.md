# 06 — Event taxonomy

## Normalized event schema

Every feed item — TfL, weather, news — is normalized to this shape before clustering/scoring:

```json
{
  "category": "transit_failure",
  "headline": "Central line: severe delays between Liverpool St and Stratford",
  "location_tokens": ["central line", "liverpool street", "stratford"],
  "lat": 51.5178,
  "lng": -0.0823,
  "radius_m": 800,
  "severity": "high",
  "source_tier": 1,
  "source_name": "TfL",
  "published_at": "2026-06-25T18:42:00Z",
  "url": "https://api.tfl.gov.uk/Line/central/Status"
}
```

## Categories

| Category | Example | Impact | Typical sources |
| --- | --- | --- | --- |
| `traffic_congestion` | "Heavy traffic on A40 Westway" | Road legs slower | TfL Road, National Highways |
| `road_closure` | "Strand closed for filming" | Reroute / blocked | TfL Road, news |
| `transit_failure` | "Victoria line part-suspended" | Transit legs slower/blocked | TfL Line Status |
| `transit_strike` | "Tube strike Thu–Fri" | Major transit loss | TfL, news (Tier1+2) |
| `accident` | "Collision on A12 eastbound" | Sudden road delay | National Highways, police |
| `crime_cordon` | "Police cordon, area closed" | Road/foot blocked | police.gov.uk, news |
| `protest_demonstration` | "March through Westminster" | Central road/foot delay | news, Met Police |
| `major_event` | "Concert at O2", "match at Emirates" | Local surge | venue, news |
| `weather_heat` | "Heat-health alert" | Speed restrictions, comfort | Met Office, Open-Meteo |
| `weather_rain` | "Heavy rain, surface water" | Road + taxi buffer up | Met Office, Open-Meteo |
| `weather_storm_snow` | "Storm / snow warning" | Severe slowdowns | Met Office |
| `utility_works` | "Gas works, lane closure" | Road narrowing | National Highways, council |
| `airport_disruption` | "Heathrow T5 delays" | Airport legs | airport, news |
| `unexplained_congestion` | anomaly with no matched event | Flag, don't attribute | (derived) |

## Notes on sensitive categories

- A **stabbing / violent incident** is handled under **`crime_cordon`** (radius **300–800 m**) — it is **never** a dedicated detector. We model the *area closure*, not the crime.
- **User-facing language:** say **"security incident in area"**, never graphic headlines. Always show a **confidence** indicator so the user can judge.
