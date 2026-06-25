# Logic audit — TravelOptimizer (RouteOps)

Three independent reviewers audited the **decision/scoring**, **learning/calibration**, and
**startup/demo** paths. This consolidates their findings (deduped where two agents agreed), ranks
them, and records what was fixed in the accompanying PR vs what remains open.

## Verdict

**The seeded daytime demo works end-to-end and the deterministic core is defensively written.**
The scorer never throws on degenerate inputs, the no-feasible / null-decision paths are handled,
calibration guards its divisions, and every source agent falls back to a Haversine heuristic — so
the London demo runs even with TfL down. The real weaknesses are **operational (startup/timezone)**
and **learning-semantics**, not crashes. None block the greedy demo.

## Demo verdict (does the documented flow work?)

Yes, for a normal daytime run, **provided Postgres is up before the app migrates**. Seeded legs land
inside the London-day query window mid-day; the page, contract, and field names all line up; a blank
OpenAI key and unconfigured Google both degrade gracefully. The two things most likely to make a
first run "just not work" were the **never-retried startup migration** and a **slow-cold-start
duplicate-legs race** — both fixed below.

---

## Fixed in this pass

| # | Severity | Fix | File |
| --- | --- | --- | --- |
| F1 | HIGH | **Startup migration now retries** (12× / 2.5s) while Postgres comes up, instead of a best-effort one-shot that left an empty schema. | `Program.cs` |
| F2 | HIGH | **Duplicate-legs race removed** — boot warm-up now runs via the scheduler's `RunOnceAtStart()` under the same `PreventOverlapping` mutex, so it can't race the EveryMinute tick. (Belt-and-braces unique index still recommended — see O1.) | `Program.cs` |
| F3 | MED | **Seeding is now London-local** — events anchored to the user's local day & times, fixing the empty-itinerary-after-23:00-UTC window and the 1-hour summer label skew. | `TravelSeeder.cs` |
| F4 | HIGH | **Calibration ratio clamped** to [0.25, 4.0] — one mistaken/forgotten tap (ratio ~48×) can no longer poison a corridor model. | `CalibrationService.cs` |
| F5 | MED | **EWMA cold-start seeded** — first sample sets the factor directly instead of anchoring to the 1.0/0.0 prior (which under-converged and under-reported MAPE). | `CalibrationService.cs` |
| F6 | LOW | **QUICKSTART corrected** — a blank OpenAI key does not crash startup; documented the migration-retry log line. | `QUICKSTART.md` |

---

## Open findings (ranked)

### Must-fix before trusting the loop unsupervised (production, not demo)

| # | Sev | Finding | Where |
| --- | --- | --- | --- |
| O1 | HIGH | **No unique index** on `TravelLeg(UserId, NotBefore, ArriveBy)` — F2 closes the practical race, but the DB still can't catch a duplicate if any other path inserts concurrently. Needs an EF migration. | `TravelInitializer.cs:44-52` |
| O2 | HIGH | **Shadow-eval backtest never reads actual outcomes** — it measures whether new weights make the model *predict* lower cost, not whether real arrivals improved, so the auto-promote gate can green-light bad changes. | `ReflectionService.cs:118-136` |
| O3 | HIGH | **`CorridorModel` has no `UserId`** — learned calibration is silently shared across all users. F4 limits poisoning, but cross-user contamination should be an explicit decision, not accidental. | `CorridorModel.cs:7-21` |
| O4 | MED | **Actual duration measured from *recommended* departure**, not actual — conflates user punctuality with travel time and biases the whole learning signal. | `LogLegOutcome.cs:57-58` |
| O5 | MED | **Outcome ingestion not transactionally idempotent** — inline log path and hourly `CalibrationJob` can double-count or lose updates under concurrency. | `CalibrationService.cs:44-52,101-107` |

### Should-fix (data integrity / correctness)

| # | Sev | Finding | Where |
| --- | --- | --- | --- |
| O6 | MED | **Inverted/zero windows persist phantom legs** — no validation that `to.StartUtc > from.EndUtc`; overlapping/duplicate events yield "arrive before you leave" legs (rendered, not crashed). | `ItineraryOptimizer.cs:50-55` |
| O7 | MED | **Leg-identity keyed on `(NotBefore, ArriveBy)`** — duplicate windows collapse / churn; should key on source event ids. | `ItineraryOptimizer.cs:55` |
| O8 | MED | **Weight-version race** — no uniqueness on active `(UserId, Key)`; concurrent promote paths can create two active rows / duplicate versions. | `AdjustmentPromoter.cs:42-59` |
| O9 | MED | **Duplicate active weights resolved nondeterministically** — fold without `OrderBy(Version)`. | `PolicyService.cs:166`, `ReflectionService.cs:160` |
| O10 | MED | **`TryExtractNewValue` "last number" heuristic** mis-parses values with trailing text ("… (was 15)" → 15; "1,000" → 0). | `AdjustmentPromoter.cs:77-83` |
| O11 | MED | **No range validation / case-sensitivity bypass** on promoted weights — `"Min_Buffer"` can slip past the human-tap gate (inert, but a gap). | `AdjustmentPromoter.cs:32-59`, `ReflectionService.cs:145` |
| O12 | LOW | **Double-tap outcome → 500 not 400** (non-atomic check vs unique index). | `LogLegOutcome.cs:54-69` |
| O13 | LOW | **Re-optimize writes DB even when "Unchanged"** — persists sub-5-min drift while returning the old departure; caller/DB disagree. | `ItineraryOptimizer.cs:221-231` |
| O14 | LOW | **Concurrent first-ingest** for a new bucket can violate the corridor unique index (narrow window). | `CalibrationService.cs:86-103` |

### Deferred — bandit / Layer-2 (off by default: `Strategy="greedy"`, so demo-safe)

| # | Sev | Finding | Where |
| --- | --- | --- | --- |
| B1 | HIGH* | **Exploration re-rolled every tick** — a leg's mode flip-flops minute-to-minute; the choice is never committed. | `PolicyService.cs:91-114` |
| B2 | MED | **Pessimistic buffer uses the full event window**, not time-from-now — overstates slack at travel time. | `PolicyService.cs:118-123` |
| B3 | MED | **Arm history ignores `DayType`** — bandit posterior mixes weekday/weekend; inconsistent with Layer-1 key. | `PolicyService.cs:128-132` |
| B4 | LOW | `MinSamplesToExplore` counts all arms, not the candidate; `SampleNormal` can return negative wasted-time. | `PolicyService.cs:105,140-157` |

\* HIGH only when bandit mode is enabled.

---

## Cleared (verified NOT bugs)

Division-by-zero & undefined-MAPE are guarded; single/zero events build no legs and don't crash;
empty candidate list is guarded; no-feasible falls back to least-late; null `Decision` handled in
mapper & totals; TfL failure degrades to heuristic; outcome-locked legs are never disturbed; seeded
`DateTime` Kind is Utc (no Npgsql error); `CalendarSyncJob`/Google-unconfigured and blank-OpenAI-key
do not throw at startup or in jobs.

## Recommended next steps

1. **O1** (unique leg index) + **O2** (backtest against actuals) + **O4** (actual departure) are the
   three to do before letting the loop auto-promote anything in the real world.
2. The **Should-fix** set is a clean follow-up sweep — mostly small, mostly testable offline.
3. The **bandit** cluster only matters once `Strategy="bandit"`; safe to leave for v2.
