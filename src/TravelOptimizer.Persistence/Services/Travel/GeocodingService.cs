using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelOptimizer.Domain.Interfaces;
using TravelOptimizer.Domain.Interfaces.Travel;
using TravelOptimizer.Domain.Interfaces.Travel.Models;

namespace TravelOptimizer.Persistence.Services.Travel;

/// <summary>
/// Resolves free-text calendar locations to coordinates. Order of attempts:
///   1. a cheap deterministic "lat,lng" parse;
///   2. OpenStreetMap Nominatim — keyless, accurate for real addresses (no OpenAI key needed);
///   3. the LLM, only for genuinely fuzzy strings Nominatim can't place (spec §1).
/// Virtual locations ("Google Meet", "Zoom", …) are treated as non-physical and reported as a miss,
/// so the optimizer simply skips that leg instead of inventing a journey.
/// </summary>
public class GeocodingService(HttpClient http, IChatCompletionService llm, ILogger<GeocodingService> logger) : IGeocodingService
{
    private static readonly string[] VirtualMarkers =
        ["google meet", "zoom", "teams", "meet.google", "webex", "hangout", "http://", "https://", "phone", "call "];

    public async Task<GeocodeResult> GeocodeAsync(string locationText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(locationText))
            return GeocodeResult.Miss(locationText);

        if (TryParseLatLng(locationText, out var lat, out var lng))
            return new GeocodeResult(true, lat, lng, locationText, 1.0);

        if (IsVirtual(locationText))
        {
            logger.LogDebug("Treating '{Location}' as a virtual (non-physical) location", locationText);
            return GeocodeResult.Miss(locationText);
        }

        var osm = await ResolveWithNominatimAsync(locationText, ct);
        if (osm.Found) return osm;

        try
        {
            return await ResolveWithLlmAsync(locationText, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM geocoding failed for '{Location}'", locationText);
            return GeocodeResult.Miss(locationText);
        }
    }

    private static bool IsVirtual(string text)
    {
        var lower = text.ToLowerInvariant();
        return VirtualMarkers.Any(lower.Contains);
    }

    private static bool TryParseLatLng(string text, out double lat, out double lng)
    {
        lat = 0; lng = 0;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lng)
               && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
    }

    /// <summary>
    /// Free, keyless forward geocode via OpenStreetMap Nominatim. Real calendar locations are often
    /// messy ("Venue - 1-2 Foo St, 1-2 Foo St, London SE1 8ND, UK"), so we try progressively
    /// simplified variants of the query until one resolves. Returns a miss on any error.
    /// </summary>
    private async Task<GeocodeResult> ResolveWithNominatimAsync(string locationText, CancellationToken ct)
    {
        foreach (var q in QueryCandidates(locationText))
        {
            try
            {
                var url = $"search?format=jsonv2&limit=1&q={Uri.EscapeDataString(q)}";
                var results = await http.GetFromJsonAsync<List<NominatimHit>>(url, ct);
                var hit = results?.FirstOrDefault();
                if (hit is not null
                    && double.TryParse(hit.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(hit.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                {
                    logger.LogInformation("Nominatim resolved '{Location}' (via '{Query}') -> {Lat},{Lng}", locationText, q, lat, lng);
                    return new GeocodeResult(true, lat, lng, locationText, 0.85);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nominatim geocoding failed for '{Query}'", q);
            }
        }

        return GeocodeResult.Miss(locationText);
    }

    private static readonly Regex UkPostcode =
        new(@"\b([A-Z]{1,2}\d[A-Z\d]?)\s*(\d[A-Z]{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Ordered, de-duplicated query variants from most-specific to most-forgiving.</summary>
    internal static IEnumerable<string> QueryCandidates(string raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ' ');

        IEnumerable<string> Variants()
        {
            var text = Norm(raw);
            yield return text;

            // Drop a leading venue-name prefix before the first " - " (e.g. "Halkin - 1-2 Paris…").
            var dashIdx = text.IndexOf(" - ", StringComparison.Ordinal);
            var noPrefix = dashIdx >= 0 ? Norm(text[(dashIdx + 3)..]) : text;
            if (noPrefix != text) yield return noPrefix;

            // Collapse consecutive duplicate comma segments ("1-2 Foo, 1-2 Foo, London" → "1-2 Foo, London").
            foreach (var src in new[] { noPrefix, text })
            {
                var segs = src.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var dedup = new List<string>();
                foreach (var seg in segs)
                    if (dedup.Count == 0 || !dedup[^1].Equals(seg, StringComparison.OrdinalIgnoreCase))
                        dedup.Add(seg);
                if (dedup.Count != segs.Length) yield return string.Join(", ", dedup);
            }

            // A bare UK postcode is a reliable last resort.
            var pc = UkPostcode.Match(raw);
            if (pc.Success) yield return $"{pc.Groups[1].Value.ToUpperInvariant()} {pc.Groups[2].Value.ToUpperInvariant()}, UK";
        }

        foreach (var v in Variants())
            if (!string.IsNullOrWhiteSpace(v) && seen.Add(v))
                yield return v;
    }

    private async Task<GeocodeResult> ResolveWithLlmAsync(string locationText, CancellationToken ct)
    {
        const string system =
            "You geocode short location strings to London coordinates. Output only JSON: " +
            "{\"found\":bool,\"lat\":number,\"lng\":number,\"confidence\":number}. " +
            "If you cannot resolve it confidently, return found=false.";

        var json = await llm.CompleteJsonAsync(system, locationText, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool found = root.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True;
        if (!found) return GeocodeResult.Miss(locationText);

        double lat = root.GetProperty("lat").GetDouble();
        double lng = root.GetProperty("lng").GetDouble();
        double conf = root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
        return new GeocodeResult(true, lat, lng, locationText, conf);
    }

    private sealed record NominatimHit(string Lat, string Lon);
}
