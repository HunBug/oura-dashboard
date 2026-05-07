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
    private readonly IOptions<WeatherOptions> _weatherOptions;
    private readonly ILogger<SyncBackgroundService> _logger;

    private readonly Channel<SyncRequest> _triggerChannel = Channel.CreateBounded<SyncRequest>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly SyncState _state = new();

    public SyncState State => _state;

    public SyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OuraOptions> options,
        IOptions<WeatherOptions> weatherOptions,
        ILogger<SyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _weatherOptions = weatherOptions;
        _logger = logger;
    }

    public bool RequestSync()
    {
        if (_state.IsRunning) return false;
        return _triggerChannel.Writer.TryWrite(new SyncRequest(SyncRequestKind.Oura, _options.Value.SyncLookbackDays));
    }

    public bool RequestFullSync()
    {
        if (_state.IsRunning) return false;
        return _triggerChannel.Writer.TryWrite(new SyncRequest(SyncRequestKind.Oura, _options.Value.FullSyncLookbackDays));
    }

    public bool RequestWeatherSync()
    {
        if (_state.IsRunning || !_weatherOptions.Value.Enabled) return false;
        return _triggerChannel.Writer.TryWrite(new SyncRequest(SyncRequestKind.Weather, _weatherOptions.Value.SyncLookbackDays));
    }

    public bool RequestFullWeatherSync()
    {
        if (_state.IsRunning || !_weatherOptions.Value.Enabled) return false;
        return _triggerChannel.Writer.TryWrite(new SyncRequest(SyncRequestKind.Weather, _weatherOptions.Value.FullSyncLookbackDays));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncBackgroundService started. Oura interval: {OuraInterval} min. Weather interval: {WeatherInterval} h",
            _options.Value.SyncIntervalMinutes, _weatherOptions.Value.SyncIntervalHours);

        var ouraInterval = TimeSpan.FromMinutes(_options.Value.SyncIntervalMinutes);
        var weatherInterval = TimeSpan.FromHours(Math.Max(_weatherOptions.Value.SyncIntervalHours, 1));
        var nextOura = DateTimeOffset.UtcNow;
        var nextWeather = DateTimeOffset.UtcNow;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            while (_triggerChannel.Reader.TryRead(out var request))
            {
                _logger.LogInformation("Manual {Kind} sync triggered via UI ({Days} days)", request.Kind, request.Days);
                await RunSyncAsync(request, stoppingToken);
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= nextOura)
            {
                _logger.LogInformation("Scheduled Oura sync triggered");
                await RunSyncAsync(new SyncRequest(SyncRequestKind.Oura, _options.Value.SyncLookbackDays), stoppingToken);
                nextOura = DateTimeOffset.UtcNow.Add(ouraInterval);
            }

            if (_weatherOptions.Value.Enabled && now >= nextWeather)
            {
                _logger.LogInformation("Scheduled weather sync triggered");
                await RunSyncAsync(new SyncRequest(SyncRequestKind.Weather, _weatherOptions.Value.SyncLookbackDays), stoppingToken);
                nextWeather = DateTimeOffset.UtcNow.Add(weatherInterval);
            }
        }
    }

    private async Task RunSyncAsync(SyncRequest request, CancellationToken ct)
    {
        _state.IsRunning = true;
        _state.LastErrors = [];

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (request.Kind == SyncRequestKind.Oura)
            {
                _state.LastResults = [];
                var options = _options.Value;
                var syncService = scope.ServiceProvider.GetRequiredService<OuraSyncService>();
                var results = new List<SyncResult>();

                foreach (var userConfig in options.Users)
                {
                    var result = await syncService.SyncUserAsync(
                        userConfig.Name, request.Days, ct);

                    results.Add(result);
                    _state.LastErrors.AddRange(result.Errors);
                }

                _state.LastResults = results;
                _state.LastSyncAt = DateTimeOffset.UtcNow;
            }
            else
            {
                var weatherService = scope.ServiceProvider.GetRequiredService<WeatherSyncService>();
                var result = await weatherService.SyncAsync(request.Days, ct);
                _state.LastWeatherResult = result;
                _state.LastErrors.AddRange(result.Errors);
                _state.LastWeatherSyncAt = DateTimeOffset.UtcNow;
            }
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

    private enum SyncRequestKind { Oura, Weather }

    private readonly record struct SyncRequest(SyncRequestKind Kind, int Days);
}
