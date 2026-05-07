
# Weather Data Plan

## Goal

Add historical weather data as a separate input stream for later comparison with Oura sleep and recovery metrics.

Phase 1 is collection only. The UI, correlations, and statistical analysis come later.

## Location

Use a configurable point location instead of hard-coding a provider-specific station.

Suggested config:

```json
"Weather": {
  "Enabled": true,
  "LocationName": "Roela",
  "Latitude": 58.7078,
  "Longitude": 26.7625,
  "ElevationMeters": 84,
  "Timezone": "Europe/Tallinn",
  "SyncIntervalHours": 24,
  "LookbackDays": 14,
  "FullSyncLookbackDays": 3650,
  "Sources": {
    "EstonianEnvironmentAgency": {
      "Enabled": true,
      "NearestStationCodes": []
    },
    "OpenMeteo": {
      "Enabled": true,
      "Model": "best_match"
    },
    "Meteostat": {
      "Enabled": false,
      "ApiKey": ""
    }
  }
}
```

Roela is ambiguous enough that the exact coordinates should be confirmed before import. The Roela/Roela-area point above is only a working default. If the intended location is near Väike-Maarja instead, use the house/village coordinates rather than station coordinates.

## Source Ranking

### 1. Estonian Environment Agency open data

Use this as the primary source for Estonia when available.

Why:

- Official Estonian source.
- Free/public; no JWT required according to the API documentation.
- JSON PostgREST API is available at `https://keskkonnaandmed.envir.ee`.
- Climate datasets include station metadata, monthly, daily, hourly, and 10-minute data tables.
- Meteorological monitoring data also exposes recent raw automatic station BUFR files for the last 24h at 10-minute and hourly precision.
- License is CC-BY 4.0 in the open data catalogue.

Important endpoints:

- `f_kliima_jaam_vaatlus` - station metadata and station/element observation periods.
- `f_kliima_element` - weather element metadata.
- `f_kliima_tund` - hourly historical climate data.
- `f_kliima_minut` - 10-minute historical climate data.
- `f_kliima_paev` - daily historical climate data.

Implementation notes:

- Requests should include:
  - `Accept-Profile: apijahialad`
  - `Accept: application/json`
- The API has a 20,000-row practical limit, so sync by month, station, and element.
- Always filter by date/year/month, station, and element. Do not query the measurement tables unfiltered.
- Store raw JSON plus typed columns, matching the Oura storage pattern.
- Use station metadata first to choose nearest valid station/element combinations for the configured coordinates.

Likely useful element codes based on public examples:

- `TA` - hourly air temperature.
- `RH` - hourly relative humidity.
- `PR1H` - hourly precipitation.
- `DTA08` - daily average air temperature.
- `DRH08` - daily average relative humidity.

Element metadata should be synced before hard-coding the final list, because element availability varies by station and period.

### 2. Open-Meteo Historical Weather API

Use this as the default fallback and model-based companion source.

Why:

- Free for non-commercial use, no API key for normal public use.
- Direct coordinate-based API, so it works for the exact configured location.
- Historical data from 1940 onward.
- Hourly and daily variables include temperature, humidity, dew point, apparent temperature, precipitation, rain, snowfall, pressure, cloud cover, wind, gusts, sunshine, radiation, soil temperature, and soil moisture.
- Several model choices exist:
  - `best_match`: highest practical convenience.
  - `era5` / `era5_land`: better for long-term consistency.
  - `cerra`: Europe-focused, 5 km, but only through June 2021.
  - `ecmwf_ifs`: 9 km, 2017-present, no delay.

Recommended use:

- For short and recent sleep correlation windows, collect `best_match`.
- For long-term statistical comparisons, also collect `era5_land` or `era5` as a stable model series.
- Use `timezone=Europe/Tallinn`.
- Request hourly values, then derive night-window and daily aggregates ourselves so they line up with Oura sleep sessions.

Suggested hourly variables:

- `temperature_2m`
- `relative_humidity_2m`
- `dew_point_2m`
- `apparent_temperature`
- `precipitation`
- `rain`
- `snowfall`
- `snow_depth`
- `pressure_msl`
- `surface_pressure`
- `cloud_cover`
- `wind_speed_10m`
- `wind_direction_10m`
- `wind_gusts_10m`
- `shortwave_radiation`
- `sunshine_duration`
- `soil_temperature_0_to_7cm`
- `soil_moisture_0_to_7cm`

### 3. Meteostat

Use only if we want a third comparison source or if Estonian station API coverage proves awkward.

Why:

- Historical hourly data by point location.
- Can fill gaps with statistically optimized model data.
- Aggregates from multiple governmental interfaces.

Tradeoffs:

- JSON API requires RapidAPI signup and a key.
- Free plan details can change.
- 30-day maximum per hourly request.
- Because we already have an official Estonian API plus Open-Meteo, this should not be first implementation work.

### 4. NOAA CDO

Not recommended for phase 1.

Why:

- It is free and historically strong, but it is station/catalog driven and less convenient for Estonia point weather than the Estonian official API.
- Better as a later archive fallback if we discover missing station years in the Estonian source.

## Data Model

Keep provider identity explicit. Different sources may disagree, and later correlation work should be able to choose or compare sources instead of mixing them silently.

Suggested tables:

### `WeatherLocations`

- `Id`
- `Name`
- `Latitude`
- `Longitude`
- `ElevationMeters`
- `Timezone`

### `WeatherStations`

- `Id`
- `Source`
- `StationCode`
- `Name`
- `Latitude`
- `Longitude`
- `ElevationMeters`
- `DistanceKm`
- `RawJson`

### `WeatherHourlySamples`

One row per source/location-or-station/hour.

- `Id`
- `LocationId`
- `StationId` nullable
- `Source`
- `Model` nullable
- `TimestampUtc`
- `TimestampLocal`
- `TemperatureC`
- `RelativeHumidityPct`
- `DewPointC`
- `ApparentTemperatureC`
- `PrecipitationMm`
- `RainMm`
- `SnowfallCm`
- `SnowDepthM`
- `PressureMslHpa`
- `SurfacePressureHpa`
- `CloudCoverPct`
- `WindSpeedMs`
- `WindDirectionDeg`
- `WindGustMs`
- `ShortwaveRadiationWm2`
- `SunshineDurationSec`
- `SoilTemperature0To7CmC`
- `SoilMoisture0To7Cm`
- `RawJson`

Unique index:

- `(LocationId, Source, Model, StationId, TimestampUtc)`

### `WeatherDailySummaries`

Daily values are useful, but should be derived from hourly samples where possible.

- `LocationId`
- `Source`
- `Model`
- `StationId`
- `Day`
- `TemperatureMeanC`
- `TemperatureMinC`
- `TemperatureMaxC`
- `HumidityMeanPct`
- `PrecipitationSumMm`
- `SnowfallSumCm`
- `WindSpeedMeanMs`
- `WindGustMaxMs`
- `CloudCoverMeanPct`
- `RawJson`

### Later derived table: `WeatherNightSummaries`

This belongs closer to phase 2/3, because it joins weather to Oura sleep windows.

- `UserId`
- `SleepSessionId`
- `Source`
- `TemperatureMeanC`
- `TemperatureMinC`
- `HumidityMeanPct`
- `PressureChangeHpa`
- `PrecipitationMm`
- `WindSpeedMeanMs`
- `CloudCoverMeanPct`

## Sync Design

Create `OuraDashboard.Weather` or extend `OuraDashboard.Sync` with weather-specific services. Keeping it separate is cleaner because weather is location-based, not user-token-based.

Suggested service shape:

- `WeatherSyncService.SyncAsync(days, cancellationToken)`
- `IWeatherProvider`
  - `Name`
  - `SyncHourlyAsync(location, start, end, cancellationToken)`
  - `SyncStationsAsync(location, cancellationToken)` where supported
- `EstonianEnvironmentAgencyWeatherProvider`
- `OpenMeteoWeatherProvider`
- optional `MeteostatWeatherProvider`

Scheduling:

- Run weather sync daily, not hourly.
- On each run, re-fetch the last 7-14 days because historical weather datasets can be corrected after the first publication.
- Full backfill via CLI, similar to Oura `--days N`.

Storage policy:

- Upsert everything.
- Store raw JSON for every provider response.
- Keep typed scalar columns provider-neutral.
- Never overwrite one provider's values with another provider's values.

## Phase Plan

### Phase 1: Collect

1. Add weather config.
2. Add weather EF entities and migration.
3. Implement Open-Meteo provider first because it is coordinate-based and easiest to validate.
4. Implement Estonian Environment Agency station metadata import.
5. Pick nearest station(s) with required elements for the configured location.
6. Implement Estonian hourly import by month/station/element.
7. Add weather sync to CLI with `--weather --days N` and `--weather-stations`.
8. Add basic sync logging/counts, but no dashboard UI yet.

### Phase 2: Show

1. Add a simple weather diagnostics page.
2. Show source coverage by day/hour.
3. Show station distance and selected elements.
4. Add overlay-ready query methods, but do not build correlation logic yet.

### Phase 3: Correlate

1. Build night-window weather summaries aligned to Oura sleep session start/end.
2. Compare multiple sources side by side.
3. Add lagged variables: same day, previous day, previous 3-day rolling window.
4. Add correlation/search tools only after enough data coverage is visible.

## Recommendation

Start with two free sources:

1. Open-Meteo `best_match` and optionally `era5_land` for coordinate-based complete coverage.
2. Estonian Environment Agency `f_kliima_*` tables for official station observations.

This gives both completeness and local observational grounding. Meteostat can wait until we see actual gaps.

## References

- Open-Meteo Historical Weather API: https://open-meteo.com/en/docs/historical-weather-api
- Open-Meteo Historical Forecast vs Historical Weather explanation: https://open-meteo.com/en/docs/historical-forecast-api
- Estonian Environmental Portal open data catalogue: https://keskkonnaportaal.ee/en/node/9566
- Estonian meteorological monitoring dataset: https://keskkonnaportaal.ee/avaandmed/meteoroloogilise-seire-andmestik
- Estonian environment/weather API services: https://keskkonnaportaal.ee/avaandmed/keskkonna-ja-ilma-valdkonna-andmeteenused
- Meteostat JSON API: https://dev.meteostat.net/api
- Meteostat hourly point API: https://dev.meteostat.net/api/point/hourly
- NOAA Climate Data Online: https://www.ncei.noaa.gov/cdo-web/
