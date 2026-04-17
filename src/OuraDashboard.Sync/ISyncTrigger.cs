namespace OuraDashboard.Sync;

/// <summary>
/// Allows Blazor UI components to request an immediate sync without coupling to the background service directly.
/// </summary>
public interface ISyncTrigger
{
    /// <summary>Request an immediate out-of-schedule sync. Returns false if a sync is already running.</summary>
    bool RequestSync();

    /// <summary>Current sync state, readable by UI components.</summary>
    SyncState State { get; }
}

public class SyncState
{
    public bool IsRunning { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public List<SyncResult> LastResults { get; set; } = [];
    public List<string> LastErrors { get; set; } = [];
}
