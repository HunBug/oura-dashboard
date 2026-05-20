using Microsoft.EntityFrameworkCore;
using OuraDashboard.Data;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Web.Services.Llm;

public sealed class LlmRequestStore(OuraDbContext db)
{
    public Task<LlmInteraction?> GetLatestNightInteractionAsync(int userId, DateOnly day, CancellationToken ct = default) =>
        db.LlmInteractions
            .AsNoTracking()
            .Where(x => x.Scope == LlmScopes.Night && x.UserId == userId && x.Day == day)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<LlmInteraction?> GetReusableInteractionAsync(string inputHash, DateTimeOffset earliestCreatedAtUtc, CancellationToken ct = default) =>
        db.LlmInteractions
            .AsNoTracking()
            .Where(x => x.InputHash == inputHash
                && x.CreatedAtUtc >= earliestCreatedAtUtc
                && (x.Status == LlmStatuses.Succeeded || x.Status == LlmStatuses.Running))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<LlmInteraction> InsertRunningAsync(LlmInteraction interaction, CancellationToken ct = default)
    {
        db.LlmInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);
        return interaction;
    }

    public async Task MarkSucceededAsync(
        long id,
        string responseText,
        string rawRequestJson,
        string rawResponseJson,
        int latencyMs,
        int? promptTokens,
        int? completionTokens,
        CancellationToken ct = default)
    {
        var row = await db.LlmInteractions.FirstAsync(x => x.Id == id, ct);
        row.Status = LlmStatuses.Succeeded;
        row.ResponseText = responseText;
        row.RawRequestJson = rawRequestJson;
        row.RawResponseJson = rawResponseJson;
        row.LatencyMs = latencyMs;
        row.PromptTokens = promptTokens;
        row.CompletionTokens = completionTokens;
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long id, string status, string code, string message, CancellationToken ct = default)
    {
        var row = await db.LlmInteractions.FirstAsync(x => x.Id == id, ct);
        row.Status = status;
        row.ErrorCode = code;
        row.ErrorMessage = message.Length > 1000 ? message[..1000] : message;
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

public static class LlmScopes
{
    public const string Night = "night";
}

public static class LlmStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string TimedOut = "timed_out";
    public const string Cancelled = "cancelled";
}
