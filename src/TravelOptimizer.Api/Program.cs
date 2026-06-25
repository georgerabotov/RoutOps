using Coravel;
using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Api;
using TravelOptimizer.Api.Common;
using TravelOptimizer.Api.Jobs;
using TravelOptimizer.Domain.Interfaces.Travel;
using TravelOptimizer.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.RegisterServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Serve the build-free dashboard SPA from wwwroot (index.html as the default document).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapControllers();

// Hourly schedule — each job gates on the user's local hour internally (TieringJob pattern, JOBS.md)
app.Services.UseScheduler(scheduler =>
{
    scheduler.Schedule<DemoCalendarJob>().EveryFifteenMinutes().PreventOverlapping(nameof(DemoCalendarJob));
    scheduler.Schedule<OptimizeDayJob>().EveryMinute().PreventOverlapping(nameof(OptimizeDayJob));
    scheduler.Schedule<CalibrationJob>().Hourly();
    scheduler.Schedule<ReflectionJob>().Hourly();
    scheduler.Schedule<CalendarSyncJob>().EveryMinute().PreventOverlapping(nameof(CalendarSyncJob));
    scheduler.Schedule<MonitorJob>().EveryThirtyMinutes();
    scheduler.Schedule<ProbeJob>().Cron("*/20 * * * *").PreventOverlapping(nameof(ProbeJob));
    scheduler.Schedule<HealthJob>().EveryFiveMinutes().PreventOverlapping(nameof(HealthJob));
});

// Apply migrations + seed, retrying while Postgres comes up — `docker compose up -d` then a quick
// `dotnet run` can race the DB healthcheck, and the old best-effort one-shot left an empty schema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var attempt = 1; attempt <= 12; attempt++)
    {
        try
        {
            db.Database.Migrate();
            await TravelOptimizer.Persistence.DataInitializers.TravelSeeder.SeedAsync(db);

            // rehydrate the in-memory source-health state from its durable mirror
            await scope.ServiceProvider.GetRequiredService<ISourceHealthService>().SeedFromDbAsync(CancellationToken.None);
            break;
        }
        catch (Exception ex) when (attempt < 12)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}/12): {Message}. Retrying in 2.5s…",
                attempt, ex.Message);
            await Task.Delay(2500);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Database migration failed after retries; starting with no schema.");
        }
    }
}

// Run every scheduled job once at boot (fire-and-forget so it doesn't block the listener) so the
// dashboard is fully populated immediately instead of waiting for the first scheduled tick. Order
// matters: seed/sync the calendar first, then optimize, then the analysis/health jobs. Each job is
// isolated so one failure doesn't stop the rest; the scheduler keeps them fresh from here.
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    // IInvocable types, in dependency order.
    Type[] startupJobs =
    [
        typeof(DemoCalendarJob),   // ensure demo users have today/tomorrow events
        typeof(CalendarSyncJob),   // pull real Google Calendar events for connected users
        typeof(OptimizeDayJob),    // build today's + tomorrow's itineraries
        typeof(CalibrationJob),
        typeof(ReflectionJob),
        typeof(MonitorJob),
        typeof(ProbeJob),
        typeof(HealthJob),
    ];

    foreach (var jobType in startupJobs)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var job = (Coravel.Invocable.IInvocable)scope.ServiceProvider.GetRequiredService(jobType);
            await job.Invoke();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Startup run of {Job} was skipped", jobType.Name);
        }
    }
}));

app.Run();
