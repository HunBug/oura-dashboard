using Microsoft.EntityFrameworkCore;
using OuraDashboard.Data;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Web.Services;

public record DailyOverviewRow(
    DateOnly Day,
    int? SleepScore,
    int? ReadinessScore,
    int? AvgHrv,
    double? AvgHr,
    int? LowestHr,
    double? AvgBreath,
    int? DeepMinutes,
    int? RemMinutes,
    int? AwakeMinutes,
    double? TempDeviation);

public record UserOverview(
    string UserName,
    List<DailyOverviewRow> Rows);

public class DashboardQueryService(OuraDbContext db)
{
    /// <summary>
    /// Returns daily overview rows for a user, last <paramref name="days"/> days,
    /// joining DailySleep (score), DailyReadiness, and the primary SleepSession.
    /// "long_sleep" sessions are preferred; if absent, falls back to any session for that day.
    /// </summary>
    public async Task<UserOverview> GetUserOverviewAsync(string userName, int days, CancellationToken ct = default)
    {
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = end.AddDays(-(days - 1));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null) return new UserOverview(userName, []);

        var scores = await db.DailySleeps
            .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
            .Select(x => new { x.Day, x.Score })
            .ToDictionaryAsync(x => x.Day, x => x.Score, ct);

        var readiness = await db.DailyReadinesses
            .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
            .Select(x => new { x.Day, x.Score, x.TemperatureDeviation })
            .ToDictionaryAsync(x => x.Day, ct);

        var sessions = await db.SleepSessions
            .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
            .Select(x => new
            {
                x.Day,
                x.Type,
                x.AverageHrv,
                x.AverageHeartRate,
                x.LowestHeartRate,
                x.AverageBreath,
                x.DeepSleepDuration,
                x.RemSleepDuration,
                x.AwakeTime,
            })
            .ToListAsync(ct);

        var sessionByDay = sessions
            .GroupBy(s => s.Day)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Type == "long_sleep")
                       .ThenByDescending(s => (s.DeepSleepDuration ?? 0) + (s.RemSleepDuration ?? 0))
                       .First());

        var rows = new List<DailyOverviewRow>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            scores.TryGetValue(day, out var score);
            readiness.TryGetValue(day, out var r);
            sessionByDay.TryGetValue(day, out var s);

            rows.Add(new DailyOverviewRow(
                Day: day,
                SleepScore: score,
                ReadinessScore: r?.Score,
                AvgHrv: s?.AverageHrv,
                AvgHr: s?.AverageHeartRate,
                LowestHr: s?.LowestHeartRate,
                AvgBreath: s?.AverageBreath,
                DeepMinutes: s?.DeepSleepDuration is int d ? d / 60 : null,
                RemMinutes: s?.RemSleepDuration is int rem ? rem / 60 : null,
                AwakeMinutes: s?.AwakeTime is int a ? a / 60 : null,
                TempDeviation: r?.TemperatureDeviation
            ));
        }

        return new UserOverview(userName, rows);
    }
}
