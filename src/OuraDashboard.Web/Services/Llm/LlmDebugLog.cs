namespace OuraDashboard.Web.Services.Llm;

public sealed class LlmDebugEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Scope { get; init; } = "";
    public string Model { get; init; } = "";
    public string MessagesJson { get; init; } = "";
    public string? RawRequestJson { get; init; }
    public string? RawResponseJson { get; init; }
    public string? ResponseText { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? LatencyMs { get; init; }
    public bool IsSandbox { get; init; }
}

public sealed class LlmDebugLog
{
    private const int Capacity = 3;
    private readonly Queue<LlmDebugEntry> _entries = new();
    private readonly object _lock = new();

    public void Push(LlmDebugEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }

    public IReadOnlyList<LlmDebugEntry> GetRecent()
    {
        lock (_lock)
            return [.. _entries.Reverse()];
    }
}
