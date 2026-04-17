using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_stress — one row per user per day.
/// </summary>
public class DailyStress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars (seconds)
    public int? StressHigh { get; set; }
    public int? RecoveryHigh { get; set; }
    public int? DaytimeStress { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
