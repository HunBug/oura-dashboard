using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OuraDashboard.Sync;

/// <summary>
/// Hosted background service that syncs Oura data on a timer and on-demand via ISyncTrigger.
/// Registered as a singleton so ISyncTrigger can expose live state to the Blazor UI.
/// </summary>
public sealed class SyncBackgroundService : BackgroundService, ISyncTrigger
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OuraOptions> _options;
    private readonly ILogger<SyncBackgroundService> _logger;

    // Bounded channel: capacity 1 means a second "refresh" click while one is queued does nothing
    private readonly Channel<bool> _triggerChannel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly SyncState _state = new();

    public SyncState State => _state;

    public SyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OuraOptions> options,
        ILogger<SyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public bool RequestSync()
    {
        if (_state.IsRunning) return false;
        return _triggerChannel.Writer.TryWrite(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncBackgroundService started. Interval: {Interval} min",
            _options.Value.SyncIntervalMinutes);

        var interval = TimeSpan.FromMinutes(_options.Value.SyncIntervalMinutes);

        // Run once on startup
        await RunSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(interval);

            try
            {
                // Wait until either the timer fires or a manual trigger arrives
                await _triggerChannel.Reader.ReadAsync(cts.Token);
                _logger.LogInformation("Manual sync triggered via UI");
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timer elapsed — scheduled sync
                _logger.LogInformation("Scheduled sync triggered");
            }

            if (!stoppingToken.IsCancellationRequested)
                await RunSyncAsync(stoppingToken);
        }
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        _state.IsRunning = true;
        _state.LastResults = [];
        _state.LastErrors = [];

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var options = _options.Value;
            var syncService = scope.ServiceProvider.GetRequiredService<OuraSyncService>();
            var results = new List<SyncResult>();

            foreach (var userConfig in options.Users)
            {
                var result = await syncService.SyncUserAsync(
                    userConfig.Name, options.SyncLookbackDays, ct);

                results.Add(result);
                _state.LastErrors.AddRange(result.Errors);
            }

            _state.LastResults = results;
            _state.LastSyncAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error during sync");
            _state.LastErrors.Add($"Unhandled: {ex.Message}");
        }
        finally
        {
            _state.IsRunning = false;
        }
    }
}
