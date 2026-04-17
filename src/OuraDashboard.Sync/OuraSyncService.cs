using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OuraDashboard.Data;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Sync;

public record SyncResult(
    string UserName,
    DateOnly Start,
    DateOnly End,
    int DailySleepCount,
    int SleepSessionCount,
    int ReadinessCount,
    int HeartRateSampleCount,
    int DailyStressCount,
    int DailyHrvCount,
    int DailyActivityCount,
    int Vo2MaxCount,
    List<string> Errors);

/// <summary>
/// Fetches all Oura endpoints for one user and upserts the results into the database.
/// Safe to call multiple times — all writes use upsert on natural keys.
/// </summary>
public class OuraSyncService(
    OuraDbContext db,
    IHttpClientFactory httpFactory,
    IOptions<OuraOptions> options,
    ILogger<OuraSyncService> logger,
    ILoggerFactory loggerFactory)
{
    public async Task<SyncResult> SyncUserAsync(
        string userName, int days, CancellationToken ct = default)
    {
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = end.AddDays(-(days - 1));
        var errors = new List<string>();

        var userConfig = options.Value.Users.FirstOrDefault(u => u.Name == userName);
        if (userConfig is null)
        {
            errors.Add($"No config found for user '{userName}'");
            return new SyncResult(userName, start, end, 0, 0, 0, 0, 0, 0, 0, 0, errors);
        }

        // Create a scoped HttpClient with this user's Bearer token
        var http = httpFactory.CreateClient("OuraApi");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userConfig.Token);
        var api = new OuraApiClient(http, loggerFactory.CreateLogger<OuraApiClient>());

        logger.LogInformation("Syncing {User}: {Start} → {End}", userName, start, end);

        // Ensure user row exists
        var user = await EnsureUserAsync(userName, ct);

        var counts = (
            DailySleep: 0, SleepSession: 0, Readiness: 0, HeartRate: 0,
            Stress: 0, Hrv: 0, Activity: 0, Vo2Max: 0);

        counts.DailySleep  = await SyncDailySleepAsync(user, api, start, end, errors, ct);
        counts.SleepSession = await SyncSleepSessionsAsync(user, api, start, end, errors, ct);
        counts.Readiness   = await SyncReadinessAsync(user, api, start, end, errors, ct);
        counts.HeartRate   = await SyncHeartRateAsync(user, api, start, end, errors, ct);
        counts.Stress      = await SyncDailyEndpointAsync(user, api, start, end, "daily_stress", errors, ct);
        counts.Hrv         = await SyncDailyEndpointAsync(user, api, start, end, "daily_hrv", errors, ct);
        counts.Activity    = await SyncDailyEndpointAsync(user, api, start, end, "daily_activity", errors, ct);
        counts.Vo2Max      = await SyncDailyEndpointAsync(user, api, start, end, "vo2_max", errors, ct);

        logger.LogInformation(
            "Sync complete for {User}: sleep={S} sessions={Ss} readiness={R} hr={Hr} stress={St} hrv={Hv} activity={A} vo2={V} errors={E}",
            userName, counts.DailySleep, counts.SleepSession, counts.Readiness, counts.HeartRate,
            counts.Stress, counts.Hrv, counts.Activity, counts.Vo2Max, errors.Count);

        return new SyncResult(userName, start, end,
            counts.DailySleep, counts.SleepSession, counts.Readiness, counts.HeartRate,
            counts.Stress, counts.Hrv, counts.Activity, counts.Vo2Max, errors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<OuraUser> EnsureUserAsync(string name, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == name, ct);
        if (user is null)
        {
            user = new OuraUser { Name = name };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    private async Task<int> SyncDailySleepAsync(
        OuraUser user, OuraApiClient api, DateOnly start, DateOnly end, List<string> errors, CancellationToken ct)
    {
        var doc = await api.FetchDailyAsync("daily_sleep", start, end, ct);
        if (doc is null) { errors.Add("daily_sleep: fetch failed"); return 0; }

        var items = doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().ToList()
            : [];

        int count = 0;
        foreach (var item in items)
        {
            try
            {
                var ouraId = item.GetProperty("id").GetString() ?? string.Empty;
                var day = DateOnly.Parse(item.GetProperty("day").GetString()!);

                var existing = await db.DailySleeps
                    .FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);

                var entity = existing ?? new DailySleep { UserId = user.Id };
                entity.OuraId = ouraId;
                entity.Day = day;

                if (item.TryGetProperty("score", out var score) && score.ValueKind != JsonValueKind.Null)
                    entity.Score = score.GetInt32();

                if (item.TryGetProperty("contributors", out var contrib))
                {
                    entity.DeepSleepContributor   = GetNullableInt(contrib, "deep_sleep");
                    entity.EfficiencyContributor   = GetNullableInt(contrib, "efficiency");
                    entity.LatencyContributor      = GetNullableInt(contrib, "latency");
                    entity.RemSleepContributor     = GetNullableInt(contrib, "rem_sleep");
                    entity.RestfulnessContributor  = GetNullableInt(contrib, "restfulness");
                    entity.TimingContributor       = GetNullableInt(contrib, "timing");
                    entity.TotalSleepContributor   = GetNullableInt(contrib, "total_sleep");
                }

                // Dispose previous RawJson before replacing
                existing?.RawJson.Dispose();
                entity.RawJson = JsonDocument.Parse(item.GetRawText());

                if (existing is null) db.DailySleeps.Add(entity);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse daily_sleep item for {User}", user.Name);
                errors.Add($"daily_sleep: parse error — {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> SyncSleepSessionsAsync(
        OuraUser user, OuraApiClient api, DateOnly start, DateOnly end, List<string> errors, CancellationToken ct)
    {
        var doc = await api.FetchDailyAsync("sleep", start, end, ct);
        if (doc is null) { errors.Add("sleep: fetch failed"); return 0; }

        var items = doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().ToList()
            : [];

        int count = 0;
        foreach (var item in items)
        {
            try
            {
                var ouraId = item.GetProperty("id").GetString() ?? string.Empty;
                var day = DateOnly.Parse(item.GetProperty("day").GetString()!);

                var existing = await db.SleepSessions
                    .FirstOrDefaultAsync(x => x.UserId == user.Id && x.OuraId == ouraId, ct);

                var entity = existing ?? new SleepSession { UserId = user.Id };
                entity.OuraId = ouraId;
                entity.Day = day;
                entity.BedtimeStart = DateTimeOffset.Parse(item.GetProperty("bedtime_start").GetString()!);
                entity.BedtimeEnd   = DateTimeOffset.Parse(item.GetProperty("bedtime_end").GetString()!);

                entity.AverageBreath      = GetNullableDouble(item, "average_breath");
                entity.AverageHeartRate   = GetNullableDouble(item, "average_heart_rate");
                entity.AverageHrv         = GetNullableInt(item, "average_hrv");
                entity.AwakeTime          = GetNullableInt(item, "awake_time");
                entity.DeepSleepDuration  = GetNullableInt(item, "deep_sleep_duration");
                entity.LightSleepDuration = GetNullableInt(item, "light_sleep_duration");
                entity.RemSleepDuration   = GetNullableInt(item, "rem_sleep_duration");
                entity.TotalSleepDuration = GetNullableInt(item, "total_sleep_duration");
                entity.TimeInBed          = GetNullableInt(item, "time_in_bed");
                entity.Efficiency         = GetNullableInt(item, "efficiency");
                entity.Latency            = GetNullableInt(item, "latency");
                entity.LowestHeartRate    = GetNullableInt(item, "lowest_heart_rate");
                entity.RestlessPeriods    = GetNullableInt(item, "restless_periods");
                entity.Type               = item.TryGetProperty("type", out var t) ? t.GetString() : null;

                entity.SleepPhase30Sec = item.TryGetProperty("sleep_phase_30_sec", out var sp30)
                    && sp30.ValueKind != JsonValueKind.Null ? sp30.GetString() : null;
                entity.SleepPhase5Min  = item.TryGetProperty("sleep_phase_5_min", out var sp5)
                    && sp5.ValueKind != JsonValueKind.Null ? sp5.GetString() : null;

                existing?.HeartRateSeries?.Dispose();
                existing?.HrvSeries?.Dispose();
                entity.HeartRateSeries = item.TryGetProperty("heart_rate", out var hr)
                    && hr.ValueKind != JsonValueKind.Null
                    ? JsonDocument.Parse(hr.GetRawText()) : null;
                entity.HrvSeries = item.TryGetProperty("hrv", out var hrv)
                    && hrv.ValueKind != JsonValueKind.Null
                    ? JsonDocument.Parse(hrv.GetRawText()) : null;

                existing?.RawJson.Dispose();
                entity.RawJson = JsonDocument.Parse(item.GetRawText());

                if (existing is null) db.SleepSessions.Add(entity);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse sleep session for {User}", user.Name);
                errors.Add($"sleep: parse error — {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> SyncReadinessAsync(
        OuraUser user, OuraApiClient api, DateOnly start, DateOnly end, List<string> errors, CancellationToken ct)
    {
        var doc = await api.FetchDailyAsync("daily_readiness", start, end, ct);
        if (doc is null) { errors.Add("daily_readiness: fetch failed"); return 0; }

        var items = doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().ToList()
            : [];

        int count = 0;
        foreach (var item in items)
        {
            try
            {
                var ouraId = item.GetProperty("id").GetString() ?? string.Empty;
                var day = DateOnly.Parse(item.GetProperty("day").GetString()!);

                var existing = await db.DailyReadinesses
                    .FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);

                var entity = existing ?? new DailyReadiness { UserId = user.Id };
                entity.OuraId = ouraId;
                entity.Day = day;
                entity.Score = GetNullableInt(item, "score");
                entity.TemperatureDeviation      = GetNullableDouble(item, "temperature_deviation");
                entity.TemperatureTrendDeviation = GetNullableDouble(item, "temperature_trend_deviation");

                if (item.TryGetProperty("contributors", out var contrib))
                {
                    entity.ActivityBalanceContributor    = GetNullableInt(contrib, "activity_balance");
                    entity.BodyTemperatureContributor    = GetNullableInt(contrib, "body_temperature");
                    entity.HrvBalanceContributor         = GetNullableInt(contrib, "hrv_balance");
                    entity.PreviousDayActivityContributor = GetNullableInt(contrib, "previous_day_activity");
                    entity.PreviousNightContributor      = GetNullableInt(contrib, "previous_night");
                    entity.RecoveryIndexContributor      = GetNullableInt(contrib, "recovery_index");
                    entity.RestingHeartRateContributor   = GetNullableInt(contrib, "resting_heart_rate");
                    entity.SleepBalanceContributor       = GetNullableInt(contrib, "sleep_balance");
                }

                existing?.RawJson.Dispose();
                entity.RawJson = JsonDocument.Parse(item.GetRawText());

                if (existing is null) db.DailyReadinesses.Add(entity);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse daily_readiness item for {User}", user.Name);
                errors.Add($"daily_readiness: parse error — {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> SyncHeartRateAsync(
        OuraUser user, OuraApiClient api, DateOnly start, DateOnly end, List<string> errors, CancellationToken ct)
    {
        var doc = await api.FetchHeartRateAsync(start, end, ct);
        if (doc is null) { errors.Add("heartrate: fetch failed"); return 0; }

        var items = doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().ToList()
            : [];

        // Bulk upsert: fetch existing timestamps for range, then insert only new ones
        var startDto = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endDto   = new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var existingTimestamps = await db.HeartRateSamples
            .Where(x => x.UserId == user.Id && x.Timestamp >= startDto && x.Timestamp <= endDto)
            .Select(x => x.Timestamp)
            .ToHashSetAsync(ct);

        var toInsert = new List<HeartRateSample>();
        int updateCount = 0;

        foreach (var item in items)
        {
            try
            {
                var timestamp = DateTimeOffset.Parse(item.GetProperty("timestamp").GetString()!);
                var bpm = item.GetProperty("bpm").GetInt32();
                var source = item.TryGetProperty("source", out var s) ? s.GetString() : null;

                if (!existingTimestamps.Contains(timestamp))
                {
                    toInsert.Add(new HeartRateSample
                    {
                        UserId = user.Id,
                        Timestamp = timestamp,
                        Bpm = bpm,
                        Source = source
                    });
                }
                else
                {
                    // Update BPM in case it changed (Oura occasionally revises data)
                    var existing = await db.HeartRateSamples
                        .FirstAsync(x => x.UserId == user.Id && x.Timestamp == timestamp, ct);
                    existing.Bpm = bpm;
                    existing.Source = source;
                    updateCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse heart rate sample for {User}", user.Name);
                errors.Add($"heartrate: parse error — {ex.Message}");
            }
        }

        if (toInsert.Count > 0)
            db.HeartRateSamples.AddRange(toInsert);

        await db.SaveChangesAsync(ct);
        return toInsert.Count + updateCount;
    }

    /// <summary>
    /// Generic handler for endpoints whose raw JSON we store but don't heavily extract from yet
    /// (daily_stress, daily_hrv, daily_activity, vo2_max).
    /// Stores the full raw payload; scalar extraction can be added per-entity later.
    /// </summary>
    private async Task<int> SyncDailyEndpointAsync(
        OuraUser user, OuraApiClient api, DateOnly start, DateOnly end,
        string endpoint, List<string> errors, CancellationToken ct)
    {
        var doc = await api.FetchDailyAsync(endpoint, start, end, ct);
        if (doc is null) { errors.Add($"{endpoint}: fetch failed"); return 0; }

        var items = doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().ToList()
            : [];

        int count = 0;
        foreach (var item in items)
        {
            try
            {
                var day = DateOnly.Parse(item.GetProperty("day").GetString()!);
                var ouraId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                var rawText = item.GetRawText();

                count += endpoint switch
                {
                    "daily_stress"   => await UpsertDailyStressAsync(user, day, ouraId, item, rawText, ct),
                    "daily_hrv"      => await UpsertDailyHrvAsync(user, day, ouraId, item, rawText, ct),
                    "daily_activity" => await UpsertDailyActivityAsync(user, day, ouraId, item, rawText, ct),
                    "vo2_max"        => await UpsertVo2MaxAsync(user, day, ouraId, item, rawText, ct),
                    _ => 0
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse {Endpoint} item for {User}", endpoint, user.Name);
                errors.Add($"{endpoint}: parse error — {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> UpsertDailyStressAsync(
        OuraUser user, DateOnly day, string ouraId, JsonElement item, string rawText, CancellationToken ct)
    {
        var existing = await db.DailyStresses.FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);
        var entity = existing ?? new DailyStress { UserId = user.Id };
        entity.OuraId = ouraId;
        entity.Day = day;
        entity.StressHigh    = GetNullableInt(item, "stress_high");
        entity.RecoveryHigh  = GetNullableInt(item, "recovery_high");
        entity.DaytimeStress = GetNullableInt(item, "day_summary");
        existing?.RawJson.Dispose();
        entity.RawJson = JsonDocument.Parse(rawText);
        if (existing is null) db.DailyStresses.Add(entity);
        return 1;
    }

    private async Task<int> UpsertDailyHrvAsync(
        OuraUser user, DateOnly day, string ouraId, JsonElement item, string rawText, CancellationToken ct)
    {
        var existing = await db.DailyHrvs.FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);
        var entity = existing ?? new DailyHrv { UserId = user.Id };
        entity.OuraId = ouraId;
        entity.Day = day;
        // Oura daily_hrv: { "balance_hrv": number } or similar; store raw, extract what we know
        entity.AvgHrv5Min = GetNullableDouble(item, "balance_hrv");
        existing?.RawJson.Dispose();
        entity.RawJson = JsonDocument.Parse(rawText);
        if (existing is null) db.DailyHrvs.Add(entity);
        return 1;
    }

    private async Task<int> UpsertDailyActivityAsync(
        OuraUser user, DateOnly day, string ouraId, JsonElement item, string rawText, CancellationToken ct)
    {
        var existing = await db.DailyActivities.FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);
        var entity = existing ?? new DailyActivity { UserId = user.Id };
        entity.OuraId = ouraId;
        entity.Day = day;
        entity.Steps                    = GetNullableInt(item, "steps");
        entity.ActiveCalories           = GetNullableInt(item, "active_calories");
        entity.TotalCalories            = GetNullableInt(item, "total_calories");
        entity.EquivalentWalkingDistance = GetNullableInt(item, "equivalent_walking_distance");
        entity.InactiveTime             = GetNullableInt(item, "inactive_time");
        entity.RestTime                 = GetNullableInt(item, "rest_time");
        entity.LowActivityTime          = GetNullableInt(item, "low_activity_time");
        entity.MediumActivityTime       = GetNullableInt(item, "medium_activity_time");
        entity.HighActivityTime         = GetNullableInt(item, "high_activity_time");
        existing?.RawJson.Dispose();
        entity.RawJson = JsonDocument.Parse(rawText);
        if (existing is null) db.DailyActivities.Add(entity);
        return 1;
    }

    private async Task<int> UpsertVo2MaxAsync(
        OuraUser user, DateOnly day, string ouraId, JsonElement item, string rawText, CancellationToken ct)
    {
        var existing = await db.Vo2Maxes.FirstOrDefaultAsync(x => x.UserId == user.Id && x.Day == day, ct);
        var entity = existing ?? new Vo2Max { UserId = user.Id };
        entity.OuraId = ouraId;
        entity.Day = day;
        entity.Vo2MaxValue = GetNullableDouble(item, "vo2_max");
        existing?.RawJson.Dispose();
        entity.RawJson = JsonDocument.Parse(rawText);
        if (existing is null) db.Vo2Maxes.Add(entity);
        return 1;
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static int? GetNullableInt(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        return prop.GetInt32();
    }

    private static double? GetNullableDouble(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        return prop.GetDouble();
    }
}
