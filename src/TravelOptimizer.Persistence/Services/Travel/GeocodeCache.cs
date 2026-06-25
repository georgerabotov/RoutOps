using System.Collections.Concurrent;
using TravelOptimizer.Domain.Interfaces.Travel.Models;

namespace TravelOptimizer.Persistence.Services.Travel;

/// <summary>
/// Process-wide cache of geocode results (registered as a singleton). The optimizer re-runs every
/// minute and would otherwise re-query the geocoder for every event that hasn't resolved yet —
/// hammering the public OpenStreetMap Nominatim endpoint and tripping its rate limit. Caching both
/// hits and misses (a miss for a shorter window, so a fixed calendar entry recovers quickly) keeps
/// us well under the limit.
/// </summary>
public sealed class GeocodeCache
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(GeocodeResult Result, DateTime ExpiresAt);

    public bool TryGet(string key, out GeocodeResult result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (_entries.TryGetValue(key, out var e))
        {
            if (e.ExpiresAt > DateTime.UtcNow) { result = e.Result; return true; }
            _entries.TryRemove(key, out _);
        }
        return false;
    }

    public void Set(string key, GeocodeResult result)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var ttl = result.Found ? HitTtl : MissTtl;
        _entries[key] = new Entry(result, DateTime.UtcNow.Add(ttl));
    }
}
