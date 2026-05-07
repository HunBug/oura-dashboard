namespace OuraDashboard.Sync;

/// <summary>
/// Allows Blazor UI components to request an immediate sync without coupling to the background service directly.
/// </summary>
public interface ISyncTrigger
{
    /// <summary>Request an immediate out-of-schedule sync. Returns false if a sync is already running.</summary>
    bool RequestSync();

    /// <summary>Request a full historical sync using FullSyncLookbackDays. Returns false if a sync is already running.</summary>
    bool RequestFullSync();

    /// <summary>Request an immediate weather sync. Returns false if a sync is already running.</summary>
    bool RequestWeatherSync();

    /// <summary>Request a full historical weather sync. Returns false if a sync is already running.</summary>
    bool RequestFullWeatherSync();

    /// <summary>Current sync state, readable by UI components.</summary>
    SyncState State { get; }
}

public class SyncState
{
    public bool IsRunning { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? LastWeatherSyncAt { get; set; }
    public List<SyncResult> LastResults { get; set; } = [];
    public WeatherSyncResult? LastWeatherResult { get; set; }
    public List<string> LastErrors { get; set; } = [];
}
