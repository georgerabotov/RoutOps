# 04 — Travel modes

## v1 modes

| Mode | Routes `travelMode` | When used |
| --- | --- | --- |
| **TRANSIT** | `TRANSIT` | Default for central London |
| **DRIVE** | `DRIVE` (taxi/car) | Suburbs, late-night, poor transit links |
| **WALK** | `WALK` | Short hops, < 1.2 km |

## Mode selection rules (pseudocode)

```python
def select_mode(leg, now, user_pref):
    if user_pref in ("TRANSIT", "DRIVE", "WALK"):
        return user_pref

    if leg.straight_line_km < 1.2:
        return "WALK"

    if is_central_london(leg.origin) and is_central_london(leg.dest):
        return "TRANSIT"  # default central

    if now.hour >= 23 or now.hour < 5:
        return "DRIVE"    # late-night, sparse transit

    if poor_transit_link(leg.origin, leg.dest):
        return "DRIVE"

    return "TRANSIT"
```

## Taxi / DRIVE pickup buffer

A pickup buffer accounts for hailing/dispatch + wait before the meter trip begins.

| Condition | Buffer (min) |
| --- | --- |
| Base (off-peak) | +3 |
| Peak 07:30–09:30 & 17:00–19:00 | +7 |
| Rain | +5 |
| Major event near pickup | +10 to +15 |
| Airport pickup | +5 / +10 |

Buffers are **additive** (e.g. peak + rain = +12), capped by judgement at +20.

## Leave-by formula

```
total_leg  = routes_duration + pickup_buffer + 5 min exit slack
leave_by   = next_meeting.start - total_leg
```

- `routes_duration` is the traffic-aware Routes value for the selected mode.
- `pickup_buffer` applies to DRIVE/taxi only (0 for TRANSIT/WALK).
- **+5 min exit slack** covers leaving the current building / finding the door.

## Transit extras

- **+5 min** station entry (gates, platform, escalators).
- **+3 min** transfer penalty per interchange.

## Deferred (not in v1)

- Bicycle / e-scooter
- True multimodal stitching (e.g. walk → train → taxi as one optimized leg)
