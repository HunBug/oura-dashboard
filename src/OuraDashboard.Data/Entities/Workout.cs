using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/workout — keyed by OuraId (multiple workouts per day possible).
/// </summary>
public class Workout
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    public string? Activity { get; set; }
    public double? Calories { get; set; }
    public double? Distance { get; set; }
    public string? Intensity { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? StartDatetime { get; set; }
    public DateTimeOffset? EndDatetime { get; set; }

    public JsonDocument RawJson { get; set; } = null!;
}
