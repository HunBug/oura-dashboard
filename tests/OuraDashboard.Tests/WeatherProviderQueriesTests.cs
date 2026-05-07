using OuraDashboard.Data.Entities;
using OuraDashboard.Sync;
using Xunit;

namespace OuraDashboard.Tests;

public class WeatherProviderQueriesTests
{
    [Fact]
    public void BuildOpenMeteoArchivePath_IncludesExpectedQuery()
    {
        var location = new WeatherLocation
        {
            Latitude = 59.14496602915124,
            Longitude = 26.569136382508024,
            ElevationMeters = 85,
            Timezone = "Europe/Tallinn"
        };
        var options = new WeatherOptions
        {
            Timezone = "Europe/Tallinn",
            Sources = new WeatherSourceOptions
            {
                OpenMeteo = new OpenMeteoOptions
                {
                    Model = "best_match",
                    HourlyVariables = ["temperature_2m", "relative_humidity_2m"]
                }
            }
        };

        var path = WeatherProviderQueries.BuildOpenMeteoArchivePath(
            location,
            options,
            new DateTimeOffset(2026, 05, 06, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 05, 07, 20, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("v1/archive?", path);
        Assert.Contains("latitude=59.14496602915124", path);
        Assert.Contains("longitude=26.569136382508024", path);
        Assert.Contains("elevation=85", path);
        Assert.Contains("start_date=2026-05-07", path);
        Assert.Contains("end_date=2026-05-07", path);
        Assert.Contains("hourly=temperature_2m%2Crelative_humidity_2m", path);
        Assert.Contains("timezone=Europe%2FTallinn", path);
        Assert.Contains("wind_speed_unit=ms", path);
        Assert.Contains("models=best_match", path);
    }

    [Fact]
    public void BuildEstonianStationMetadataPath_FiltersElementAndStations()
    {
        var path = WeatherProviderQueries.BuildEstonianStationMetadataPath(["ROELA", "TARTU ÜLENURME"], "PR1H");

        Assert.StartsWith("f_kliima_jaam_vaatlus?", path);
        Assert.Contains("element_kood=eq.PR1H", path);
        Assert.Contains("jaam_kood=in.(ROELA,TARTU%20%C3%9CLENURME)", path);
        Assert.Contains("select=jaam_kood,jaam_nimi", path);
    }

    [Fact]
    public void BuildEstonianHourlyPath_FiltersStationElementAndMonth()
    {
        var path = WeatherProviderQueries.BuildEstonianHourlyPath("TARTU ÜLENURME", "TA", 2026, 5);

        Assert.StartsWith("f_kliima_tund?", path);
        Assert.Contains("jaam_kood=eq.TARTU%20%C3%9CLENURME", path);
        Assert.Contains("element_kood=eq.TA", path);
        Assert.Contains("aasta=eq.2026", path);
        Assert.Contains("kuu=eq.5", path);
        Assert.Contains("order=paev.asc,tund.asc", path);
    }
}
