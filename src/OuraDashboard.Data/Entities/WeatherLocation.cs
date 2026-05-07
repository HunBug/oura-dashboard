using System.Text.Json;

namespace OuraDashboard.Data.Entities;

public class WeatherLocation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? ElevationMeters { get; set; }
    public string Timezone { get; set; } = "Europe/Tallinn";
    public JsonDocument? RawJson { get; set; }

    public List<WeatherStation> Stations { get; set; } = [];
    public List<WeatherHourlySample> HourlySamples { get; set; } = [];
}
