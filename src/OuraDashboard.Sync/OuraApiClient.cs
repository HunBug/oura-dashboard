using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OuraDashboard.Sync;

/// <summary>
/// Thin wrapper around the Oura v2 API. One instance per user (each has its own Bearer token).
/// </summary>
public class OuraApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OuraApiClient> _logger;

    private const string BaseUrl = "https://api.ouraring.com/v2/usercollection";

    public OuraApiClient(HttpClient http, ILogger<OuraApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Fetch a date-ranged endpoint that uses start_date / end_date query params.</summary>
    public Task<JsonDocument?> FetchDailyAsync(
        string endpoint, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{endpoint}?start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}";
        return FetchAsync(url, ct);
    }

    /// <summary>Fetch the heartrate endpoint which uses datetime params.</summary>
    public Task<JsonDocument?> FetchHeartRateAsync(
        DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/heartrate" +
                  $"?start_datetime={start:yyyy-MM-dd}T00:00:00Z" +
                  $"&end_datetime={end:yyyy-MM-dd}T23:59:59Z";
        return FetchAsync(url, ct);
    }

    private async Task<JsonDocument?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Oura API returned {Status} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch {Url}", url);
            return null;
        }
    }
}
