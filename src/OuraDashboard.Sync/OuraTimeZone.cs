namespace OuraDashboard.Sync;

public static class OuraTimeZone
{
    public const string DefaultId = "Europe/Tallinn";

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? DefaultId : timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public static DateOnly Today(string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Resolve(timeZoneId)).DateTime);
}
