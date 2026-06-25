using TravelOptimizer.Domain.DataHelpers;
using TravelOptimizer.Domain.Entities.Travel;
using TravelOptimizer.Domain.Models.Travel;

namespace TravelOptimizer.Api.Mapping.Travel;

public static class ItineraryMapper
{
    public static ItineraryResponse ToResponse(this Itinerary it) => new(
        it.UserId,
        it.Date,
        it.TotalPredictedWastedMin,
        it.Legs.Select(l => new ItineraryLegResponse(
            l.Leg.Id,
            l.Leg.FromLabel,
            l.Leg.ToLabel,
            l.Leg.NotBefore,
            l.Leg.ArriveBy,
            l.Leg.CorridorKey,
            l.Decision is null ? null : l.Decision.ToResponse(),
            MapsLink.ForLeg(l.Leg.FromLat, l.Leg.FromLng, l.Leg.ToLat, l.Leg.ToLng,
                l.Decision?.ChosenMode ?? TravelMode.Tube),
            l.Predictions.Select(p => p.ToResponse()).ToList())).ToList());

    public static DecisionResponse ToResponse(this TravelDecision d) => new(
        d.Id,
        d.ChosenMode,
        d.RecommendedDeparture,
        d.PredictedArrival,
        d.PredictedWastedMin,
        d.WasExploration,
        d.PolicyVersion,
        d.Rationale);

    public static PredictionResponse ToResponse(this TravelPrediction p) => new(
        p.Mode,
        p.RawDurationMin,
        p.CalibratedDurationMin,
        p.WaitMin,
        p.Confidence,
        p.Feasible,
        p.Rationale);

    public static ProposedAdjustmentResponse ToResponse(this ProposedAdjustment a) => new(
        a.Id,
        a.Kind,
        a.Target,
        a.Change,
        a.Rationale,
        a.ShadowImprovementMin,
        a.Status,
        a.CreatedAt);
}
