using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelOptimizer.Domain.DataHelpers;
using TravelOptimizer.Domain.Entities.Travel;
using TravelOptimizer.Domain.Interfaces.Travel;
using TravelOptimizer.Domain.Interfaces.Travel.Models;
using TravelOptimizer.Domain.Models.Travel;

namespace TravelOptimizer.Persistence.Services.Travel;

/// <summary>
/// The decision agent (spec §1). Deterministic core — no LLM in the selection path. For each leg it
/// fans out to every source agent, calibrates, drops infeasible options, selects via the policy,
/// and persists the decision with the full context that produced it.
/// </summary>
public class ItineraryOptimizer(
    AppDbContext db,
    IEnumerable<ISourceAgent> agents,
    ICalibrationService calibration,
    IPolicyService policy,
    IGeocodingService geocoding,
    ISourceHealthState health,
    ILogger<ItineraryOptimizer> logger) : IItineraryOptimizer
{
    private readonly List<ISourceAgent> _agents = agents.ToList();
    private const double DegradedConfidenceFactor = 0.6;

    public async Task<Itinerary> OptimizeDayAsync(int userId, DateOnly date, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException($"User {userId} not found.");

        var tz = ResolveTimeZone(user.TimeZone);
        var (fromUtc, toUtc) = LocalDayWindowUtc(date, tz);

        var events = await db.CalendarEvents
            .Where(e => e.UserId == userId && e.StartUtc >= fromUtc && e.StartUtc < toUtc)
            .OrderBy(e => e.StartUtc)
            .ToListAsync(ct);

        // Idempotent: reuse legs already built for this day so repeated runs (hourly job, startup
        // kick) don't duplicate. Legs are keyed by their event window (NotBefore, ArriveBy).
        var existingLegs = await db.TravelLegs
            .Where(l => l.UserId == userId && l.ArriveBy >= fromUtc && l.ArriveBy < toUtc)
            .Include(l => l.Predictions).ThenInclude(p => p.Segments)
            .Include(l => l.Decision!).ThenInclude(d => d.Outcome)
            .ToListAsync(ct);

        var itinerary = new Itinerary { UserId = userId, Date = date };
        var matched = new HashSet<int>();

        for (int i = 0; i < events.Count - 1; i++)
        {
            var from = events[i];
            var to = events[i + 1];

            var existing = existingLegs.FirstOrDefault(l => l.NotBefore == from.EndUtc && l.ArriveBy == to.StartUtc);
            if (existing is not null)
            {
                matched.Add(existing.Id);

                // A logged outcome locks the leg — it already happened; never disturb history.
                if (existing.Decision?.Outcome is not null)
                {
                    itinerary.Legs.Add(ToItineraryLeg(existing));
                    continue;
                }

                itinerary.Legs.Add(await RefreshLegAsync(existing, ct));
                continue;
            }

            var leg = await BuildLegAsync(userId, from, to, tz, ct);
            if (leg is null) continue;

            itinerary.Legs.Add(await OptimizeLegAsync(leg, ct));
        }

        await PruneStaleLegsAsync(existingLegs, matched, ct);

        logger.LogDebug(
            "Optimised {Count} leg(s) for user {User} on {Date}; total predicted wasted = {Wasted} min",
            itinerary.Legs.Count, userId, date, itinerary.TotalPredictedWastedMin);

        return itinerary;
    }

    /// <summary>
    /// Re-runs the source fan-out + policy for an existing, not-yet-travelled leg and updates the
    /// decision in place so its id (and any future outcome link) stays stable.
    /// </summary>
    private async Task<ItineraryLeg> RefreshLegAsync(TravelLeg leg, CancellationToken ct)
    {
        db.TravelPredictions.RemoveRange(leg.Predictions);
        var raws = await EstimateAndCalibrateAsync(leg, ct);
        db.TravelPredictions.AddRange(raws);
        await db.SaveChangesAsync(ct);

        var fresh = await policy.SelectAsync(leg, raws);
        if (leg.Decision is null)
        {
            leg.Decision = fresh;
            db.TravelDecisions.Add(fresh);
        }
        else
        {
            leg.Decision.ChosenMode = fresh.ChosenMode;
            leg.Decision.RecommendedDeparture = fresh.RecommendedDeparture;
            leg.Decision.PredictedArrival = fresh.PredictedArrival;
            leg.Decision.PredictedWastedMin = fresh.PredictedWastedMin;
            leg.Decision.WasExploration = fresh.WasExploration;
            leg.Decision.PolicyVersion = fresh.PolicyVersion;
            leg.Decision.Rationale = fresh.Rationale;
        }

        await db.SaveChangesAsync(ct);
        return new ItineraryLeg { Leg = leg, Decision = leg.Decision, Predictions = raws };
    }

    /// <summary>Drops legs whose calendar events were moved/removed — but only if they carry no outcome.</summary>
    private async Task PruneStaleLegsAsync(IEnumerable<TravelLeg> existingLegs, ISet<int> matched, CancellationToken ct)
    {
        var stale = existingLegs
            .Where(l => !matched.Contains(l.Id) && l.Decision?.Outcome is null)
            .ToList();

        if (stale.Count == 0) return;

        foreach (var leg in stale)
        {
            if (leg.Decision is not null) db.TravelDecisions.Remove(leg.Decision);
            db.TravelPredictions.RemoveRange(leg.Predictions);
        }

        db.TravelLegs.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Pruned {Count} stale leg(s) after calendar changes", stale.Count);
    }

    private static ItineraryLeg ToItineraryLeg(TravelLeg leg) => new()
    {
        Leg = leg,
        Decision = leg.Decision,
        Predictions = leg.Predictions.ToList(),
    };

    private async Task<TravelLeg?> BuildLegAsync(int userId, CalendarEvent from, CalendarEvent to, TimeZoneInfo tz, CancellationToken ct)
    {
        var fromCoords = await ResolveCoordsAsync(from, ct);
        var toCoords = await ResolveCoordsAsync(to, ct);
        if (fromCoords is null || toCoords is null)
        {
            logger.LogWarning("Skipping leg {From} -> {To}: could not resolve coordinates", from.Title, to.Title);
            return null;
        }

        var localDeparture = TimeZoneInfo.ConvertTimeFromUtc(from.EndUtc, tz);

        var leg = new TravelLeg
        {
            UserId = userId,
            FromLabel = from.Location,
            FromLat = fromCoords.Value.lat,
            FromLng = fromCoords.Value.lng,
            ToLabel = to.Location,
            ToLat = toCoords.Value.lat,
            ToLng = toCoords.Value.lng,
            NotBefore = from.EndUtc,
            ArriveBy = to.StartUtc,
            CorridorKey = Geohash.CorridorKey(fromCoords.Value.lat, fromCoords.Value.lng, toCoords.Value.lat, toCoords.Value.lng),
            DayType = DayType.FromLocalDate(DateOnly.FromDateTime(localDeparture)),
            HourBucket = HourBucket.FromLocalHour(localDeparture.Hour),
        };

        db.TravelLegs.Add(leg);
        await db.SaveChangesAsync(ct); // persist to obtain leg.Id for predictions/decision
        return leg;
    }

    private async Task<ItineraryLeg> OptimizeLegAsync(TravelLeg leg, CancellationToken ct)
    {
        var raws = await EstimateAndCalibrateAsync(leg, ct);
        db.TravelPredictions.AddRange(raws);
        await db.SaveChangesAsync(ct);

        // drop infeasible / select via policy (greedy v1, bandit v2)
        var decision = await policy.SelectAsync(leg, raws);

        // persist the decision with the policy snapshot that produced it
        db.TravelDecisions.Add(decision);
        await db.SaveChangesAsync(ct);

        return new ItineraryLeg { Leg = leg, Decision = decision, Predictions = raws };
    }

    public async Task<ReoptimizeResult> ReoptimizeLegAsync(int legId, CancellationToken ct)
    {
        var leg = await db.TravelLegs
            .Include(l => l.Predictions)
            .Include(l => l.Decision)
            .FirstOrDefaultAsync(l => l.Id == legId, ct)
            ?? throw new InvalidOperationException($"Leg {legId} not found.");

        if (leg.Decision is null)
            throw new InvalidOperationException($"Leg {legId} has no decision to re-optimise.");

        var decision = leg.Decision;
        var prevMode = decision.ChosenMode;
        var prevDeparture = decision.RecommendedDeparture;

        // replace the leg's predictions with a fresh source read
        db.TravelPredictions.RemoveRange(leg.Predictions);
        var raws = await EstimateAndCalibrateAsync(leg, ct);
        db.TravelPredictions.AddRange(raws);
        await db.SaveChangesAsync(ct);

        var fresh = await policy.SelectAsync(leg, raws);

        bool changed = prevMode != fresh.ChosenMode
                       || Math.Abs((prevDeparture - fresh.RecommendedDeparture).TotalMinutes) >= 5;

        // update the existing decision in place so its id / outcome link stay stable
        decision.ChosenMode = fresh.ChosenMode;
        decision.RecommendedDeparture = fresh.RecommendedDeparture;
        decision.PredictedArrival = fresh.PredictedArrival;
        decision.PredictedWastedMin = fresh.PredictedWastedMin;
        decision.WasExploration = fresh.WasExploration;
        decision.PolicyVersion = fresh.PolicyVersion;
        decision.Rationale = fresh.Rationale;
        await db.SaveChangesAsync(ct);

        if (!changed)
            return ReoptimizeResult.Unchanged(legId, prevMode, prevDeparture);

        var note = prevMode != fresh.ChosenMode
            ? $"Mode changed {prevMode} -> {fresh.ChosenMode} (source conditions shifted)."
            : $"Departure moved {prevDeparture:HH:mm} -> {fresh.RecommendedDeparture:HH:mm}.";

        logger.LogInformation("Leg {LegId} re-optimised: {Note}", legId, note);
        return new ReoptimizeResult(legId, true, prevMode, fresh.ChosenMode, prevDeparture, fresh.RecommendedDeparture, note);
    }

    /// <summary>
    /// Fan out to the healthy/degraded agents in parallel (raw), then calibrate each (Layer 1).
    /// Self-healing: disabled sources are skipped (bar a recovery probe once their cooldown expires)
    /// and degraded sources have their confidence down-weighted so the policy trusts them less.
    /// DbContext writes stay sequential.
    /// </summary>
    private async Task<TravelPrediction[]> EstimateAndCalibrateAsync(TravelLeg leg, CancellationToken ct)
    {
        var runnable = _agents.Where(a => ShouldConsult(a.Mode)).ToList();
        if (runnable.Count == 0) runnable = _agents; // never leave the policy with nothing to choose

        var raws = await Task.WhenAll(runnable.Select(a => a.EstimateAsync(leg, ct)));
        foreach (var raw in raws)
        {
            await calibration.CalibrateAsync(raw, leg);
            raw.TravelLegId = leg.Id;

            if (health.GetState(raw.Mode) == SourceHealthStatus.Degraded)
                raw.Confidence *= DegradedConfidenceFactor;
        }

        return raws;
    }

    /// <summary>Skip disabled sources, but let one through once the cooldown has elapsed (recovery probe).</summary>
    private bool ShouldConsult(string mode)
    {
        if (!health.IsDisabled(mode, out var until)) return true;
        return until is null || until <= DateTime.UtcNow;
    }

    private async Task<(double lat, double lng)?> ResolveCoordsAsync(CalendarEvent e, CancellationToken ct)
    {
        if (e.HasCoordinates) return (e.Lat, e.Lng);

        var geo = await geocoding.GeocodeAsync(e.Location, ct);
        if (!geo.Found) return null;

        // Cache the resolved coordinates on the event so future runs skip the geocoder entirely.
        e.Lat = geo.Lat;
        e.Lng = geo.Lng;
        e.HasCoordinates = true;
        await db.SaveChangesAsync(ct);

        return (geo.Lat, geo.Lng);
    }

    private static (DateTime fromUtc, DateTime toUtc) LocalDayWindowUtc(DateOnly date, TimeZoneInfo tz)
    {
        var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, tz), TimeZoneInfo.ConvertTimeToUtc(localEnd, tz));
    }

    private TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "Europe/London" : id);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Unknown timezone '{Tz}'; defaulting to Europe/London", id);
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        }
    }
}
