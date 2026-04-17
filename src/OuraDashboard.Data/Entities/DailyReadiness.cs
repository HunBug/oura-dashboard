using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_readiness — one row per user per day.
/// </summary>
public class DailyReadiness
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars
    public int? Score { get; set; }
    public double? TemperatureDeviation { get; set; }
    public double? TemperatureTrendDeviation { get; set; }

    // Contributors
    public int? ActivityBalanceContributor { get; set; }
    public int? BodyTemperatureContributor { get; set; }
    public int? HrvBalanceContributor { get; set; }
    public int? PreviousDayActivityContributor { get; set; }
    public int? PreviousNightContributor { get; set; }
    public int? RecoveryIndexContributor { get; set; }
    public int? RestingHeartRateContributor { get; set; }
    public int? SleepBalanceContributor { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
