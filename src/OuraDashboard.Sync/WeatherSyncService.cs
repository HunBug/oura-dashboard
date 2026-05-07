using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OuraDashboard.Data;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Sync;

public record WeatherSyncResult(
    string LocationName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int OpenMeteoCount,
    int EstonianAgencyCount,
    int StationCount,
    List<string> Errors);

public class WeatherSyncService(
    OuraDbContext db,
    IHttpClientFactory httpFactory,
    IOptions<WeatherOptions> options,
    ILogger<WeatherSyncService> logger)
{
    private const string OpenMeteoSource = "open-meteo";
    private const string EstonianAgencySource = "estonian-environment-agency";

    public async Task<WeatherSyncResult> SyncAsync(
        int days,
        bool refreshExistingWindow = false,
        CancellationToken ct = default)
    {
        var config = options.Value;
        var errors = new List<string>();
        var timezone = OuraTimeZone.Resolve(config.Timezone);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        var endLocal = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, 0, 0, nowLocal.Offset).AddHours(-1);
        var requestedStartLocal = endLocal.AddDays(-(Math.Max(days, 1) - 1));

        var location = await EnsureLocationAsync(config, ct);
        var startUtc = requestedStartLocal.ToUniversalTime();
        var endUtc = endLocal.ToUniversalTime();

        if (endUtc < startUtc)
            return new WeatherSyncResult(location.Name, startUtc, endUtc, 0, 0, 0, errors);

        var stationCount = 0;
        var openMeteoCount = 0;
        var estonianCount = 0;

        if (config.Sources.OpenMeteo.Enabled)
        {
            var latest = await LatestSampleAsync(location.Id, OpenMeteoSource, config.Sources.OpenMeteo.Model, null, ct);
            var start = refreshExistingWindow ? startUtc : MissingStart(startUtc, latest);
            if (start <= endUtc)
                openMeteoCount = await SyncOpenMeteoAsync(location, config, start, endUtc, errors, ct);
        }

        if (config.Sources.EstonianEnvironmentAgency.Enabled)
        {
            var stations = await EnsureEstonianStationsAsync(location, config, errors, ct);
            stationCount = stations.Count;

            foreach (var station in stations)
            {
                var latest = await LatestSampleAsync(location.Id, EstonianAgencySource, null, station.Id, ct);
                var start = refreshExistingWindow ? startUtc : MissingStart(startUtc, latest);
                if (start <= endUtc)
                    estonianCount += await SyncEstonianStationElementAsync(location, station, config, start, endUtc, errors, ct);
            }
        }

        logger.LogInformation(
            "Weather sync complete for {Location}: openMeteo={OpenMeteo} estonian={Estonian} stations={Stations} errors={Errors}",
            location.Name, openMeteoCount, estonianCount, stationCount, errors.Count);

        return new WeatherSyncResult(location.Name, startUtc, endUtc, openMeteoCount, estonianCount, stationCount, errors);
    }

    private async Task<WeatherLocation> EnsureLocationAsync(WeatherOptions config, CancellationToken ct)
    {
        var location = await db.WeatherLocations.FirstOrDefaultAsync(x => x.Name == config.LocationName, ct);
        if (location is null)
        {
            location = new WeatherLocation { Name = config.LocationName };
            db.WeatherLocations.Add(location);
        }

        location.Latitude = config.Latitude;
        location.Longitude = config.Longitude;
        location.ElevationMeters = config.ElevationMeters;
        location.Timezone = config.Timezone;
        location.RawJson?.Dispose();
        location.RawJson = JsonSerializer.SerializeToDocument(config);

        await db.SaveChangesAsync(ct);
        return location;
    }

    private async Task<DateTimeOffset?> LatestSampleAsync(
        int locationId, string source, string? model, int? stationId, CancellationToken ct)
    {
        return await db.WeatherHourlySamples
            .Where(x => x.WeatherLocationId == locationId
                && x.Source == source
                && x.Model == model
                && x.WeatherStationId == stationId)
            .MaxAsync(x => (DateTimeOffset?)x.TimestampUtc, ct);
    }

    private static DateTimeOffset MissingStart(DateTimeOffset requestedStartUtc, DateTimeOffset? latest)
    {
        if (latest is null) return requestedStartUtc;
        var next = latest.Value.AddHours(1);
        return next > requestedStartUtc ? next : requestedStartUtc;
    }

    private async Task<int> SyncOpenMeteoAsync(
        WeatherLocation location,
        WeatherOptions config,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        List<string> errors,
        CancellationToken ct)
    {
        var http = httpFactory.CreateClient("OpenMeteoApi");
        var timezone = OuraTimeZone.Resolve(config.Timezone);
        var count = 0;

        foreach (var (chunkStartUtc, chunkEndUtc) in Chunks(startUtc, endUtc, TimeSpan.FromDays(90)))
        {
            var model = config.Sources.OpenMeteo.Model;
            var url = WeatherProviderQueries.BuildOpenMeteoArchivePath(location, config, chunkStartUtc, chunkEndUtc);

            try
            {
                using var doc = await GetJsonAsync(http, url, ct);
                if (!doc.RootElement.TryGetProperty("hourly", out var hourly)
                    || !hourly.TryGetProperty("time", out var times))
                {
                    errors.Add($"open-meteo: response had no hourly.time for {url}");
                    continue;
                }

                var timeValues = times.EnumerateArray().Select(x => x.GetString()).ToList();
                if (timeValues.Count == 0)
                    errors.Add($"open-meteo: response had zero hourly.time rows for {url}");

                for (var i = 0; i < timeValues.Count; i++)
                {
                    if (timeValues[i] is null) continue;

                    var localTime = ParseOpenMeteoLocal(timeValues[i]!, timezone);
                    var utc = localTime.ToUniversalTime();
                    if (utc < startUtc || utc > endUtc) continue;

                    var existing = await db.WeatherHourlySamples.FirstOrDefaultAsync(x =>
                        x.WeatherLocationId == location.Id
                        && x.Source == OpenMeteoSource
                        && x.Model == model
                        && x.WeatherStationId == null
                        && x.TimestampUtc == utc, ct);

                    var sample = existing ?? new WeatherHourlySample
                    {
                        WeatherLocationId = location.Id,
                        Source = OpenMeteoSource,
                        Model = model
                    };

                    sample.TimestampUtc = utc;
                    sample.TimestampLocal = localTime.DateTime;
                    sample.TemperatureC = GetHourlyDouble(hourly, "temperature_2m", i);
                    sample.RelativeHumidityPct = GetHourlyDouble(hourly, "relative_humidity_2m", i);
                    sample.DewPointC = GetHourlyDouble(hourly, "dew_point_2m", i);
                    sample.ApparentTemperatureC = GetHourlyDouble(hourly, "apparent_temperature", i);
                    sample.PrecipitationMm = GetHourlyDouble(hourly, "precipitation", i);
                    sample.RainMm = GetHourlyDouble(hourly, "rain", i);
                    sample.SnowfallCm = GetHourlyDouble(hourly, "snowfall", i);
                    sample.SnowDepthM = GetHourlyDouble(hourly, "snow_depth", i);
                    sample.PressureMslHpa = GetHourlyDouble(hourly, "pressure_msl", i);
                    sample.SurfacePressureHpa = GetHourlyDouble(hourly, "surface_pressure", i);
                    sample.CloudCoverPct = GetHourlyDouble(hourly, "cloud_cover", i);
                    sample.WindSpeedMs = GetHourlyDouble(hourly, "wind_speed_10m", i);
                    sample.WindDirectionDeg = GetHourlyDouble(hourly, "wind_direction_10m", i);
                    sample.WindGustMs = GetHourlyDouble(hourly, "wind_gusts_10m", i);
                    sample.ShortwaveRadiationWm2 = GetHourlyDouble(hourly, "shortwave_radiation", i);
                    sample.SunshineDurationSec = GetHourlyDouble(hourly, "sunshine_duration", i);
                    sample.SoilTemperature0To7CmC = GetHourlyDouble(hourly, "soil_temperature_0_to_7cm", i);
                    sample.SoilMoisture0To7Cm = GetHourlyDouble(hourly, "soil_moisture_0_to_7cm", i);

                    existing?.RawJson.Dispose();
                    sample.RawJson = BuildOpenMeteoRaw(hourly, i, model);

                    if (existing is null)
                        db.WeatherHourlySamples.Add(sample);
                    count++;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Open-Meteo weather sync failed for {Start} - {End}", chunkStartUtc, chunkEndUtc);
                errors.Add($"open-meteo {url}: {ex.Message}");
            }
        }

        return count;
    }

    private async Task<List<WeatherStation>> EnsureEstonianStationsAsync(
        WeatherLocation location,
        WeatherOptions config,
        List<string> errors,
        CancellationToken ct)
    {
        var stationOptions = config.Sources.EstonianEnvironmentAgency;
        var stations = new List<WeatherStation>();

        foreach (var elementCode in stationOptions.ElementCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var row = await FindEstonianStationMetadataAsync(location, stationOptions.StationCodes, elementCode, ct);
                if (row is null)
                {
                    errors.Add($"estonian-environment-agency: no station metadata for element {elementCode}");
                    continue;
                }

                var stationCode = GetString(row.Value, "jaam_kood") ?? "";
                var existing = await db.WeatherStations.FirstOrDefaultAsync(x =>
                    x.WeatherLocationId == location.Id
                    && x.Source == EstonianAgencySource
                    && x.StationCode == stationCode
                    && x.ElementCode == elementCode, ct);

                var station = existing ?? new WeatherStation
                {
                    WeatherLocationId = location.Id,
                    Source = EstonianAgencySource,
                    StationCode = stationCode,
                    ElementCode = elementCode
                };

                station.Name = GetString(row.Value, "jaam_nimi") ?? stationCode;
                station.Latitude = GetDouble(row.Value, "laiuskraad") ?? 0;
                station.Longitude = GetDouble(row.Value, "pikkuskraad") ?? 0;
                station.ElevationMeters = GetDouble(row.Value, "korgus_merepinnast_m");
                station.DistanceKm = WeatherProviderQueries.DistanceKm(location.Latitude, location.Longitude, station.Latitude, station.Longitude);
                station.ElementName = GetString(row.Value, "element_nimi_eng") ?? GetString(row.Value, "element_nimi");
                station.ObservationPeriodStart = GetDateTimeOffset(row.Value, "vaatlus_periood_algus");
                station.ObservationPeriodEnd = GetDateTimeOffset(row.Value, "vaatlus_periood_lopp");
                existing?.RawJson.Dispose();
                station.RawJson = JsonDocument.Parse(row.Value.GetRawText());

                if (existing is null)
                    db.WeatherStations.Add(station);

                stations.Add(station);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load Estonian station metadata for {Element}", elementCode);
                errors.Add($"estonian-environment-agency station {elementCode}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return stations;
    }

    private async Task<JsonElement?> FindEstonianStationMetadataAsync(
        WeatherLocation location, string[] configuredStationCodes, string elementCode, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("EstonianEnvironmentAgencyApi");
        var url = WeatherProviderQueries.BuildEstonianStationMetadataPath(configuredStationCodes, elementCode);

        using var doc = await GetJsonAsync(http, url, ct);
        return WeatherProviderQueries.FindClosestStationRow(doc.RootElement, location.Latitude, location.Longitude);
    }

    private async Task<int> SyncEstonianStationElementAsync(
        WeatherLocation location,
        WeatherStation station,
        WeatherOptions config,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        List<string> errors,
        CancellationToken ct)
    {
        var timezone = OuraTimeZone.Resolve(config.Timezone);
        var startLocal = TimeZoneInfo.ConvertTime(startUtc, timezone);
        var endLocal = TimeZoneInfo.ConvertTime(endUtc, timezone);
        var http = httpFactory.CreateClient("EstonianEnvironmentAgencyApi");
        var count = 0;

        foreach (var month in Months(startLocal, endLocal))
        {
            var url = WeatherProviderQueries.BuildEstonianHourlyPath(
                station.StationCode,
                station.ElementCode ?? "",
                month.Year,
                month.Month);

            try
            {
                using var doc = await GetJsonAsync(http, url, ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"estonian-environment-agency {station.StationCode}/{station.ElementCode} {month.Year}-{month.Month:00}: response root was {doc.RootElement.ValueKind} for {url}");
                    continue;
                }

                if (doc.RootElement.GetArrayLength() == 0)
                    errors.Add($"estonian-environment-agency {station.StationCode}/{station.ElementCode} {month.Year}-{month.Month:00}: zero rows for {url}");

                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    var localTime = ParseEstonianLocal(row, timezone);
                    if (localTime is null) continue;

                    var utc = localTime.Value.ToUniversalTime();
                    if (utc < startUtc || utc > endUtc) continue;

                    var existing = await db.WeatherHourlySamples.FirstOrDefaultAsync(x =>
                        x.WeatherLocationId == location.Id
                        && x.Source == EstonianAgencySource
                        && x.Model == null
                        && x.WeatherStationId == station.Id
                        && x.TimestampUtc == utc, ct);

                    var sample = existing ?? new WeatherHourlySample
                    {
                        WeatherLocationId = location.Id,
                        WeatherStationId = station.Id,
                        Source = EstonianAgencySource
                    };

                    sample.TimestampUtc = utc;
                    sample.TimestampLocal = localTime.Value.DateTime;
                    ApplyEstonianValue(sample, station.ElementCode, GetDouble(row, "vaartus"));

                    existing?.RawJson.Dispose();
                    sample.RawJson = JsonDocument.Parse(row.GetRawText());

                    if (existing is null)
                        db.WeatherHourlySamples.Add(sample);
                    count++;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Estonian weather sync failed for {Station} {Element} {Year}-{Month}",
                    station.StationCode, station.ElementCode, month.Year, month.Month);
                errors.Add($"estonian-environment-agency {station.StationCode}/{station.ElementCode} {month.Year}-{month.Month:00} {url}: {ex.Message}");
            }
        }

        return count;
    }

    private static void ApplyEstonianValue(WeatherHourlySample sample, string? elementCode, double? value)
    {
        switch (elementCode?.ToUpperInvariant())
        {
            case "TA":
                sample.TemperatureC = value;
                break;
            case "RH":
                sample.RelativeHumidityPct = value;
                break;
            case "PR1H":
                sample.PrecipitationMm = value;
                break;
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static DateTimeOffset ParseOpenMeteoLocal(string value, TimeZoneInfo timezone)
    {
        var local = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var offset = timezone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTimeOffset? ParseEstonianLocal(JsonElement row, TimeZoneInfo timezone)
    {
        var year = GetInt(row, "aasta");
        var month = GetInt(row, "kuu");
        var day = GetInt(row, "paev");
        var hour = GetInt(row, "tund");
        if (!year.HasValue || !month.HasValue || !day.HasValue || !hour.HasValue)
            return null;

        var local = new DateTime(year.Value, month.Value, day.Value, hour.Value, 0, 0);
        return new DateTimeOffset(local, timezone.GetUtcOffset(local));
    }

    private static double? GetHourlyDouble(JsonElement hourly, string name, int index)
    {
        if (!hourly.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;

        var values = array.EnumerateArray().ToList();
        if (index >= values.Count || values[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return values[index].GetDouble();
    }

    private static JsonDocument BuildOpenMeteoRaw(JsonElement hourly, int index, string model)
    {
        var raw = new Dictionary<string, object?> { ["model"] = model };
        foreach (var property in hourly.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array) continue;
            var values = property.Value.EnumerateArray().ToList();
            if (index >= values.Count || values[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                raw[property.Name] = null;
            else if (values[index].ValueKind == JsonValueKind.String)
                raw[property.Name] = values[index].GetString();
            else if (values[index].ValueKind == JsonValueKind.Number)
                raw[property.Name] = values[index].GetDouble();
        }

        return JsonSerializer.SerializeToDocument(raw);
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null ? prop.GetString() : null;

    private static int? GetInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null ? prop.GetInt32() : null;

    private static double? GetDouble(JsonElement item, string name) =>
        item.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null ? prop.GetDouble() : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement item, string name)
    {
        var value = GetString(item, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Chunks(
        DateTimeOffset start, DateTimeOffset end, TimeSpan size)
    {
        var current = start;
        while (current <= end)
        {
            var chunkEnd = current.Add(size);
            if (chunkEnd > end) chunkEnd = end;
            yield return (current, chunkEnd);
            current = chunkEnd.AddHours(1);
        }
    }

    private static IEnumerable<(int Year, int Month)> Months(DateTimeOffset startLocal, DateTimeOffset endLocal)
    {
        var current = new DateOnly(startLocal.Year, startLocal.Month, 1);
        var end = new DateOnly(endLocal.Year, endLocal.Month, 1);
        while (current <= end)
        {
            yield return (current.Year, current.Month);
            current = current.AddMonths(1);
        }
    }

}
