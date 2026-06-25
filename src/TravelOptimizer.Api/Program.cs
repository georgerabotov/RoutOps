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
    scheduler.Schedule<OptimizeDayJob>().EveryMinute().RunOnceAtStart().PreventOverlapping(nameof(OptimizeDayJob));
    scheduler.Schedule<CalibrationJob>().Hourly();
    scheduler.Schedule<ReflectionJob>().Hourly();
    scheduler.Schedule<CalendarSyncJob>().EveryThirtyMinutes();
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

// The scheduled OptimizeDayJob runs once at startup (RunOnceAtStart, above) under the same
// PreventOverlapping mutex, so the first itinerary build can't race the EveryMinute tick into
// duplicate legs. The client just GETs /api/itineraries/{date}.

app.Run();
