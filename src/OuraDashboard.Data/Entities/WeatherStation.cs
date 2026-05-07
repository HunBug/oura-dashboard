using System.Text.Json;

namespace OuraDashboard.Data.Entities;

public class WeatherStation
{
    public int Id { get; set; }
    public int WeatherLocationId { get; set; }
    public WeatherLocation WeatherLocation { get; set; } = null!;

    public string Source { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? ElevationMeters { get; set; }
    public double? DistanceKm { get; set; }
    public string? ElementCode { get; set; }
    public string? ElementName { get; set; }
    public DateTimeOffset? ObservationPeriodStart { get; set; }
    public DateTimeOffset? ObservationPeriodEnd { get; set; }
    public JsonDocument RawJson { get; set; } = JsonDocument.Parse("{}");

    public List<WeatherHourlySample> HourlySamples { get; set; } = [];
}
