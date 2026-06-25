using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Domain.Interfaces.Travel;
using TravelOptimizer.Persistence;

namespace TravelOptimizer.Api.Jobs;

/// <summary>
/// Periodically pulls each connected user's upcoming Google Calendar events into CalendarEvent and
/// then immediately re-optimises that user's today + tomorrow, so a newly added event shows up as a
/// journey right after the sync instead of waiting for the separate OptimizeDayJob tick.
/// </summary>
public class CalendarSyncJob(
    AppDbContext db,
    IGoogleCalendarService google,
    IItineraryOptimizer optimizer,
    JobRunRegistry registry,
    ILogger<CalendarSyncJob> logger) : IInvocable
{
    public async Task Invoke()
    {
        var users = await db.Users
            .Where(u => db.GoogleCalendarConnections.Select(c => c.UserId).Contains(u.Id))
            .ToListAsync();

        int failures = 0;
        foreach (var user in users)
        {
            try
            {
                await google.SyncAsync(user.Id, CancellationToken.None);

                // Chain straight into optimisation so synced events become journeys immediately.
                var today = JobTime.LocalToday(user);
                foreach (var date in new[] { today, today.AddDays(1) })
                    await optimizer.OptimizeDayAsync(user.Id, date, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "CalendarSyncJob failed for user {User}", user.Id);
            }
        }

        registry.Record(nameof(CalendarSyncJob), failures == 0, failures == 0 ? null : $"{failures} failure(s)");
    }
}
