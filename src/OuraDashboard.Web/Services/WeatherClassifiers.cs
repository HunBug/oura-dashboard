namespace OuraDashboard.Web.Services;

public static class WeatherClassifiers
{
    public const double MinimumCoveragePct = 70.0;

    public static string PressureLevel(double? pressureChangeHpa, double coveragePct)
    {
        if (!pressureChangeHpa.HasValue || coveragePct < MinimumCoveragePct)
            return WeatherLevels.Insufficient;

        return pressureChangeHpa.Value switch
        {
            < 4.0 => WeatherLevels.Acceptable,
            <= 8.0 => WeatherLevels.Medium,
            _ => WeatherLevels.High,
        };
    }

    public static string SunLevel(double? sunnyHours, double coveragePct)
    {
        if (!sunnyHours.HasValue || coveragePct < MinimumCoveragePct)
            return WeatherLevels.Insufficient;

        return sunnyHours.Value switch
        {
            >= 5.0 => WeatherLevels.Enough,
            >= 2.0 => WeatherLevels.Middle,
            _ => WeatherLevels.Low,
        };
    }

    public static int PressureSeverity(string level) => level switch
    {
        WeatherLevels.Acceptable => 0,
        WeatherLevels.Medium => 1,
        WeatherLevels.High => 2,
        _ => -1,
    };
}

public static class WeatherLevels
{
    public const string Acceptable = "acceptable";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Enough = "enough";
    public const string Middle = "middle";
    public const string Low = "low";
    public const string Insufficient = "insufficient data";
}
