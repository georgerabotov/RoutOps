using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelOptimizer.Domain.Entities.Travel;
using TravelOptimizer.Domain.Interfaces.Travel;

namespace TravelOptimizer.Persistence.Services.Travel;

/// <summary>
/// Layer 1 (spec §2). Pure arithmetic: an EWMA multiplicative correction per corridor bucket plus
/// an empirical-Bayes confidence blend. No training infra, no drift risk.
/// </summary>
public class CalibrationService(AppDbContext db, ILogger<CalibrationService> logger) : ICalibrationService
{
    private const double Alpha = 0.2; // EWMA smoothing
    private const double ShrinkK = 10.0; // empirical-Bayes prior strength
    private const double RatioFloor = 0.25; // clamp band for actual/predicted — guards against
    private const double RatioCeil = 4.0;   // a mistaken/forgotten tap poisoning the corridor model

    public async Task<TravelPrediction> CalibrateAsync(TravelPrediction raw, TravelLeg leg)
    {
        var model = await db.CorridorModels.AsNoTracking().FirstOrDefaultAsync(m =>
            m.Mode == raw.Mode &&
            m.CorridorKey == leg.CorridorKey &&
            m.DayType == leg.DayType &&
            m.HourBucket == leg.HourBucket);

        if (model is null || model.SampleCount == 0)
        {
            // no learned signal yet — trust the raw API estimate as-is
            raw.CalibratedDurationMin = raw.RawDurationMin;
            return raw;
        }

        raw.CalibratedDurationMin = Math.Max(1, (int)Math.Round(raw.RawDurationMin * model.CorrectionFactor));

        // empirical-Bayes blend: few samples → trust API, many → trust learned reliability
        double learnedConfidence = 1.0 / (1.0 + model.Mape);
        double weight = model.SampleCount / (model.SampleCount + ShrinkK);
        raw.Confidence = (1 - weight) * raw.Confidence + weight * learnedConfidence;

        return raw;
    }

    public async Task IngestOutcomeAsync(LegOutcome outcome)
    {
        var stored = await db.LegOutcomes.FirstOrDefaultAsync(o => o.Id == outcome.Id);
        if (stored is null)
        {
            logger.LogWarning("IngestOutcome: outcome {Id} not found", outcome.Id);
            return;
        }

        if (stored.IngestedAt is not null)
            return; // idempotent — already folded in

        var decision = await db.TravelDecisions
            .Include(d => d.TravelLeg)
            .FirstOrDefaultAsync(d => d.Id == stored.TravelDecisionId);

        if (decision?.TravelLeg is null)
        {
            logger.LogWarning("IngestOutcome: decision/leg missing for outcome {Id}", stored.Id);
            return;
        }

        var leg = decision.TravelLeg;
        var chosen = await db.TravelPredictions.AsNoTracking().FirstOrDefaultAsync(p =>
            p.TravelLegId == leg.Id && p.Mode == decision.ChosenMode);

        if (chosen is null || chosen.RawDurationMin <= 0 || stored.ActualDurationMin <= 0)
        {
            logger.LogWarning("IngestOutcome: cannot calibrate outcome {Id} (missing/zero predictions)", stored.Id);
            stored.IngestedAt = DateTime.UtcNow; // mark so we don't retry forever
            await db.SaveChangesAsync();
            return;
        }

        double actual = stored.ActualDurationMin;
        // clamp so one mistaken/forgotten tap (e.g. tapped the next day → ratio ~48) can't poison the model
        double ratio = Math.Clamp(actual / chosen.RawDurationMin, RatioFloor, RatioCeil);
        double mapeSample = Math.Abs(actual - chosen.CalibratedDurationMin) / actual;

        var model = await db.CorridorModels.FirstOrDefaultAsync(m =>
            m.Mode == decision.ChosenMode &&
            m.CorridorKey == leg.CorridorKey &&
            m.DayType == leg.DayType &&
            m.HourBucket == leg.HourBucket);

        if (model is null)
        {
            model = new CorridorModel
            {
                Mode = decision.ChosenMode,
                CorridorKey = leg.CorridorKey,
                DayType = leg.DayType,
                HourBucket = leg.HourBucket,
                CorrectionFactor = 1.0,
                Mape = 0.0,
                SampleCount = 0,
            };
            db.CorridorModels.Add(model);
        }

        if (model.SampleCount == 0)
        {
            // seed the first observation directly instead of anchoring to the 1.0 / 0.0 prior,
            // which otherwise leaves the factor under-converged and reports a misleadingly low MAPE
            model.CorrectionFactor = ratio;
            model.Mape = mapeSample;
        }
        else
        {
            model.CorrectionFactor = (1 - Alpha) * model.CorrectionFactor + Alpha * ratio;
            model.Mape = (1 - Alpha) * model.Mape + Alpha * mapeSample;
        }
        model.SampleCount += 1;
        model.UpdatedAt = DateTime.UtcNow;

        stored.IngestedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Calibrated {Mode} {Corridor}/{Day}/{Bucket}: factor={Factor:F3} mape={Mape:F3} n={N}",
            model.Mode, model.CorridorKey, model.DayType, model.HourBucket,
            model.CorrectionFactor, model.Mape, model.SampleCount);
    }
}
