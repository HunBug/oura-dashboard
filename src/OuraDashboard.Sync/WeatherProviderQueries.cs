using System.Globalization;
using System.Text.Json;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Sync;

public static class WeatherProviderQueries
{
    public static string BuildOpenMeteoArchivePath(
        WeatherLocation location,
        WeatherOptions config,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var timezone = OuraTimeZone.Resolve(config.Timezone);
        var chunkStartLocal = TimeZoneInfo.ConvertTime(startUtc, timezone);
        var chunkEndLocal = TimeZoneInfo.ConvertTime(endUtc, timezone);
        var model = config.Sources.OpenMeteo.Model;
        var variables = string.Join(",", config.Sources.OpenMeteo.HourlyVariables);

        return "v1/archive" +
            $"?latitude={Invariant(location.Latitude)}" +
            $"&longitude={Invariant(location.Longitude)}" +
            (location.ElevationMeters.HasValue ? $"&elevation={Invariant(location.ElevationMeters.Value)}" : "") +
            $"&start_date={chunkStartLocal:yyyy-MM-dd}" +
            $"&end_date={chunkEndLocal:yyyy-MM-dd}" +
            $"&hourly={Uri.EscapeDataString(variables)}" +
            $"&timezone={Uri.EscapeDataString(config.Timezone)}" +
            "&wind_speed_unit=ms" +
            $"&models={Uri.EscapeDataString(model)}";
    }

    public static string BuildEstonianStationMetadataPath(
        string[] configuredStationCodes,
        string elementCode)
    {
        var stationFilter = configuredStationCodes.Length > 0
            ? $"&jaam_kood=in.({string.Join(",", configuredStationCodes.Select(Uri.EscapeDataString))})"
            : "";

        return "f_kliima_jaam_vaatlus" +
            $"?element_kood=eq.{Uri.EscapeDataString(elementCode)}" +
            stationFilter +
            "&select=jaam_kood,jaam_nimi,pikkuskraad,laiuskraad,korgus_merepinnast_m,element_kood,element_nimi,element_nimi_eng,vaatlus_periood_algus,vaatlus_periood_lopp";
    }

    public static string BuildEstonianHourlyPath(
        string stationCode,
        string elementCode,
        int year,
        int month)
    {
        return "f_kliima_tund" +
            $"?jaam_kood=eq.{Uri.EscapeDataString(stationCode)}" +
            $"&element_kood=eq.{Uri.EscapeDataString(elementCode)}" +
            $"&aasta=eq.{year}" +
            $"&kuu=eq.{month}" +
            "&select=jaam_kood,jaam_nimi,aasta,kuu,paev,tund,vaartus,element_kood,element_nimi_eng,element_yhik_eng,avaandmed_ts" +
            "&order=paev.asc,tund.asc";
    }

    public static JsonElement? FindClosestStationRow(JsonElement root, double latitude, double longitude)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;

        JsonElement? best = null;
        double? bestDistance = null;
        foreach (var row in root.EnumerateArray())
        {
            var lat = GetDouble(row, "laiuskraad");
            var lon = GetDouble(row, "pikkuskraad");
            if (!lat.HasValue || !lon.HasValue) continue;

            var distance = DistanceKm(latitude, longitude, lat.Value, lon.Value);
            if (bestDistance is null || distance < bestDistance)
            {
                bestDistance = distance;
                best = row.Clone();
            }
        }

        return best;
    }

    public static double? GetDouble(JsonElement item, string name) =>
        item.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null ? prop.GetDouble() : null;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double radiusKm = 6371.0;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);
}
