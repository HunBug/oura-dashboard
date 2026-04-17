namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/heartrate — minute-by-minute samples.
/// High volume: ~1440 rows/user/day. Indexed on (UserId, Timestamp).
/// </summary>
public class HeartRateSample
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public int Bpm { get; set; }

    /// <summary>Source from Oura API: "awake", "rest", "sleep", "workout", etc.</summary>
    public string? Source { get; set; }
}
