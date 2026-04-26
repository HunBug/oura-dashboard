namespace OuraDashboard.Sync;

public class OuraOptions
{
    public const string SectionName = "Oura";

    public List<OuraUserConfig> Users { get; set; } = [];

    /// <summary>How often the background service triggers a sync. Default: 60 minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 60;

    /// <summary>How many days back to fetch on each scheduled/background sync run. Default: 2.</summary>
    public int SyncLookbackDays { get; set; } = 2;

    /// <summary>How many days back to fetch when a full/historical sync is requested. Default: 90.</summary>
    public int FullSyncLookbackDays { get; set; } = 90;
}

public class OuraUserConfig
{
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
