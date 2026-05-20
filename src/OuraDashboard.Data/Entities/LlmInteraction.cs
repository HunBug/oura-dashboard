namespace OuraDashboard.Data.Entities;

public class LlmInteraction
{
    public long Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? UserNameSnapshot { get; set; }
    public DateOnly? Day { get; set; }
    public DateOnly? StartDay { get; set; }
    public DateOnly? EndDay { get; set; }
    public string PromptKey { get; set; } = string.Empty;
    public int PromptVersion { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public string InputHash { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string MessagesJson { get; set; } = "[]";
    public string? ResponseText { get; set; }
    public string? ResponseJson { get; set; }
    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? LatencyMs { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public OuraUser? User { get; set; }
}
