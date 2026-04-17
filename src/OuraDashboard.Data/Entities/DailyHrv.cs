using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_hrv — one row per user per day.
/// </summary>
public class DailyHrv
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars
    public double? AvgHrv5Min { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
