using OuraDashboard.Web.Services;
using Xunit;

namespace OuraDashboard.Tests;

public class WeatherClassifiersTests
{
    [Theory]
    [InlineData(3.99, WeatherLevels.Acceptable)]
    [InlineData(4.0, WeatherLevels.Medium)]
    [InlineData(8.0, WeatherLevels.Medium)]
    [InlineData(8.01, WeatherLevels.High)]
    public void PressureLevel_UsesConfiguredThresholds(double change, string expected)
    {
        Assert.Equal(expected, WeatherClassifiers.PressureLevel(change, 70));
    }

    [Fact]
    public void PressureLevel_RequiresMinimumCoverage()
    {
        Assert.Equal(WeatherLevels.Insufficient, WeatherClassifiers.PressureLevel(12, 69.9));
    }

    [Theory]
    [InlineData(5.0, WeatherLevels.Enough)]
    [InlineData(2.0, WeatherLevels.Middle)]
    [InlineData(1.99, WeatherLevels.Low)]
    public void SunLevel_UsesConfiguredThresholds(double hours, string expected)
    {
        Assert.Equal(expected, WeatherClassifiers.SunLevel(hours, 70));
    }

    [Fact]
    public void SunLevel_RequiresMinimumCoverage()
    {
        Assert.Equal(WeatherLevels.Insufficient, WeatherClassifiers.SunLevel(6, 69.9));
    }
}
