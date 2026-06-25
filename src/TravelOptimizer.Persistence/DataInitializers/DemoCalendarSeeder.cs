using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Domain.Entities;
using TravelOptimizer.Domain.Entities.Travel;

namespace TravelOptimizer.Persistence.DataInitializers;

/// <summary>
/// Keeps a demo London calendar populated for users with no real Google connection, so the
/// optimizer always has consecutive located events to turn into journeys. Idempotent per (user,
/// local date): events are keyed by a date-stamped ExternalId, so re-running never duplicates.
/// Used by the one-off startup seed and by the recurring DemoCalendarJob.
/// </summary>
public static class DemoCalendarSeeder
{
    // Four located London meetings → three legs. Coordinates are baked in so the geocoder (and
    // thus OpenAI) is never invoked for the demo.
    private static readonly (string Title, double Lat, double Lng, int StartH, int StartM, int EndH, int EndM)[] Meetings =
    [
        ("King's Cross",     51.5308, -0.1238,  9,  0,  9, 30),
        ("Canary Wharf",     51.5054, -0.0235, 11,  0, 11, 30),
        ("Soho Square",      51.5152, -0.1322, 14,  0, 14, 30),
        ("Liverpool Street", 51.5178, -0.0823, 16, 30, 17,  0),
    ];

    /// <summary>
    /// Ensures the demo meetings exist for <paramref name="day"/> (a UTC calendar date). Returns the
    /// number of events added. Skips quietly if they are already present for that day.
    /// </summary>
    public static async Task<int> EnsureDayAsync(AppDbContext db, int userId, DateTime day, CancellationToken ct = default)
    {
        var stamp = day.ToString("yyyyMMdd");
        var prefix = $"seed-{stamp}-";

        var present = await db.CalendarEvents
            .Where(e => e.UserId == userId && e.Source == "seed" && e.ExternalId.StartsWith(prefix))
            .Select(e => e.ExternalId)
            .ToListAsync(ct);

        int added = 0;
        for (int i = 0; i < Meetings.Length; i++)
        {
            var externalId = $"{prefix}{i}";
            if (present.Contains(externalId)) continue;

            var m = Meetings[i];
            db.CalendarEvents.Add(new CalendarEvent
            {
                UserId = userId,
                ExternalId = externalId,
                Source = "seed",
                Title = m.Title,
                Location = m.Title,
                Lat = m.Lat,
                Lng = m.Lng,
                HasCoordinates = true,
                StartUtc = day.Date.AddHours(m.StartH).AddMinutes(m.StartM),
                EndUtc = day.Date.AddHours(m.EndH).AddMinutes(m.EndM),
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Seeds the demo calendar for today and tomorrow for a freshly created user.</summary>
    public static async Task SeedTodayAndTomorrowAsync(AppDbContext db, User user, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        await EnsureDayAsync(db, user.Id, today, ct);
        await EnsureDayAsync(db, user.Id, today.AddDays(1), ct);
    }
}
