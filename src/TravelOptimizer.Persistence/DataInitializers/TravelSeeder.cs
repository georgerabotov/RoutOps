using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Domain.DataHelpers;
using TravelOptimizer.Domain.Entities;
using TravelOptimizer.Domain.Entities.Travel;

namespace TravelOptimizer.Persistence.DataInitializers;

/// <summary>
/// Minimal dev seed: a default London user and the conservative starting PolicyWeights, so the
/// optimizer and the X-User-Id fallback work on a fresh database. No-op if data already exists.
/// </summary>
public static class TravelSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        var user = new User { Email = "founder@example.com", TimeZone = "Europe/London" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        foreach (var (key, value) in PolicyKeys.Defaults)
        {
            db.PolicyWeights.Add(new PolicyWeight
            {
                UserId = user.Id,
                Key = key,
                Value = value,
                Version = 1,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();

        SeedDemoEvents(db, user);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Demo itinerary for a hands-off run with no Google OAuth: four London meetings today, each
    /// with coordinates so the geocoder (and thus OpenAI) is never invoked. The optimizer turns
    /// these into three legs and runs the full live-TfL → calibrate → decide loop. See QUICKSTART.md.
    /// </summary>
    private static void SeedDemoEvents(AppDbContext db, User user)
    {
        var today = DateTime.UtcNow.Date;

        (string Title, double Lat, double Lng, int StartH, int StartM, int EndH, int EndM)[] meetings =
        [
            ("King's Cross",     51.5308, -0.1238,  9,  0,  9, 30),
            ("Canary Wharf",     51.5054, -0.0235, 11,  0, 11, 30),
            ("Soho Square",      51.5152, -0.1322, 14,  0, 14, 30),
            ("Liverpool Street", 51.5178, -0.0823, 16, 30, 17,  0),
        ];

        var i = 0;
        foreach (var m in meetings)
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                UserId = user.Id,
                ExternalId = $"seed-{today:yyyyMMdd}-{i++}",
                Source = "seed",
                Title = m.Title,
                Location = m.Title,
                Lat = m.Lat,
                Lng = m.Lng,
                HasCoordinates = true,
                StartUtc = today.AddHours(m.StartH).AddMinutes(m.StartM),
                EndUtc = today.AddHours(m.EndH).AddMinutes(m.EndM),
            });
        }
    }
}
