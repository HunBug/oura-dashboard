# LLM Session Context — oura-dashboard

> Paste this at the start of a new AI session to give the model full context.
> Last updated: 2026-05

---

## What this project is

**oura-dashboard** — private home-server health dashboard for two Oura Ring users: **Boo** and **Maa**.

- Pulls raw data from the Oura Cloud API into **PostgreSQL 17**
- **Blazor Server** (.NET 10) dashboard with custom sleep/recovery metrics
- Core philosophy: **distributions and trends, not point-in-time scores** (Oura's own scores are poor signals for this use case)

## Tech stack

| | |
|---|---|
| Runtime | .NET 10, C# 13 |
| UI | Blazor Server (`@rendermode InteractiveServer`) |
| Charts | Blazor-ApexCharts 6.1.0 |
| ORM | EF Core 9 + Npgsql |
| Database | PostgreSQL 17 via Docker (port **5433**) |
| CSS | Bootstrap 5 |
| Tests | xUnit (37 tests) |

## Solution layout

```
src/
  OuraDashboard.Data/       # EF Core entities, DbContext, migrations
  OuraDashboard.Sync/       # OuraApiClient + OuraSyncService (upsert logic)
  OuraDashboard.Sync.Cli/   # Console app: --days N, --migrate
  OuraDashboard.Web/        # Blazor Server app
    Components/Pages/       # .razor pages
    Services/
      DashboardQueryService.cs   # all DB reads (Scoped)
      NightMetrics.cs            # pure static custom metric calculator
tests/
  OuraDashboard.Tests/      # NightMetricsCalculator xUnit tests
docs/
  architecture.md           # full design reference
```

## Current page inventory (all implemented)

| Route | Component | Description |
|---|---|---|
| `/` | `Home.razor` | Morning briefing: RRS strip (last night, both users) + compact weather context + dual-axis HRV + combo HR/Resp charts + weather lanes + pattern callouts |
| `/user/{name}` | `UserDetail.razor` | History per person, weather lanes, heatmap table with weather markers, 7/14/30/90-day toggle |
| `/night/{name}/{day}` | `NightDetail.razor` | Single-night deep dive: verdict bar (RRS), weather context strip, intra-night HR + HRV charts, 3 collapsible metric sections |
| `/compare` | `Compare.razor` | Boo vs Maa overlays: dual-axis HRV, clustered HR>75%, weather lanes, correlation badge |
| `/sync` | `Sync.razor` | Sync status (2s poll), per-user counts, manual refresh/reload buttons, weather DB totals + context diagnostics |
| `/raw` | `Raw.razor` | Raw JSON export by user + date + endpoint |
| `/metrics` | `MetricsGuide.razor` | Explanations of all custom metrics (not in main nav) |

## Database tables

| Table | Key columns |
|---|---|
| `Users` | `Id`, `Name`, `OuraToken` |
| `DailySleep` | `UserId`, `Day`, `Score`, `DeepMinutes`, `RemMinutes`, `AwakeMinutes`, `Efficiency`, `RawJson` |
| `SleepSessions` | `UserId`, `Day`, `SessionStart`, `SessionEnd`, `AvgHr`, `AvgHrv`, `AvgBreath`, `HrvTimeSeries` (JSONB), `SleepStages` (JSONB), `RawJson` |
| `DailyReadinesses` | `UserId`, `Day`, `Score`, `TemperatureDeviation`, `TempTrendDeviation`, `RawJson` |
| `HeartRateSamples` | `UserId`, `Timestamp`, `Bpm`, `Source` — minute-by-minute |
| `DailyStresses` | `UserId`, `Day`, `StressHigh`, `RecoveryHigh`, `RawJson` |
| `DailyActivities` | `UserId`, `Day`, `Steps`, `ActiveCalories`, `RawJson` |
| `Vo2Maxes` | `UserId`, `Day`, `Vo2Max`, `RawJson` |
| `DailySpo2s` | `UserId`, `Day`, `BreathingDisturbanceIndex`, `Spo2Average`, `RawJson` |
| `DailyResilienceRecords` | `UserId`, `Day`, `Level` (string), `SleepRecovery`, `DaytimeRecovery`, `Stress`, `RawJson` |
| `Workouts` | `UserId`, `Day`, `Activity`, `Calories`, `Distance`, `Intensity`, `RawJson` |
| `DailyHrvs` | **Dead table** — `daily_hrv` endpoint does not exist in the Oura API |
| `WeatherLocations` | configured weather point, currently Roela at `59.14496602915124, 26.569136382508024` |
| `WeatherStations` | official/source station metadata by source + station + element |
| `WeatherHourlySamples` | hourly weather values by location + source + model/station + UTC timestamp, with raw JSON |

## Key service types

**`DashboardQueryService`** (Scoped) — all DB reads.
- `GetUserOverviewAsync(userName, days)` → `UserOverview` with `List<DailyOverviewRow>`
- `GetNightDetailAsync(userName, day)` → `NightData?`
- `GetNightDaysAsync(userName, days)` → `List<DateOnly>` (for prev/next nav)
- `GetNightWeatherContextAsync(userName, day)` → `WeatherNightContext?`
- `GetWeatherDayContextsAsync(userName, start, end)` → weather annotation lane data
- `GetRecentWeatherContextDebugAsync(...)` → `/sync` weather diagnostics
- `GetRawExportAsync(...)` → raw JSON strings from RawJson columns

**`NightMetrics`** — pure static, computed on-the-fly from intra-night timeseries.
No DB queries, no DI. Fields:
- `RealRecoveryScore` (0–100): HR<75bpm×35% + HRV×25% + Restorative sleep×25% + Resp rate×15%
- `HrAbove75Pct`, `HrAbove80Pct`, `HrSettlingMinutes`
- `HrvDirection` ("improving"/"declining"/"flat", ±2ms dead-band)
- `HrvEarlyHalfAvg`, `HrvLateHalfAvg`, `HrvPeak`, HRV distribution buckets
- `RestorativeMinutes`

**`SyncBackgroundService`** (hosted in Web) — timer + Channel trigger. Handles Oura sync plus weather sync. Defaults: Oura auto-sync enabled every 360 minutes, weather auto-sync enabled every 24 hours. Both can be disabled with `Oura:AutoSyncEnabled` / `Weather:AutoSyncEnabled`; `/sync` manual buttons still work.

**`WeatherSyncService`** — location-based historical weather collection.
- Open-Meteo archive API, no token, default model `best_match`.
- Estonian Environment Agency open data, station metadata plus hourly climate observations.
- Normal syncs skip already-collected hours per source/model/station.
- Historical weather reload refreshes the full requested lookback window so it can backfill older missing rows.
- CLI: `--weather --days N` for weather only, `--all --days N` for Oura + weather.

**Weather UI rules** — context only, not correlation.
- Pressure uses `pressure_msl` first, then `surface_pressure`; level is acceptable `<4 hPa`, medium `4-8 hPa`, high `>8 hPa`.
- Sun uses Open-Meteo `sunshine_duration`; level is enough `>=5h`, middle `2-5h`, low `<2h`.
- Both require at least 70% hourly coverage; otherwise show `insufficient data`.
- `/night` and home show numeric chips; home/user/compare charts show `P/D/S` annotation lanes.

## Non-obvious rules & gotchas

1. **Central Package Management** — versions in `Directory.Packages.props` only. Never add `Version=` in `.csproj`.
2. **ApexCharts shared-options bug** — each chart must have its own `ApexChartOptions<T>` instance. Sharing one instance causes all charts after the first to render blank.
3. **Timezones** — Oura timestamps carry local UTC offsets. Always `.ToUniversalTime()` before saving to EF entities (Npgsql requires UTC `timestamptz`).
4. **`daily_hrv` doesn't exist** — the endpoint is invalid; HRV lives in sleep sessions.
5. **`vO2_max` path** — capital O required. `vo2_max` returns 404.
6. **`heartrate` endpoint** — returns 400 for ranges >30 days. Client fetches in 30-day chunks.
7. **Upsert safety** — all sync operations upsert on `(UserId, Day)`. Re-running is always safe.
8. **`appsettings.Local.json`** is gitignored — real tokens go there.

## Common commands

```bash
# Start Postgres
docker compose up -d postgres

# Apply migrations (CLI reads real connection string; dotnet ef doesn't)
dotnet run --project src/OuraDashboard.Sync.Cli -- --migrate

# Bulk import historical data
dotnet run --project src/OuraDashboard.Sync.Cli -- --days 90

# Run the web app → http://localhost:5195
dotnet run --project src/OuraDashboard.Web

# Run tests
dotnet test tests/OuraDashboard.Tests

# Build everything
dotnet build
```

## What's left to build

- **Deployment artifacts**: `docker-compose.full.yml` (web + postgres containers) + systemd unit file
- **Trend layer on UserDetail**: 7-day rolling averages for HRV and HR; autonomic state trend line

## Files to read for deeper context

- `docs/architecture.md` — full schema, service descriptions, API endpoint list, deployment options
- `src/OuraDashboard.Web/Services/NightMetrics.cs` — custom metric logic
- `src/OuraDashboard.Web/Services/DashboardQueryService.cs` — all DB query shapes
- `src/OuraDashboard.Web/Components/Pages/Home.razor` — most complex page
- `tests/OuraDashboard.Tests/NightMetricsCalculatorTests.cs` — reference for metric behaviour
