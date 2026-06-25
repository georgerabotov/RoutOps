using System.Globalization;

namespace TravelOptimizer.Domain.DataHelpers;

/// <summary>
/// Builds a Google Maps "directions" deep link (Maps URLs API) for a leg. No API key and no Routes
/// quota — it is just a URL that opens the Maps app/site with the route preloaded. Internal travel
/// modes are mapped to Google's <c>travelmode</c> values (public-transport modes → <c>transit</c>).
/// </summary>
public static class MapsLink
{
    /// <summary>Maps an internal <see cref="TravelMode"/> to a Google Maps <c>travelmode</c> value.</summary>
    public static string ToGoogleTravelMode(string mode) => mode switch
    {
        TravelMode.Walk => "walking",
        TravelMode.Cycle => "bicycling",
        TravelMode.Tube or TravelMode.Bus or TravelMode.Rail => "transit",
        _ => "transit",
    };

    /// <summary>
    /// Directions deep link from (<paramref name="fromLat"/>,<paramref name="fromLng"/>) to
    /// (<paramref name="toLat"/>,<paramref name="toLng"/>) in the given mode. Coordinates are
    /// formatted with the invariant culture so the decimal separator is always '.'.
    /// </summary>
    public static string ForLeg(double fromLat, double fromLng, double toLat, double toLng, string mode)
    {
        var origin = Format(fromLat, fromLng);
        var destination = Format(toLat, toLng);
        var travel = ToGoogleTravelMode(mode);
        return $"https://www.google.com/maps/dir/?api=1&origin={origin}&destination={destination}&travelmode={travel}";
    }

    private static string Format(double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat},{lng}");
}
