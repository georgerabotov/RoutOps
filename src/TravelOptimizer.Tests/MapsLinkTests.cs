using System.Globalization;
using TravelOptimizer.Domain.DataHelpers;
using Xunit;

namespace TravelOptimizer.Tests;

public class MapsLinkTests
{
    [Theory]
    [InlineData(TravelMode.Tube, "transit")]
    [InlineData(TravelMode.Bus, "transit")]
    [InlineData(TravelMode.Rail, "transit")]
    [InlineData(TravelMode.Walk, "walking")]
    [InlineData(TravelMode.Cycle, "bicycling")]
    [InlineData("something-unknown", "transit")] // safe default
    public void ToGoogleTravelMode_MapsEveryMode(string mode, string expected)
        => Assert.Equal(expected, MapsLink.ToGoogleTravelMode(mode));

    [Fact]
    public void ForLeg_BuildsDirectionsUrl_WithOriginDestinationAndMode()
    {
        var url = MapsLink.ForLeg(51.5308, -0.1238, 51.5054, -0.0235, TravelMode.Tube);

        Assert.StartsWith("https://www.google.com/maps/dir/?api=1", url);
        Assert.Contains("origin=51.5308,-0.1238", url);
        Assert.Contains("destination=51.5054,-0.0235", url);
        Assert.Contains("travelmode=transit", url);
    }

    [Fact]
    public void ForLeg_UsesInvariantCulture_SoCoordinatesAlwaysUseDotSeparator()
    {
        // Under a comma-decimal culture (de-DE), naive interpolation would emit "51,5" and break
        // the URL. The builder must force '.' via the invariant culture.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var url = MapsLink.ForLeg(51.5, -0.12, 51.6, -0.10, TravelMode.Walk);

            Assert.Contains("origin=51.5,-0.12", url);
            Assert.Contains("destination=51.6,-0.1", url);
            Assert.Contains("travelmode=walking", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
