using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Persistence;
using TravelOptimizer.Persistence.DataInitializers;

namespace TravelOptimizer.Api.Jobs;

/// <summary>
/// Keeps the demo calendar rolling forward. For every user that has NOT connected a real Google
/// Calendar, it ensures the demo London meetings exist for today and tomorrow, so the optimizer
/// always has consecutive located events to turn into journeys and the dashboard is never empty.
/// Users with a Google connection are left alone — their real synced events win.
/// Idempotent (events are keyed by a date-stamped ExternalId), so running it often is cheap.
/// </summary>
public class DemoCalendarJob(
    AppDbContext db,
    JobRunRegistry registry,
    ILogger<DemoCalendarJob> logger) : IInvocable
{
    public async Task Invoke()
    {
        var connectedUserIds = await db.GoogleCalendarConnections
            .Select(c => c.UserId)
            .ToHashSetAsync();

        var users = await db.Users.ToListAsync();
        var today = DateTime.UtcNow.Date;
        int seeded = 0;
        int failures = 0;

        foreach (var user in users)
        {
            if (connectedUserIds.Contains(user.Id)) continue; // real calendar takes over

            try
            {
                seeded += await DemoCalendarSeeder.EnsureDayAsync(db, user.Id, today);
                seeded += await DemoCalendarSeeder.EnsureDayAsync(db, user.Id, today.AddDays(1));
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "DemoCalendarJob failed for user {User}", user.Id);
            }
        }

        if (seeded > 0)
            logger.LogInformation("DemoCalendarJob seeded {Count} demo event(s)", seeded);

        registry.Record(nameof(DemoCalendarJob), failures == 0, failures == 0 ? null : $"{failures} failure(s)");
    }
}
