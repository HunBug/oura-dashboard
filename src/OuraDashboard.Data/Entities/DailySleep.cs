using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_sleep — one row per user per day.
/// </summary>
public class DailySleep
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars
    public int? Score { get; set; }
    public int? DeepSleepContributor { get; set; }
    public int? EfficiencyContributor { get; set; }
    public int? LatencyContributor { get; set; }
    public int? RemSleepContributor { get; set; }
    public int? RestfulnessContributor { get; set; }
    public int? TimingContributor { get; set; }
    public int? TotalSleepContributor { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
