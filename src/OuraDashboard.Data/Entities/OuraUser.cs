namespace OuraDashboard.Data.Entities;

public class OuraUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Navigation
    public List<DailySleep> DailySleeps { get; set; } = [];
    public List<SleepSession> SleepSessions { get; set; } = [];
    public List<DailyReadiness> DailyReadinesses { get; set; } = [];
    public List<HeartRateSample> HeartRateSamples { get; set; } = [];
    public List<DailyStress> DailyStresses { get; set; } = [];
    public List<DailyHrv> DailyHrvs { get; set; } = [];
    public List<DailyActivity> DailyActivities { get; set; } = [];
    public List<Vo2Max> Vo2Maxes { get; set; } = [];
}
