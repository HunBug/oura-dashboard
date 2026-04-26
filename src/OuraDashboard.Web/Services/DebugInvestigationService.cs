using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OuraDashboard.Data;
using OuraDashboard.Sync;

namespace OuraDashboard.Web.Services;

public record DbDebugRow(
    string Endpoint,
    string Key,
    string Extracted,
    string RawJson);

public record LiveOuraDebugRow(
    string Endpoint,
    string Request,
    string Status,
    string RawJson);

public class DebugInvestigationService(
    OuraDbContext db,
    IHttpClientFactory httpFactory,
    IOptions<OuraOptions> options)
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    public DateOnly Today() => OuraTimeZone.Today(options.Value.DisplayTimeZoneId);

    public async Task<List<DbDebugRow>> GetStoredRowsAsync(string userName, DateOnly day, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null) return [];

        var rows = new List<DbDebugRow>();

        rows.AddRange(await db.DailySleeps
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_sleep",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"score={x.Score}; contributors: deep={x.DeepSleepContributor}, efficiency={x.EfficiencyContributor}, latency={x.LatencyContributor}, rem={x.RemSleepContributor}, restfulness={x.RestfulnessContributor}, timing={x.TimingContributor}, total={x.TotalSleepContributor}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.SleepSessions
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderByDescending(x => x.Type == "long_sleep")
            .ThenBy(x => x.BedtimeStart)
            .Select(x => new DbDebugRow(
                "sleep",
                $"{x.Day:yyyy-MM-dd} / {x.Type ?? "unknown"} / {x.OuraId}",
                $"bedtime_utc={x.BedtimeStart:u} - {x.BedtimeEnd:u}; avg_hrv={x.AverageHrv}; avg_hr={x.AverageHeartRate}; lowest_hr={x.LowestHeartRate}; deep_sec={x.DeepSleepDuration}; rem_sec={x.RemSleepDuration}; light_sec={x.LightSleepDuration}; awake_sec={x.AwakeTime}; hrv_ts={JsonTimestamp(x.HrvSeries)}; hr_ts={JsonTimestamp(x.HeartRateSeries)}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.DailyReadinesses
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_readiness",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"score={x.Score}; temp_dev={x.TemperatureDeviation}; temp_trend={x.TemperatureTrendDeviation}; contributors: activity={x.ActivityBalanceContributor}, body_temp={x.BodyTemperatureContributor}, hrv={x.HrvBalanceContributor}, prev_day={x.PreviousDayActivityContributor}, prev_night={x.PreviousNightContributor}, recovery={x.RecoveryIndexContributor}, rhr={x.RestingHeartRateContributor}, sleep_balance={x.SleepBalanceContributor}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.DailyActivities
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_activity",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"steps={x.Steps}; active_calories={x.ActiveCalories}; total_calories={x.TotalCalories}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.DailyStresses
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_stress",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"stress_high={x.StressHigh}; recovery_high={x.RecoveryHigh}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.DailySpo2s
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_spo2",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"spo2_average={x.Spo2Average}; breathing_disturbance_index={x.BreathingDisturbanceIndex}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.DailyResilienceRecords
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.Id)
            .Select(x => new DbDebugRow(
                "daily_resilience",
                $"{x.Day:yyyy-MM-dd} / {x.OuraId}",
                $"level={x.Level}; sleep_recovery={x.SleepRecovery}; daytime_recovery={x.DaytimeRecovery}; stress={x.Stress}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        rows.AddRange(await db.Workouts
            .Where(x => x.UserId == user.Id && x.Day == day)
            .OrderBy(x => x.StartDatetime)
            .Select(x => new DbDebugRow(
                "workout",
                $"{x.Day:yyyy-MM-dd} / {x.Activity ?? "unknown"} / {x.OuraId}",
                $"start_utc={x.StartDatetime:u}; end_utc={x.EndDatetime:u}; calories={x.Calories}; distance={x.Distance}; intensity={x.Intensity}; source={x.Source}",
                x.RawJson.RootElement.ToString()))
            .ToListAsync(ct));

        return rows.Select(r => r with { RawJson = Pretty(r.RawJson) }).ToList();
    }

    public async Task<List<LiveOuraDebugRow>> FetchLiveRowsAsync(string userName, DateOnly day, CancellationToken ct = default)
    {
        var userConfig = options.Value.Users.FirstOrDefault(u => u.Name == userName);
        if (userConfig is null)
        {
            return [new LiveOuraDebugRow("config", userName, "Missing Oura user config", "")];
        }

        var http = httpFactory.CreateClient("OuraApi");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userConfig.Token);

        var rows = new List<LiveOuraDebugRow>();
        foreach (var endpoint in new[]
        {
            "daily_sleep",
            "sleep",
            "daily_readiness",
            "daily_activity",
            "daily_stress",
            "daily_spo2",
            "daily_resilience",
            "workout",
        })
        {
            var request = $"/v2/usercollection/{endpoint}?start_date={day:yyyy-MM-dd}&end_date={day:yyyy-MM-dd}";
            rows.Add(await FetchAsync(http, endpoint, request, ct));
        }

        var previousDay = day.AddDays(-1);
        var nextDay = day.AddDays(1);
        var heartRateRequest = $"/v2/usercollection/heartrate?start_datetime={previousDay:yyyy-MM-dd}T00:00:00Z&end_datetime={nextDay:yyyy-MM-dd}T23:59:59Z";
        rows.Add(await FetchAsync(http, "heartrate", heartRateRequest, ct));

        return rows;
    }

    private static async Task<LiveOuraDebugRow> FetchAsync(HttpClient http, string endpoint, string request, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("https://api.ouraring.com" + request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new LiveOuraDebugRow(
                endpoint,
                request,
                $"{(int)response.StatusCode} {response.ReasonPhrase}",
                Pretty(body));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LiveOuraDebugRow(endpoint, request, "Fetch failed", ex.Message);
        }
    }

    private static string JsonTimestamp(JsonDocument? doc)
    {
        if (doc is null) return "null";
        return doc.RootElement.TryGetProperty("timestamp", out var timestamp)
            ? timestamp.GetString() ?? "null"
            : "missing";
    }

    private static string Pretty(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
