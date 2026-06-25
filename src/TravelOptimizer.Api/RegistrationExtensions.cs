using Coravel;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelOptimizer.Api.Common;
using TravelOptimizer.Api.Jobs;
using TravelOptimizer.Domain.Interfaces;
using TravelOptimizer.Domain.Interfaces.Travel;
using TravelOptimizer.Persistence;
using TravelOptimizer.Persistence.Services;
using TravelOptimizer.Persistence.Services.Travel;

namespace TravelOptimizer.Api;

public static class RegistrationExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
                               ?? "Host=localhost;Database=TravelOptimizer;Username=admin;Password=root;Include Error Detail=True";

        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));
        return services;
    }

    public static IServiceCollection RegisterServices(this IServiceCollection services, IConfiguration config)
    {
        // options
        services.Configure<PolicyOptions>(config.GetSection("Travel:Policy"));
        services.Configure<ReflectionOptions>(config.GetSection("Travel:Reflection"));
        services.Configure<AiOptions>(config.GetSection("Travel:OpenAI"));
        services.Configure<GoogleOptions>(config.GetSection("Travel:Google"));
        services.Configure<SourceHealthOptions>(config.GetSection("Travel:SourceHealth"));

        // core Travel services (interface in Domain, impl in Persistence — SERVICES.md)
        services.AddScoped<ICalibrationService, CalibrationService>();
        services.AddScoped<IPolicyService, PolicyService>();
        services.AddScoped<IItineraryOptimizer, ItineraryOptimizer>();
        // Geocoder gets a typed HttpClient for the keyless OpenStreetMap (Nominatim) fallback used
        // when the LLM is unconfigured. Nominatim requires a descriptive User-Agent.
        services.AddHttpClient<IGeocodingService, GeocodingService>(c =>
        {
            c.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("TravelOptimizer/1.0 (route planner demo)");
        });
        services.AddScoped<IReflectionService, ReflectionService>();
        services.AddScoped<IAdjustmentPromoter, AdjustmentPromoter>();
        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();

        // self-healing source health: in-memory live state (singleton) + durable companion (scoped)
        services.AddSingleton<ISourceHealthState, SourceHealthState>();
        services.AddScoped<ISourceHealthService, SourceHealthService>();

        // optional TfL app-key authenticator (no-op when unconfigured)
        services.AddTransient<TflAppKeyHandler>();

        // source agents — one typed HttpClient each (TfL), exposed via ISourceAgent (spec §5)
        AddTflAgent<TubeAgent>(services);
        AddTflAgent<BusAgent>(services);
        AddTflAgent<RailAgent>(services);
        AddTflAgent<CycleAgent>(services);
        AddTflAgent<WalkAgent>(services);
        AddTflAgent<MultiModalAgent>(services); // composite mixed-mode option

        // AI completion, mirroring AddHttpClient<OpenAIService> (REGISTRATION.md)
        var aiBase = config["Travel:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/";
        services.AddHttpClient<IChatCompletionService, OpenAiChatCompletionService>(c => c.BaseAddress = new Uri(aiBase));

        // CQRS pipeline (ARCHITECTURE.md)
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RegistrationExtensions).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddValidatorsFromAssembly(typeof(RegistrationExtensions).Assembly);

        // Coravel jobs (JOBS.md)
        services.AddScheduler();
        services.AddSingleton<JobRunRegistry>();
        services.AddTransient<OptimizeDayJob>();
        services.AddTransient<CalibrationJob>();
        services.AddTransient<ReflectionJob>();
        services.AddTransient<CalendarSyncJob>();
        services.AddTransient<DemoCalendarJob>();
        services.AddTransient<MonitorJob>();
        services.AddTransient<ProbeJob>();
        services.AddTransient<HealthJob>();

        return services;
    }

    private static void AddTflAgent<TAgent>(IServiceCollection services) where TAgent : class, ISourceAgent
    {
        services.AddHttpClient<TAgent>(c => c.BaseAddress = new Uri("https://api.tfl.gov.uk/"))
            .AddHttpMessageHandler<TflAppKeyHandler>();
        services.AddTransient<ISourceAgent>(sp => sp.GetRequiredService<TAgent>());
    }
}
