using System.Text.Json;

namespace OuraDashboard.Data.Entities;

public class WeatherHourlySample
{
    public int Id { get; set; }
    public int WeatherLocationId { get; set; }
    public WeatherLocation WeatherLocation { get; set; } = null!;
    public int? WeatherStationId { get; set; }
    public WeatherStation? WeatherStation { get; set; }

    public string Source { get; set; } = string.Empty;
    public string? Model { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTime TimestampLocal { get; set; }

    public double? TemperatureC { get; set; }
    public double? RelativeHumidityPct { get; set; }
    public double? DewPointC { get; set; }
    public double? ApparentTemperatureC { get; set; }
    public double? PrecipitationMm { get; set; }
    public double? RainMm { get; set; }
    public double? SnowfallCm { get; set; }
    public double? SnowDepthM { get; set; }
    public double? PressureMslHpa { get; set; }
    public double? SurfacePressureHpa { get; set; }
    public double? CloudCoverPct { get; set; }
    public double? WindSpeedMs { get; set; }
    public double? WindDirectionDeg { get; set; }
    public double? WindGustMs { get; set; }
    public double? ShortwaveRadiationWm2 { get; set; }
    public double? SunshineDurationSec { get; set; }
    public double? SoilTemperature0To7CmC { get; set; }
    public double? SoilMoisture0To7Cm { get; set; }
    public JsonDocument RawJson { get; set; } = JsonDocument.Parse("{}");
}
