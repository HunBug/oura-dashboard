using System.Text.Json;

namespace OuraDashboard.Data.Entities;

/// <summary>
/// Corresponds to GET /v2/usercollection/sleep — per-session detail with HR/HRV time series.
/// Multiple sessions can exist for a single day (e.g. nap + night).
/// </summary>
public class SleepSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OuraUser User { get; set; } = null!;

    public string OuraId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }

    // Window
    public DateTimeOffset BedtimeStart { get; set; }
    public DateTimeOffset BedtimeEnd { get; set; }

    // Extracted scalars (seconds unless noted)
    public double? AverageBreath { get; set; }
    public double? AverageHeartRate { get; set; }
    public int? AverageHrv { get; set; }
    public int? AwakeTime { get; set; }
    public int? DeepSleepDuration { get; set; }
    public int? LightSleepDuration { get; set; }
    public int? RemSleepDuration { get; set; }
    public int? TotalSleepDuration { get; set; }
    public int? TimeInBed { get; set; }
    public int? Efficiency { get; set; }
    public int? Latency { get; set; }
    public int? LowestHeartRate { get; set; }
    public int? RestlessPeriods { get; set; }
    public string? Type { get; set; }

    // Time-series stored as JSONB: { interval, items, timestamp }
    public JsonDocument? HeartRateSeries { get; set; }
    public JsonDocument? HrvSeries { get; set; }

    // Sleep stage strings (30-sec and 5-min resolution)
    public string? SleepPhase30Sec { get; set; }
    public string? SleepPhase5Min { get; set; }

    // Raw API response
    public JsonDocument RawJson { get; set; } = null!;
}
