namespace OuraDashboard.Sync;

public class WeatherOptions
{
    public const string SectionName = "Weather";

    public bool Enabled { get; set; } = true;
    public string LocationName { get; set; } = "Roela";
    public double Latitude { get; set; } = 59.14496602915124;
    public double Longitude { get; set; } = 26.569136382508024;
    public double? ElevationMeters { get; set; }
    public string Timezone { get; set; } = "Europe/Tallinn";
    public bool AutoSyncEnabled { get; set; } = true;
    public int SyncIntervalHours { get; set; } = 24;
    public int SyncLookbackDays { get; set; } = 14;
    public int FullSyncLookbackDays { get; set; } = 3650;
    public WeatherSourceOptions Sources { get; set; } = new();
}

public class WeatherSourceOptions
{
    public OpenMeteoOptions OpenMeteo { get; set; } = new();
    public EstonianEnvironmentAgencyOptions EstonianEnvironmentAgency { get; set; } = new();
}

public class OpenMeteoOptions
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "best_match";
    public string[] HourlyVariables { get; set; } =
    [
        "temperature_2m",
        "relative_humidity_2m",
        "dew_point_2m",
        "apparent_temperature",
        "precipitation",
        "rain",
        "snowfall",
        "snow_depth",
        "pressure_msl",
        "surface_pressure",
        "cloud_cover",
        "wind_speed_10m",
        "wind_direction_10m",
        "wind_gusts_10m",
        "shortwave_radiation",
        "sunshine_duration",
        "soil_temperature_0_to_7cm",
        "soil_moisture_0_to_7cm"
    ];
}

public class EstonianEnvironmentAgencyOptions
{
    public bool Enabled { get; set; } = true;
    public string[] StationCodes { get; set; } = [];
    public string[] ElementCodes { get; set; } = ["TA", "RH", "PR1H"];
}
