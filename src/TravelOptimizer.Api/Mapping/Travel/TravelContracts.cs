namespace TravelOptimizer.Api.Mapping.Travel;

public record PredictionResponse(
    string Mode,
    int RawDurationMin,
    int CalibratedDurationMin,
    int WaitMin,
    double Confidence,
    bool Feasible,
    string Rationale);

public record DecisionResponse(
    int DecisionId,
    string ChosenMode,
    DateTime RecommendedDeparture,
    DateTime PredictedArrival,
    int PredictedWastedMin,
    bool WasExploration,
    int PolicyVersion,
    string Rationale);

public record ItineraryLegResponse(
    int LegId,
    string FromLabel,
    string ToLabel,
    DateTime NotBefore,
    DateTime ArriveBy,
    string CorridorKey,
    DecisionResponse? Decision,
    string MapsUrl,
    IReadOnlyList<PredictionResponse> Options);

public record ItineraryResponse(
    int UserId,
    DateOnly Date,
    int TotalPredictedWastedMin,
    IReadOnlyList<ItineraryLegResponse> Legs);

public record ProposedAdjustmentResponse(
    int Id,
    string Kind,
    string Target,
    string Change,
    string Rationale,
    double ShadowImprovementMin,
    string Status,
    DateTime CreatedAt);
