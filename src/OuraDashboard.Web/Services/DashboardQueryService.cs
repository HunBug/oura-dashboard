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
    double? TempDeviation,
    double? HrAbove75Pct,
    int? RestorativeMinutes);

public record UserOverview(
    string UserName,
    List<DailyOverviewRow> Rows);

public record SamplePoint(DateTimeOffset Time, double? Value);

public record NightData(
    string UserName,
    DateOnly Day,
    // Scores
    int? SleepScore,
    int? ReadinessScore,
    double? TempDeviation,
    double? TempTrendDeviation,
    // Session scalars
    int? AverageHrv,
    double? AverageHeartRate,
    int? LowestHeartRate,
    double? AverageBreath,
    int? DeepMinutes,
    int? RemMinutes,
    int? LightSleepMinutes,
    int? AwakeMinutes,
    int? TotalSleepMinutes,
    int? TimeInBedMinutes,
    int? Efficiency,
    int? LatencyMinutes,
    int? RestlessPeriods,
    DateTimeOffset? BedtimeStart,
    DateTimeOffset? BedtimeEnd,
    string? SleepPhase5Min,
    // Sleep score contributors (0–100)
    int? SleepDeepContributor,
    int? SleepEfficiencyContributor,
    int? SleepLatencyContributor,
    int? SleepRemContributor,
    int? SleepRestfulnessContributor,
    int? SleepTimingContributor,
    int? SleepTotalContributor,
    // Readiness contributors (0–100)
    int? ReadinessActivityBalance,
    int? ReadinessBodyTemp,
    int? ReadinessHrvBalance,
    int? ReadinessPrevDayActivity,
    int? ReadinessPrevNight,
    int? ReadinessRecoveryIndex,
    int? ReadinessRhr,
    int? ReadinessSleepBalance,
    // Daytime context
    int? StressHighSec,
    int? RecoveryHighSec,
    int? Steps,
    int? ActiveCalories,
    double? Spo2Average,
    int? BreathingDisturbanceIndex,
    string? ResilienceLevel,
    double? ResilienceSleepRecovery,
    double? ResilienceDaytimeRecovery,
    // Timeseries
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
                x.BedtimeStart,
                x.BedtimeEnd,
            })
            .ToListAsync(ct);

        var sessionByDay = sessions
            .GroupBy(s => s.Day)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Type == "long_sleep")
                       .ThenByDescending(s => (s.DeepSleepDuration ?? 0) + (s.RemSleepDuration ?? 0))
                       .First());

        // Batch-load sleep-source HR samples for the period to compute HrAbove75Pct per night.
        var hrAbove75ByDay = new Dictionary<DateOnly, double?>();
        if (sessionByDay.Count > 0)
        {
            var windowStart = sessionByDay.Values.Min(s => s.BedtimeStart);
            var windowEnd   = sessionByDay.Values.Max(s => s.BedtimeEnd);
            var hrSamples   = await db.HeartRateSamples
                .Where(x => x.UserId == user.Id
                         && x.Source == "sleep"
                         && x.Timestamp >= windowStart
                         && x.Timestamp <= windowEnd)
                .Select(x => new { x.Timestamp, x.Bpm })
                .ToListAsync(ct);

            foreach (var (day, sess) in sessionByDay)
            {
                var sessHr = hrSamples
                    .Where(h => h.Timestamp >= sess.BedtimeStart && h.Timestamp <= sess.BedtimeEnd)
                    .ToList();
                hrAbove75ByDay[day] = sessHr.Count > 0
                    ? sessHr.Count(h => h.Bpm > 75) * 100.0 / sessHr.Count
                    : null;
            }
        }

        var rows = new List<DailyOverviewRow>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            scores.TryGetValue(day, out var score);
            readiness.TryGetValue(day, out var r);
            sessionByDay.TryGetValue(day, out var s);
            hrAbove75ByDay.TryGetValue(day, out var hrAbove75);

            int? restorative = null;
            if (s is not null && (s.DeepSleepDuration.HasValue || s.RemSleepDuration.HasValue))
                restorative = ((s.DeepSleepDuration ?? 0) + (s.RemSleepDuration ?? 0)) / 60;

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
                TempDeviation: r?.TemperatureDeviation,
                HrAbove75Pct: hrAbove75,
                RestorativeMinutes: restorative
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

        var sleep = await db.DailySleeps
            .Where(x => x.UserId == user.Id && x.Day == day)
            .FirstOrDefaultAsync(ct);

        var readiness = await db.DailyReadinesses
            .Where(x => x.UserId == user.Id && x.Day == day)
            .FirstOrDefaultAsync(ct);

        var stress = await db.DailyStresses
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => new { x.StressHigh, x.RecoveryHigh })
            .FirstOrDefaultAsync(ct);

        var activity = await db.DailyActivities
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => new { x.Steps, x.ActiveCalories })
            .FirstOrDefaultAsync(ct);

        var spo2 = await db.DailySpo2s
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => new { x.Spo2Average, x.BreathingDisturbanceIndex })
            .FirstOrDefaultAsync(ct);

        var resilience = await db.DailyResilienceRecords
            .Where(x => x.UserId == user.Id && x.Day == day)
            .Select(x => new { x.Level, x.SleepRecovery, x.DaytimeRecovery })
            .FirstOrDefaultAsync(ct);

        return new NightData(
            UserName: userName,
            Day: day,
            SleepScore: sleep?.Score,
            ReadinessScore: readiness?.Score,
            TempDeviation: readiness?.TemperatureDeviation,
            TempTrendDeviation: readiness?.TemperatureTrendDeviation,
            AverageHrv: session?.AverageHrv,
            AverageHeartRate: session?.AverageHeartRate,
            LowestHeartRate: session?.LowestHeartRate,
            AverageBreath: session?.AverageBreath,
            DeepMinutes: session?.DeepSleepDuration is int d ? d / 60 : null,
            RemMinutes: session?.RemSleepDuration is int r ? r / 60 : null,
            LightSleepMinutes: session?.LightSleepDuration is int light ? light / 60 : null,
            AwakeMinutes: session?.AwakeTime is int aw ? aw / 60 : null,
            TotalSleepMinutes: session?.TotalSleepDuration is int tot ? tot / 60 : null,
            TimeInBedMinutes: session?.TimeInBed is int tib ? tib / 60 : null,
            Efficiency: session?.Efficiency,
            LatencyMinutes: session?.Latency is int lat ? lat / 60 : null,
            RestlessPeriods: session?.RestlessPeriods,
            BedtimeStart: session?.BedtimeStart,
            BedtimeEnd: session?.BedtimeEnd,
            SleepPhase5Min: session?.SleepPhase5Min,
            SleepDeepContributor: sleep?.DeepSleepContributor,
            SleepEfficiencyContributor: sleep?.EfficiencyContributor,
            SleepLatencyContributor: sleep?.LatencyContributor,
            SleepRemContributor: sleep?.RemSleepContributor,
            SleepRestfulnessContributor: sleep?.RestfulnessContributor,
            SleepTimingContributor: sleep?.TimingContributor,
            SleepTotalContributor: sleep?.TotalSleepContributor,
            ReadinessActivityBalance: readiness?.ActivityBalanceContributor,
            ReadinessBodyTemp: readiness?.BodyTemperatureContributor,
            ReadinessHrvBalance: readiness?.HrvBalanceContributor,
            ReadinessPrevDayActivity: readiness?.PreviousDayActivityContributor,
            ReadinessPrevNight: readiness?.PreviousNightContributor,
            ReadinessRecoveryIndex: readiness?.RecoveryIndexContributor,
            ReadinessRhr: readiness?.RestingHeartRateContributor,
            ReadinessSleepBalance: readiness?.SleepBalanceContributor,
            StressHighSec: stress?.StressHigh,
            RecoveryHighSec: stress?.RecoveryHigh,
            Steps: activity?.Steps,
            ActiveCalories: activity?.ActiveCalories,
            Spo2Average: spo2?.Spo2Average,
            BreathingDisturbanceIndex: spo2?.BreathingDisturbanceIndex,
            ResilienceLevel: resilience?.Level,
            ResilienceSleepRecovery: resilience?.SleepRecovery,
            ResilienceDaytimeRecovery: resilience?.DaytimeRecovery,
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

    /// <summary>
    /// Returns raw JSON strings for the given endpoint and date range.
    /// Each element is the stored RawJson for one row (one API response object).
    /// </summary>
    /// <param name="endpoint">
    ///   One of: daily_sleep, sleep, daily_readiness, daily_stress,
    ///   daily_activity, vo2_max, daily_spo2, daily_resilience, workout.
    /// </param>
    public async Task<List<string>> GetRawExportAsync(
        string userName,
        DateOnly start,
        DateOnly end,
        string endpoint,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null) return [];

        return endpoint switch
        {
            "daily_sleep" => await db.DailySleeps
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "sleep" => await db.SleepSessions
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "daily_readiness" => await db.DailyReadinesses
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "daily_stress" => await db.DailyStresses
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "daily_activity" => await db.DailyActivities
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "vo2_max" => await db.Vo2Maxes
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "daily_spo2" => await db.DailySpo2s
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "daily_resilience" => await db.DailyResilienceRecords
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            "workout" => await db.Workouts
                .Where(x => x.UserId == user.Id && x.Day >= start && x.Day <= end)
                .OrderBy(x => x.Day)
                .Select(x => x.RawJson.RootElement.ToString())
                .ToListAsync(ct),

            _ => []
        };
    }
}
