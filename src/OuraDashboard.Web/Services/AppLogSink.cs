using System.Collections.Concurrent;

namespace OuraDashboard.Web.Services;

/// <summary>
/// A single captured log entry (Warning level or above).
/// </summary>
public record AppLogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionText);

/// <summary>
/// In-memory circular buffer of recent Warning+ log entries.
/// Registered as both ILoggerProvider (so it intercepts the logging pipeline)
/// and as a singleton service (so Blazor components can inject it).
///
/// Deliberately captures only Warning and above — we don't want to flood
/// the buffer with Info/Debug noise.
/// </summary>
public sealed class AppLogSink : ILoggerProvider
{
    private const int MaxEntries = 200;
    private readonly ConcurrentQueue<AppLogEntry> _entries = new();

    // ── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<AppLogEntry> Entries => [.. _entries];

    public int ErrorCount => _entries.Count(e => e.Level >= LogLevel.Error);

    public int WarningCount => _entries.Count(e => e.Level == LogLevel.Warning);

    public void Clear() => _entries.Clear();

    // ── ILoggerProvider ──────────────────────────────────────────────────────

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void Dispose() { }

    // ── Internal write ───────────────────────────────────────────────────────

    internal void AddEntry(AppLogEntry entry)
    {
        _entries.Enqueue(entry);

        // Trim oldest entries once we exceed the cap
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    // ── Inner logger ─────────────────────────────────────────────────────────

    private sealed class SinkLogger(AppLogSink sink, string category) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;

            sink.AddEntry(new AppLogEntry(
                TimestampUtc: DateTime.UtcNow,
                Level: logLevel,
                Category: category,
                Message: formatter(state, exception),
                ExceptionText: exception?.ToString()));
        }
    }
}
