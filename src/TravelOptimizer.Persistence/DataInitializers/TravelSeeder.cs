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

        // Seed a demo calendar for today + tomorrow so the dashboard is populated out of the box.
        // The recurring DemoCalendarJob keeps rolling it forward from here. See QUICKSTART.md.
        await DemoCalendarSeeder.SeedTodayAndTomorrowAsync(db, user);
    }
}
