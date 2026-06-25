# 13 — Timing & anomaly model

> The core of RouteOps. Defines **baseline**, **anomaly**, the **leave-by buffer**, and the
> **timed runs** that fetch data. Supersedes the vaguer "seasonal multiplier" framing in
> `05-baseline-anomaly.md` with a concrete, implementable model.
>
> **Output is the Google Calendar link** (a "Leave by" event + Maps deep link), not WhatsApp.
> The Wassist/`notifications` pieces in docs 02/07/12 are obsolete — see audit.

---

## 1. Definitions

| Term | Meaning |
| --- | --- |
| `baseline` | Google Routes prediction for the leg, queried **at meeting-creation time** with `arrivalTime = meeting start`. Google includes *typical* traffic for that slot. This is "normal." Stored once per leg. |
| `live(t)` | Routes prediction queried at time `t`, traffic-aware, for the relevant departure window. |
| `anomaly(t)` | `live(t) − baseline`. Positive = today is worse than normal for this slot. |
| `anomaly_ratio(t)` | `live(t) / baseline`. Used for thresholds and adaptive buffer. |
| `allowance` | Travel time budget we reserve. See §2. |
| `leave_by` | `meeting_start − allowance`. |

> A leg is `previous location → next meeting location`. Virtual meetings are skipped.

---

## 2. The leave-by buffer equation

Baseline guide (from product owner):

```
allowance = baseline × (1 + 1/3) + 5 min
leave_by  = meeting_start − allowance
```

Worked example: `baseline = 30 min` → `allowance = 40 + 5 = 45 min` → leave 45 min before start.

### Refinement (adaptive multiplier)
The flat `1/3` is the floor. Use the worst credible margin so abnormal days self-correct:

```
margin   = max(1/3, learned_ratio − 1, anomaly_ratio(t) − 1)
predicted = max(baseline, live(t))            // never under-budget vs normal
allowance = predicted × (1 + margin) + 5 min
```

- `learned_ratio` = historical mean of `actual / baseline` for this corridor + time-of-day
  (starts at 1.0, learned via the post-meeting run, §3.4).
- On a standard day `live ≈ baseline`, `anomaly_ratio ≈ 1`, so this collapses back to the
  simple `baseline × 4/3 + 5` equation. The refinement only *adds* buffer when warranted.

---

## 3. The runs (when data is fetched)

### 3.1 Creation run — establishes baseline
- **Trigger:** meeting first seen (created / first calendar sync).
- **Action:** Routes query for the scheduled slot → store `baseline`. Compute a provisional
  `leave_by` from the equation. Write the provisional "Leave by" calendar event.
- **Cost:** 1 Routes call per leg, once.

### 3.2 T-2h run — anomaly check
- **Trigger:** meeting is ≤ 2h away and hasn't had an anomaly run yet.
- **Action:** query `live`; compute `anomaly` / `anomaly_ratio`.
  - **Standard** (`anomaly ≤ threshold`): keep equation-based `leave_by`.
  - **Abnormal** (`anomaly > threshold`): run **attribution** (TfL → weather → news, trust-tiered),
    inflate buffer via §2 adaptive margin, update the "Leave by" event, and (optional) note the
    cause on the event ("⚠️ Northern line part-suspended").
- **Threshold (tunable):** `anomaly > 10 min OR anomaly_ratio > 1.25`.

### 3.3 Departure run — final route
- **Trigger:** now is within the `leave_by` window (≈ at `meeting_start − allowance`).
- **Action:** re-query `live` to choose the **actual route travelled**; finalise `leave_by` and the
  Maps deep link (origin → waypoints → destination) on the "Leave by" event. Fire the reminder.
- **Guard:** if the delta vs the T-2h run is large, re-attribute before committing.

### 3.4 Post-meeting run — learning (implicit 4th)
- **Trigger:** after `meeting_start` (or arrival detected).
- **Action:** record observed conditions for the corridor/slot; update `learned_ratio`
  (rolling mean of `actual / baseline`). This is how "see how long it actually took on average"
  feeds back into future buffers.

---

## 4. Mapping onto the single 5-minute cron

No separate schedulers. Each tick, for each upcoming meeting, pick the phase:

```
for meeting in upcoming_meetings(user):
    if meeting.baseline is None:                  phase = CREATION
    elif within(meeting, hours=2) and not meeting.anomaly_checked:
                                                  phase = ANOMALY
    elif now >= meeting.leave_by - SLACK:         phase = DEPARTURE
    elif meeting.ended and not meeting.learned:   phase = LEARNING
    else:                                          phase = IDLE   # no API call
```

- Most ticks are `IDLE` → **zero Routes calls** → cost stays tiny (see `07-data-sources.md`).
- `SLACK` (e.g. one cron interval) ensures the departure run fires just before leave time.

---

## 5. Anomaly → attribution (trust-tiered)

Only abnormal legs trigger event work (keeps cost + noise down):

1. **Tier 1** (TfL, Met Office, National Highways, police) → may auto-act on one source.
2. **Tier 2** (BBC, Guardian, Standard, Reuters) → act only if ≥ 2 agree.
3. **Tier 3** (social) → log only, never act.

Attribution attaches a cause + confidence to the leg and is surfaced on the calendar event.
If no source explains a large anomaly → mark `unexplained`, still inflate buffer, don't fabricate.

---

## 6. Parameters to tune (single source of truth)

| Param | Default | Notes |
| --- | --- | --- |
| `BUFFER_MARGIN_FLOOR` | `1/3` | The owner's `+ a third`. |
| `BUFFER_FIXED_MIN` | `5 min` | The `+ 5 minutes`. |
| `ANOMALY_ABS_MIN` | `10 min` | Abnormal if exceeded. |
| `ANOMALY_RATIO` | `1.25` | Abnormal if exceeded. |
| `ANOMALY_LOOKAHEAD` | `2 h` | When the anomaly run fires. |
| `CRON_INTERVAL` | `5 min` | Loop cadence. |
| `LEAVE_BY_SLACK` | `1 interval` | Departure-run trigger slack. |

---

## 7. Back-testing, stop detection & cross-user validation

> **Status: §7.2–§7.4 deferred to post-demo.** Stop detection and cross-user validation need a live
> GPS/location signal the calendar-only build doesn't have (see §7.5 + §8). §7.1 (back-test from
> logged outcomes) is partly realised by the Reflection loop; the GPS-dependent parts are a future
> to-do, not in the demo.

Part of the self-healing loop: continuously compare **actual vs predicted** and adjust rules at
runtime — but only learn from *clean* journeys. A voluntary coffee stop must not inflate everyone's
buffer.

### 7.1 Back-testing (runtime rule adjustment)
- After each leg, compute `error = actual − predicted` and `actual / baseline`.
- Feed **clean** samples (see 7.2/7.3) into `learned_ratio` and re-tune params
  (`BUFFER_MARGIN_FLOOR`, thresholds). Adjustments are **bounded** (e.g. margin ∈ [1/3, 1.5]) so one
  weird day can't swing the model.
- Log every adjustment to `agent_runs` — it's also the autonomy story ("retuned itself N times").

### 7.2 Stop detection — separate *behaviour* from *conditions*
The user's extra time has two causes we must NOT conflate:
- **Conditions** (traffic, strike, closure) → belong in the corridor baseline → buffer everyone.
- **Behaviour** (grabbed a coffee, took a detour) → personal → exclude from corridor learning.

Heuristic — **dwell = stationary > 5 min while NOT in reported traffic**:
```
if location_unchanged(>= 5 min) and not traffic_reported(here, now):
    classify = VOLUNTARY_STOP     # subtract dwell from `actual` before learning
elif location_unchanged and traffic_reported(here, now):
    classify = GENUINE_DELAY      # keep — it's a real condition
else:
    classify = MOVING
```

### 7.3 Cross-user outlier detection
A single user's leg can't tell stop-vs-condition alone. Compare against the **cohort** — other users
on the same corridor + time window:
- **Many** users slow → real condition → trust it, update corridor baseline.
- **Only this** user slow → outlier → behaviour (stop/detour) → exclude from corridor learning;
  optionally fold into a per-user habit (7.4).
- Outlier test: user's `actual / baseline` is `> k · MAD` from the cohort median.

### 7.4 Per-user habit model (optional, high-quality touch)
If a user *repeatedly* adds ~N min on a corridor/time (always-grabs-coffee), learn it as a
**personal** buffer applied only to them — distinct from corridor conditions. Personalised leave-by
without polluting shared data.

### 7.5 Cold-start
With one demo user there is no cohort. Fallbacks, in order: (1) traffic-gate alone (7.2),
(2) the user's own history, (3) corridor priors. The cross-user mechanism is designed-in but only
activates at scale — say so honestly in the demo.

> **Dependency / gap:** 7.2 and 7.3 need a **live location signal** (GPS) to detect dwell. The
> calendar-only demo has none. Options: (a) a companion phone location feed, (b) simulate dwell from
> calendar gaps / a scripted track for the demo, (c) ship the back-test as post-hoc on logged data
> only. **Decision needed — see §8.**

---

## 8. Open items (for audit / decision)

- [ ] Baseline source: traffic-aware-at-creation (typical) vs traffic-unaware (free-flow)?
      Model assumes **typical (traffic-aware for the slot)** — confirm.
- [ ] Reminder lead time on the "Leave by" event (e.g. popup at leave time + 5 min before)?
- [ ] Does `predicted = max(baseline, live)` ever over-budget on genuinely-better-than-normal
      days? Acceptable for a punctuality tool, but note it.
- [ ] **Location signal for stop detection (§7.2/7.3):** GPS feed, simulated track, or post-hoc
      only? Blocks back-testing realism for the demo.
- [ ] Minimum cohort size before cross-user learning activates (§7.3).
