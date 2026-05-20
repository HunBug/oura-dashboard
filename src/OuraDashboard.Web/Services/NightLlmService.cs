using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OuraDashboard.Data;
using OuraDashboard.Data.Entities;
using OuraDashboard.Web.Services.Llm;

namespace OuraDashboard.Web.Services;

public sealed class NightLlmService(
    OuraDbContext db,
    LlmRequestStore store,
    ILlmClient client,
    LlmDebugLog debugLog,
    IOptions<LlmOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public bool IsEnabled => options.Value.Enabled;
    public bool IsSandboxMode => options.Value.SandboxMode;

    public async Task<LlmInteraction?> GetLatestNoteAsync(string userName, DateOnly day, CancellationToken ct = default)
    {
        if (options.Value.SandboxMode)
            return null;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Name == userName, ct);
        return user is null ? null : await store.GetLatestNightInteractionAsync(user.Id, day, ct);
    }

    public async Task<LlmInteraction> GenerateNightNoteAsync(
        NightData night,
        NightMetrics metrics,
        WeatherNightContext? weather,
        bool forceNew = false,
        CancellationToken ct = default)
    {
        var opts = options.Value;
        var parameters = new LlmParameters(opts.Temperature, opts.TopP, opts.TopK, opts.RepeatPenalty, opts.NumPredict, opts.NumCtx);
        var parametersJson = JsonSerializer.Serialize(parameters, JsonOptions);
        var input = NightSummaryInput.From(night, metrics, weather);
        var inputJson = JsonSerializer.Serialize(input, JsonOptions);
        if (inputJson.Length > opts.MaxPromptChars)
            throw new InvalidOperationException($"Night LLM input is too large ({inputJson.Length} chars).");

        var messages = new[]
        {
            new LlmMessage("system", PromptCatalog.SystemPrompt),
            new LlmMessage("user", $"{PromptCatalog.UserContext(night.UserName)}\n\n{PromptCatalog.NightSummaryPrompt}\n\nData JSON:\n{inputJson}"),
        };
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions);

        if (opts.SandboxMode)
            return await GenerateSandboxAsync(night, opts, parameters, inputJson, messagesJson, ct);

        return await GeneratePersistedAsync(night, opts, parameters, parametersJson, inputJson, messagesJson, forceNew, ct);
    }

    private async Task<LlmInteraction> GenerateSandboxAsync(
        NightData night,
        LlmOptions opts,
        LlmParameters parameters,
        string inputJson,
        string messagesJson,
        CancellationToken ct)
    {
        var messages = JsonSerializer.Deserialize<LlmMessage[]>(messagesJson, JsonOptions)!;
        var now = DateTimeOffset.UtcNow;
        string? responseText = null, rawRequestJson = null, rawResponseJson = null, errorCode = null, errorMessage = null;
        int? latencyMs = null;

        try
        {
            var response = await client.CompleteChatAsync(new LlmChatRequest(opts.Model, messages, parameters, opts.KeepAlive), ct);
            responseText = response.Text.Length > opts.MaxResponseChars ? response.Text[..opts.MaxResponseChars] : response.Text;
            rawRequestJson = response.RawRequestJson;
            rawResponseJson = response.RawResponseJson;
            latencyMs = response.LatencyMs;
        }
        catch (LlmClientException ex)
        {
            errorCode = ex.Code;
            errorMessage = ex.Message;
            rawRequestJson = ex.RawRequestJson;
            rawResponseJson = ex.RawResponseJson;
        }
        catch (OperationCanceledException)
        {
            errorCode = "cancelled";
            errorMessage = "The request was cancelled.";
        }

        debugLog.Push(new LlmDebugEntry
        {
            Timestamp = now,
            Scope = LlmScopes.Night,
            Model = opts.Model,
            MessagesJson = messagesJson,
            RawRequestJson = rawRequestJson,
            RawResponseJson = rawResponseJson,
            ResponseText = responseText,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            LatencyMs = latencyMs,
            IsSandbox = true,
        });

        return new LlmInteraction
        {
            Id = 0,
            Scope = LlmScopes.Night,
            UserId = 0,
            UserNameSnapshot = night.UserName,
            Day = night.Day,
            PromptKey = PromptCatalog.NightSummaryKey,
            PromptVersion = PromptCatalog.Version,
            Model = opts.Model,
            Provider = "ollama",
            Status = errorCode is null ? LlmStatuses.Succeeded : LlmStatuses.Failed,
            ResponseText = responseText,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            LatencyMs = latencyMs,
            CreatedAtUtc = now,
            StartedAtUtc = now,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<LlmInteraction> GeneratePersistedAsync(
        NightData night,
        LlmOptions opts,
        LlmParameters parameters,
        string parametersJson,
        string inputJson,
        string messagesJson,
        bool forceNew,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Name == night.UserName, ct);
        var messages = JsonSerializer.Deserialize<LlmMessage[]>(messagesJson, JsonOptions)!;
        var inputHash = ComputeHash(PromptCatalog.NightSummaryKey, PromptCatalog.Version, opts.Model, parametersJson, night.UserName, night.Day, inputJson, messagesJson);

        if (!forceNew)
        {
            var reusableAfter = DateTimeOffset.UtcNow.AddHours(-Math.Max(0, opts.RequestCacheTtlHours));
            var reusable = await store.GetReusableInteractionAsync(inputHash, reusableAfter, ct);
            if (reusable is not null)
                return reusable;
        }

        var now = DateTimeOffset.UtcNow;
        var row = await store.InsertRunningAsync(new LlmInteraction
        {
            Scope = LlmScopes.Night,
            UserId = user.Id,
            UserNameSnapshot = user.Name,
            Day = night.Day,
            PromptKey = PromptCatalog.NightSummaryKey,
            PromptVersion = PromptCatalog.Version,
            Model = opts.Model,
            Provider = "ollama",
            ParametersJson = parametersJson,
            InputHash = inputHash,
            InputJson = inputJson,
            MessagesJson = messagesJson,
            Status = LlmStatuses.Running,
            CreatedAtUtc = now,
            StartedAtUtc = now,
        }, ct);

        string? rawRequestJson = null, rawResponseJson = null;
        try
        {
            var response = await client.CompleteChatAsync(new LlmChatRequest(opts.Model, messages, parameters, opts.KeepAlive), ct);
            rawRequestJson = response.RawRequestJson;
            rawResponseJson = response.RawResponseJson;
            await store.MarkSucceededAsync(row.Id, response.Text, rawRequestJson, rawResponseJson, response.LatencyMs, response.PromptTokens, response.CompletionTokens, ct);

            debugLog.Push(new LlmDebugEntry
            {
                Timestamp = now,
                Scope = LlmScopes.Night,
                Model = opts.Model,
                MessagesJson = messagesJson,
                RawRequestJson = rawRequestJson,
                RawResponseJson = rawResponseJson,
                ResponseText = response.Text,
                LatencyMs = response.LatencyMs,
                IsSandbox = false,
            });
        }
        catch (LlmClientException ex)
        {
            var status = ex.Code == "timeout" ? LlmStatuses.TimedOut : LlmStatuses.Failed;
            await store.MarkFailedAsync(row.Id, status, ex.Code, ex.Message, CancellationToken.None);

            debugLog.Push(new LlmDebugEntry
            {
                Timestamp = now,
                Scope = LlmScopes.Night,
                Model = opts.Model,
                MessagesJson = messagesJson,
                RawRequestJson = ex.RawRequestJson,
                RawResponseJson = ex.RawResponseJson,
                ErrorCode = ex.Code,
                ErrorMessage = ex.Message,
                IsSandbox = false,
            });
        }
        catch (OperationCanceledException)
        {
            await store.MarkFailedAsync(row.Id, LlmStatuses.Cancelled, "cancelled", "The request was cancelled.", CancellationToken.None);
        }

        return await db.LlmInteractions.AsNoTracking().FirstAsync(x => x.Id == row.Id, CancellationToken.None);
    }

    private static string ComputeHash(params object?[] parts)
    {
        var joined = string.Join('\n', parts.Select(x => x?.ToString() ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record NightSummaryInput(
        string UserName,
        DateOnly Day,
        SleepSnapshot Sleep,
        CustomMetricSnapshot Metrics,
        WeatherSnapshot? Weather,
        DaytimeSnapshot Daytime)
    {
        public static NightSummaryInput From(NightData d, NightMetrics m, WeatherNightContext? w) => new(
            d.UserName,
            d.Day,
            new SleepSnapshot(
                d.SleepScore,
                d.ReadinessScore,
                d.AverageHrv,
                d.AverageHeartRate,
                d.LowestHeartRate,
                d.AverageBreath,
                d.DeepMinutes,
                d.RemMinutes,
                d.LightSleepMinutes,
                d.AwakeMinutes,
                d.TotalSleepMinutes,
                d.TimeInBedMinutes,
                d.Efficiency,
                d.LatencyMinutes,
                d.RestlessPeriods,
                d.TempDeviation,
                d.TempTrendDeviation,
                d.BedtimeStart,
                d.BedtimeEnd),
            new CustomMetricSnapshot(
                m.RealRecoveryScore,
                m.HrAbove75Pct,
                m.HrAbove80Pct,
                m.HrSettlingMinutes,
                m.HrShape,
                m.HrvDirection,
                m.HrvEarlyHalfAvg,
                m.HrvLateHalfAvg,
                m.HrvPeak,
                m.HrvBelow12Pct,
                m.Hrv12To20Pct,
                m.HrvAbove20Pct,
                m.RestorativeMinutes,
                m.AwakePctOfBed),
            w is null ? null : new WeatherSnapshot(
                w.NightPressure.Level,
                w.NightPressure.PressureChangeHpa,
                w.NightPressure.CoveragePct,
                w.PreviousDaySun.Level,
                w.PreviousDaySun.SunnyHours,
                w.PreviousDaySun.CoveragePct,
                w.PreviousDayPressure.Level,
                w.PreviousDayPressure.PressureChangeHpa,
                w.PreviousDayPressure.CoveragePct),
            new DaytimeSnapshot(
                d.Steps,
                d.ActiveCalories,
                d.StressHighSec,
                d.RecoveryHighSec,
                d.Spo2Average,
                d.BreathingDisturbanceIndex,
                d.ResilienceLevel,
                d.ResilienceSleepRecovery,
                d.ResilienceDaytimeRecovery));
    }

    private sealed record SleepSnapshot(
        int? SleepScore,
        int? ReadinessScore,
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
        double? TempDeviation,
        double? TempTrendDeviation,
        DateTimeOffset? BedtimeStart,
        DateTimeOffset? BedtimeEnd);

    private sealed record CustomMetricSnapshot(
        int? RealRecoveryScore,
        double? HrAbove75Pct,
        double? HrAbove80Pct,
        int? HrSettlingMinutes,
        string HrShape,
        string HrvDirection,
        double? HrvEarlyHalfAvg,
        double? HrvLateHalfAvg,
        double? HrvPeak,
        double? HrvBelow12Pct,
        double? Hrv12To20Pct,
        double? HrvAbove20Pct,
        int? RestorativeMinutes,
        double? AwakePctOfBed);

    private sealed record WeatherSnapshot(
        string NightPressureLevel,
        double? NightPressureChangeHpa,
        double NightPressureCoveragePct,
        string PreviousDaySunLevel,
        double? PreviousDaySunnyHours,
        double PreviousDaySunCoveragePct,
        string PreviousDayPressureLevel,
        double? PreviousDayPressureChangeHpa,
        double PreviousDayPressureCoveragePct);

    private sealed record DaytimeSnapshot(
        int? Steps,
        int? ActiveCalories,
        int? StressHighSec,
        int? RecoveryHighSec,
        double? Spo2Average,
        int? BreathingDisturbanceIndex,
        string? ResilienceLevel,
        double? ResilienceSleepRecovery,
        double? ResilienceDaytimeRecovery);
}
