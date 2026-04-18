using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/daily_spo2 — one row per user per day.
/// </summary>
public class DailySpo2
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    public int? BreathingDisturbanceIndex { get; set; }
    public double? Spo2Average { get; set; }

    public JsonDocument RawJson { get; set; } = null!;
}
