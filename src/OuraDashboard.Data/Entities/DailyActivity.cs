using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_activity — one row per user per day.
/// </summary>
public class DailyActivity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars
    public int? Steps { get; set; }
    public int? ActiveCalories { get; set; }
    public int? TotalCalories { get; set; }
    public int? EquivalentWalkingDistance { get; set; }

    // Minutes per intensity level
    public int? InactiveTime { get; set; }
    public int? RestTime { get; set; }
    public int? LowActivityTime { get; set; }
    public int? MediumActivityTime { get; set; }
    public int? HighActivityTime { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
