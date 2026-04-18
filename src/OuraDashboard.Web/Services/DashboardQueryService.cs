using System.Text.Json;
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

public record SamplePoint(DateTimeOffset Time, double? Value);

public record NightData(
    string UserName,
    DateOnly Day,
    int? SleepScore,
    int? ReadinessScore,
    double? TempDeviation,
    int? AverageHrv,
    double? AverageHeartRate,
    int? LowestHeartRate,
    double? AverageBreath,
    int? DeepMinutes,
    int? RemMinutes,
    int? AwakeMinutes,
    List<SamplePoint> HrvSeries,
    List<SamplePoint> HeartRateSeries);

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

        // Clamp start to first day we actually have session data — avoids empty left-side chart.
        var earliestDay = await db.SleepSessions
            .Where(x => x.UserId == user.Id)
            .MinAsync(x => (DateOnly?)x.Day, ct);
        if (earliestDay.HasValue && earliestDay.Value > start)
            start = earliestDay.Value;

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

    /// <summary>
    /// Returns the intra-night HR and HRV timeseries for a single night, plus scalars and scores.
    /// </summary>
    public async Task<NightData?> GetNightDetailAsync(string userName, DateOnly day, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null) return null;

        var session = await db.SleepSessions
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderByDescending(x => x.Type == "long_sleep")
            .ThenByDescending(x => (x.DeepSleepDuration ?? 0) + (x.RemSleepDuration ?? 0))
            .FirstOrDefaultAsync(ct);

        var score = await db.DailySleeps
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => (int?)x.Score).FirstOrDefaultAsync(ct);

        var readiness = await db.DailyReadinesses
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => new { x.Score, x.TemperatureDeviation })
            .FirstOrDefaultAsync(ct);

        return new NightData(
            UserName: userName,
            Day: day,
            SleepScore: score,
            ReadinessScore: readiness?.Score,
            TempDeviation: readiness?.TemperatureDeviation,
            AverageHrv: session?.AverageHrv,
            AverageHeartRate: session?.AverageHeartRate,
            LowestHeartRate: session?.LowestHeartRate,
            AverageBreath: session?.AverageBreath,
            DeepMinutes: session?.DeepSleepDuration is int d ? d / 60 : null,
            RemMinutes: session?.RemSleepDuration is int r ? r / 60 : null,
            AwakeMinutes: session?.AwakeTime is int a ? a / 60 : null,
            HrvSeries: ParseSeries(session?.HrvSeries),
            HeartRateSeries: ParseSeries(session?.HeartRateSeries));
    }

    /// <summary>Returns the days that have a long_sleep session, descending.</summary>
    public async Task<List<DateOnly>> GetNightDaysAsync(string userName, int days, CancellationToken ct = default)
    {
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = end.AddDays(-(days - 1));
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null) return [];
        return await db.SleepSessions
            .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end && x.Type == "long_sleep")
            .Select(x => x.Day)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync(ct);
    }

    private static List<SamplePoint> ParseSeries(JsonDocument? doc)
    {
        if (doc is null) return [];
        var root = doc.RootElement;
        if (!root.TryGetProperty("items", out var items)) return [];
        if (!root.TryGetProperty("timestamp", out var tsEl)) return [];
        if (!root.TryGetProperty("interval", out var intervalEl)) return [];

        var baseTime = DateTimeOffset.Parse(tsEl.GetString()!);
        var intervalSec = intervalEl.GetDouble();

        var result = new List<SamplePoint>();
        int index = 0;
        foreach (var item in items.EnumerateArray())
        {
            double? val = item.ValueKind == JsonValueKind.Null ? null : item.GetDouble();
            result.Add(new SamplePoint(baseTime.AddSeconds(intervalSec * index), val));
            index++;
        }
        return result;
    }
}
