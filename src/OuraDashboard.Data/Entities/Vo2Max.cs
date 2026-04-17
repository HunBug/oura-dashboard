using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/vo2_max — one row per user per day (when available).
/// </summary>
public class Vo2Max
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Extracted scalars
    public double? Vo2MaxValue { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
