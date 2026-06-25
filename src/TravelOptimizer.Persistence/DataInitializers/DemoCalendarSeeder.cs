using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Domain.Entities;
using TravelOptimizer.Domain.Entities.Travel;

namespace TravelOptimizer.Persistence.DataInitializers;

/// <summary>
/// Keeps a demo London calendar populated for users with no real Google connection, so the
/// optimizer always has consecutive located events to turn into journeys. Idempotent per (user,
/// local date): events are keyed by a date-stamped ExternalId, so re-running never duplicates.
/// Used by the one-off startup seed and by the recurring DemoCalendarJob.
///
/// Events are seeded against the user's LOCAL day (not the UTC day) so the meetings land inside the
/// local-day query window even late in the evening, and displayed times match the labels.
/// </summary>
public static class DemoCalendarSeeder
{
    // Four located London meetings → three legs. Coordinates are baked in so the geocoder is never
    // invoked for the demo.
    private static readonly (string Title, double Lat, double Lng, int StartH, int StartM, int EndH, int EndM)[] Meetings =
    [
        ("King's Cross",     51.5308, -0.1238,  9,  0,  9, 30),
        ("Canary Wharf",     51.5054, -0.0235, 11,  0, 11, 30),
        ("Soho Square",      51.5152, -0.1322, 14,  0, 14, 30),
        ("Liverpool Street", 51.5178, -0.0823, 16, 30, 17,  0),
    ];

    /// <summary>
    /// Ensures the demo meetings exist for <paramref name="localDate"/> in the user's timezone.
    /// Returns the number of events added. Skips quietly if they are already present for that day.
    /// </summary>
    public static async Task<int> EnsureDayAsync(AppDbContext db, User user, DateOnly localDate, CancellationToken ct = default)
    {
        var tz = ResolveTimeZone(user.TimeZone);
        var stamp = localDate.ToString("yyyyMMdd");
        var prefix = $"seed-{stamp}-";

        var present = await db.CalendarEvents
            .Where(e => e.UserId == user.Id && e.Source == "seed" && e.ExternalId.StartsWith(prefix))
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
                UserId = user.Id,
                ExternalId = externalId,
                Source = "seed",
                Title = m.Title,
                Location = m.Title,
                Lat = m.Lat,
                Lng = m.Lng,
                HasCoordinates = true,
                StartUtc = ToUtc(localDate, m.StartH, m.StartM, tz),
                EndUtc = ToUtc(localDate, m.EndH, m.EndM, tz),
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Seeds the demo calendar for the user's local today and tomorrow.</summary>
    public static async Task SeedTodayAndTomorrowAsync(AppDbContext db, User user, CancellationToken ct = default)
    {
        var tz = ResolveTimeZone(user.TimeZone);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        await EnsureDayAsync(db, user, localToday, ct);
        await EnsureDayAsync(db, user, localToday.AddDays(1), ct);
    }

    private static DateTime ToUtc(DateOnly localDate, int hour, int minute, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "Europe/London" : id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); }
    }
}
