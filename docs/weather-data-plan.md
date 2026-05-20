
# Weather Data Plan

## Goal

Add historical weather data as a separate input stream for later comparison with Oura sleep and recovery metrics.

Phase 1 is collection only. The UI, correlations, and statistical analysis come later.

## Deployment Follow-up

The current production deployment is Docker-based under `docker/oura-dashboard/`.

- Use a mounted `/srv/oura-dashboard/appsettings.json` as `/app/appsettings.json` for app configuration, including Oura tokens, weather settings, sync intervals, timezone, and the Postgres connection string.
- Keep compose environment overrides minimal; `.env` should only provide infrastructure values such as `DB_PASSWORD` for the Postgres container.
- Persist ASP.NET Core Data Protection keys with `/srv/oura-dashboard/data-protection-keys:/home/app/.aspnet/DataProtection-Keys` to avoid antiforgery token failures after container replacement.
- Re-check recent production logs after the compose change. Known recent entries were one `Antiforgery.DefaultAntiforgery` token deserialization error, one `Hosting.Diagnostics` warning about `HTTP_PORTS=8080` being overridden by `URLS=http://+:8085`, one unencrypted Data Protection key warning, and one non-persistent Data Protection key storage warning.

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
- `PressureChangeLevel` (`acceptable`, `medium`, `high`, nullable when coverage is weak)
- `PrecipitationMm`
- `WindSpeedMeanMs`
- `CloudCoverMeanPct`
- `CoveragePct`

### Later derived table: `WeatherDaySummaries`

Daily values should be derived from local-time hourly samples where possible. This table supports daytime weather context without tying the value to a specific sleep session.

- `LocationId`
- `Source`
- `Model`
- `StationId`
- `Day`
- `PressureChangeHpa`
- `PressureChangeLevel` (`acceptable`, `medium`, `high`, nullable when coverage is weak)
- `SunnyHours`
- `SunnyHoursLevel` (`enough`, `middle`, `low`, nullable when coverage is weak)
- `DaylightHoursSampled`
- `CoveragePct`

## Weather UI Updates

The first weather UI should be descriptive context inside the Oura views, not a separate weather product. It should answer simple questions beside the sleep/recovery data:

- Was pressure stable enough overnight, or did it change enough to plausibly matter?
- How was the day before that sleep: enough sun, middle sun, or low sun?
- If day coverage is good enough, did daytime pressure also change acceptably, moderately, or highly?

Weather-specific trend diagrams are still useful later, but the first implementation should make weather visible as an annotation layer for Oura nights.

### Pressure Change Classification

Use hourly pressure data, preferring `pressure_msl` for consistency across elevation and sources. Fall back to `surface_pressure` only if sea-level pressure is unavailable for that source.

For a window, calculate:

- `pressureChangeHpa = max(pressure) - min(pressure)`
- `coveragePct = valid hourly pressure samples / expected hourly samples`
- `sourceCount` where values are available, because Open-Meteo and station observations should remain visibly separate until we intentionally choose a primary display source.

Initial thresholds:

| Level | Pressure change in window |
|---|---:|
| Acceptable | `< 4 hPa` |
| Medium | `4-8 hPa` |
| High | `> 8 hPa` |

Coverage rules:

- Night pressure classification needs at least 70% of expected hourly samples inside the actual Oura sleep-session window.
- Day pressure classification needs at least 70% of expected local daytime samples.
- If coverage is below threshold, show `insufficient data` rather than forcing a level.
- If multiple sources disagree by more than one level, show the primary source level plus a small disagreement marker in diagnostics.

UI placement:

- Add a compact weather context strip to `/night/{name}/{day}` below the RRS/verdict area:
  - `Night pressure`: `3.2 hPa acceptable`, `6.1 hPa medium`, `10.4 hPa high`, or `insufficient data`.
  - `Previous day pressure`: same classification for the local daytime window before that sleep.
  - `Source`: primary source/model, with a diagnostics link when sources disagree.
- Add the same compact weather context to the last-night cards on `/`, but keep it visually secondary to RRS/HRV/HR/respiration.
- Add a small day-weather row to `/user/{name}` history rows only after a shared daily query exists. The value is location/day based, so do not duplicate complex source details in each user card.
- Add the full source-by-source pressure values first to a weather diagnostics section so threshold tuning is easy.

### Daytime Sunny Hours Classification

Use Open-Meteo `sunshine_duration` as the primary source because the current Estonian station import does not collect sunshine elements. Sum hourly `sunshine_duration` over the local daytime window and divide by 3600.

Initial daytime window:

- Prefer astronomical daylight if we later store sunrise/sunset or request daily sunrise/sunset values.
- Until then, use local `08:00-20:00` as a pragmatic daytime window for UI classification.

Initial thresholds:

| Level | Sunny hours in daytime window |
|---|---:|
| Enough | `>= 5 hours` |
| Middle | `2-5 hours` |
| Low | `< 2 hours` |

Coverage rules:

- Classify sunny hours only when at least 70% of the daytime hourly samples have `sunshine_duration`.
- If `sunshine_duration` is missing but `shortwave_radiation` exists, show a diagnostics-only fallback candidate, not a main UI level yet. Radiation needs seasonal calibration before it is a user-facing sunny-hours label.

UI placement:

- Add `Previous day sun` to the `/night/{name}/{day}` weather context strip: `6.3h enough`, `3.1h middle`, `0.8h low`, or `insufficient data`.
- Add `Sun` to the compact weather context on `/` last-night cards.
- Add a simple 7/14/30-day mini trend later once enough daily rows exist: sunny-hours level, pressure-change level, and Oura recovery/sleep score columns side by side.

### Night Summary Context

For Oura night pages, define two weather windows:

| Window | Definition | Main labels |
|---|---|---|
| Night | actual Oura sleep session start/end in local time | `Night pressure` |
| Previous day | local daytime before the sleep start, initially `08:00-20:00` on the sleep-start date | `Previous day sun`, `Previous day pressure` |

This avoids confusing the Oura `Day` with the calendar date after waking. If a sleep session starts after midnight, use the daytime window immediately before sleep, not the next calendar day.

The summary should be one small row/strip, not a large weather card:

| Item | Example display | Why |
|---|---|---|
| Night pressure | `3.2 hPa acceptable` | direct sleep-window context |
| Previous day sun | `6.3h enough` | daytime exposure context |
| Previous day pressure | `5.7 hPa medium` | day stressor/context before sleep |

Recommended visual treatment:

- Use small Bootstrap badges or compact text chips in the existing night header area.
- Keep labels short and numeric: users should see the level and the actual value.
- Use muted styling when weather is normal, amber for medium, red for high/low-problem cases.
- Do not let weather change the RRS color or summary text automatically until we have enough data to trust correlations.

### Trend Chart Pictograms

For trend graphs with dates, add weather pictograms under the chart as a lightweight annotation lane. This is easier than adding more Y-axes and keeps weather visibly attached to the Oura measurements.

Initial implementation:

- Render a narrow row below each dated ApexChart using the same date list as the chart series.
- Each day gets a fixed-width cell aligned to the chart's date buckets.
- Use simple symbols plus `title` tooltips first; replace with icons later only if needed.

Suggested pictogram set:

| Signal | Good/low impact | Middle | High/low concern | Missing |
|---|---|---|---|---|
| Night pressure | `P.` acceptable | `P~` medium | `P!` high | `P?` |
| Previous day pressure | `D.` acceptable | `D~` medium | `D!` high | `D?` |
| Previous day sun | `S+` enough | `S~` middle | `S-` low | `S?` |

If Bootstrap Icons are already added later, these can become icon+color markers:

- pressure acceptable: gauge/check style marker.
- pressure medium: gauge/warning style marker.
- pressure high: gauge/exclamation style marker.
- sun enough/middle/low: sun, sun/cloud, cloud.

Recommended chart placement:

- `/`: add one shared weather annotation lane under the HRV chart and the HR/resp chart. Because weather is location-level, show one lane shared by Boo and Maa instead of duplicating it per user.
- `/user/{name}`: add the annotation lane under 7/14/30/90-day trend charts.
- `/compare`: add one shared annotation lane under comparison charts so shared bad nights can be visually checked against shared weather context.
- `/night/{name}/{day}`: no pictogram lane needed; use the numeric weather context strip instead.

Implementation detail:

- Query a `Dictionary<DateOnly, WeatherDayContext>` for the date range already used by the chart.
- Use CSS grid with `grid-template-columns: repeat(n, minmax(0, 1fr))`.
- Keep each marker stable-width and tooltip-rich: `title="Night pressure 6.1 hPa, medium; previous day sun 3.1h, middle"`.
- Limit visible markers to 30-60 days. For 90-day views, show denser dots/letters or only mark non-normal days.
- Do not add weather values to chart axes unless the chart is explicitly a weather chart.

### Separate Weather Trend Diagrams

Add separate weather trend diagrams when the user is intentionally inspecting weather stats rather than reading Oura measurements.

Good first weather-only charts:

- Daily sunny hours line/bar chart with enough/middle/low threshold bands.
- Night pressure change bar chart with acceptable/medium/high colors.
- Previous day pressure change bar chart.
- Optional source comparison chart for Open-Meteo vs Estonian station pressure when both exist.

Placement options:

- Start with a collapsible `Weather details` section on `/user/{name}` or `/compare`.
- Later promote to `/weather` only if it grows beyond contextual support.

These charts should not replace the pictogram lanes. The lanes answer "could weather be part of this Oura night?", while weather diagrams answer "what has the weather been doing over time?"

### Query/API Work Needed

1. Add query methods in `DashboardQueryService` for weather windows:
   - sleep-session weather summary by user/day/source.
   - local-day weather summary by day/source.
   - date-range weather context for trend pictogram lanes.
2. Keep classification logic in a pure helper, for example `WeatherClassifiers`, with unit tests for threshold edges and low coverage.
3. Return both raw numeric values and display levels so the UI can show `3.2 hPa acceptable` instead of only a badge.
4. Add a small `WeatherDayContext`/`WeatherNightContext` view model for UI display.
5. Add diagnostics output for expected sample count, actual sample count, and missing variables.
6. Only persist derived tables if query-time calculation becomes slow or if correlation tooling needs indexed historical summaries.

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

Status: implemented as contextual Oura UI plus `/sync` diagnostics. Weather-only trend diagrams remain Phase 3/future work.

1. ✅ Add a simple weather diagnostics section on `/sync`.
2. ✅ Show source/variable coverage by recent Oura weather windows.
3. ✅ Show selected source/model/station context in diagnostics and chip tooltips.
4. ✅ Add `WeatherClassifiers` and focused tests for pressure/sun thresholds and low coverage.
5. ✅ Add `/night/{name}/{day}` weather context strip:
   - night pressure change.
   - previous-day sunny hours.
   - previous-day pressure change when coverage allows.
6. ✅ Add compact weather context to `/` last-night cards.
7. ✅ Add overlay-ready query methods and weather pictogram lanes under dated Oura trend charts, but do not build correlation logic yet.

### Phase 3: Correlate

1. Build night-window weather summaries aligned to Oura sleep session start/end.
2. Compare multiple sources side by side.
3. Add lagged variables: same day, previous day, previous 3-day rolling window.
4. Add separate weather trend diagrams for sunny hours and pressure changes.
5. Add correlation/search tools only after enough data coverage is visible.

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
