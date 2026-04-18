using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_resilience — one row per user per day.
/// Level values: "limited" | "adequate" | "solid" | "strong" | "exceptional"
/// </summary>
public class DailyResilience
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    public string? Level { get; set; }
    public double? SleepRecovery { get; set; }
    public double? DaytimeRecovery { get; set; }
    public double? Stress { get; set; }

    public JsonDocument RawJson { get; set; } = null!;
}
