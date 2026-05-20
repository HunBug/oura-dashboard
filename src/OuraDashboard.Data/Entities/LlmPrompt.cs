namespace OuraDashboard.Data.Entities;

public class LlmPrompt
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? Notes { get; set; }

    public OuraUser? User { get; set; }
}
