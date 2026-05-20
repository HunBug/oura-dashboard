# Architecture

## Overview

```
Oura Cloud API
      │
      ├──── triggered by ────┐
      │                      │
      ▼                      ▼
┌──────────────────────────────────────┐       ┌──────────────────┐
│          OuraDashboard.Web           │──────▶│   PostgreSQL 17   │
│  Blazor UI  +  SyncBackgroundService │◀──────│  (Docker, :5433) │
└──────────────────────────────────────┘       └──────────────────┘
  - hourly timer (configurable)                        ▲
  - "Refresh" button → Channel<T>                      │
                                               ┌───────┴──────────┐
                                               │  Sync.Cli (opt.) │
                                               │  bulk/cron import│
                                               └──────────────────┘
```

- **Sync logic** lives in `OuraDashboard.Sync` (class library) — shared by both the web app and the CLI.
- **Web** hosts a `SyncBackgroundService` (`IHostedService`) with two trigger paths:
  - A configurable periodic timer (default: hourly)
  - A `Channel<SyncRequest>` written to by the UI's "Refresh" button
- **Sync.Cli** is a thin console app referencing the same library — useful for first-time bulk imports (`--days 365`) or OS-level cron if preferred.
- No inter-process communication, no separate Docker container required for sync; the web app handles it.
- The web app will show last-sync time, in-progress state, and per-user sync results.

---

## Solution structure

```
OuraDashboard.sln
├── src/
│   ├── OuraDashboard.Data/       # shared: EF Core entities, DbContext, migrations
│   ├── OuraDashboard.Sync/       # class library: Oura API client, sync service
│   ├── OuraDashboard.Sync.Cli/   # thin console app: bulk/historical imports
│   └── OuraDashboard.Web/        # Blazor Server: dashboard, charts, export + SyncBackgroundService
├── docs/
│   └── architecture.md
├── docker-compose.yml            # postgres only (standalone dev/prod)
├── docker-compose.full.yml       # postgres + web container (optional)
└── appsettings.example.json
```

## Weather data

Historical weather collection is implemented as a location-based sync path alongside Oura sync.

### Configuration

Weather is configured separately from Oura users:

```json
"Weather": {
  "Enabled": true,
  "LocationName": "Roela",
  "Latitude": 59.14496602915124,
  "Longitude": 26.569136382508024,
  "Timezone": "Europe/Tallinn",
  "AutoSyncEnabled": true,
  "SyncIntervalHours": 24,
  "SyncLookbackDays": 14,
  "FullSyncLookbackDays": 3650
}
```

Normal syncs only request hours missing after the latest stored sample per source/model/station. Historical reload refreshes the full configured lookback window, so it can backfill older missing Open-Meteo rows even after recent scheduled syncs have run.

### Sources

| Source | Status | Notes |
|---|---|---|
| Open-Meteo historical archive | Implemented | Coordinate-based hourly data, no token. Default model is `best_match`. |
| Estonian Environment Agency open data | Implemented | Official station observations from `keskkonnaandmed.envir.ee`, using station metadata plus hourly climate table. |
| Meteostat | Planned only | Keep as a later optional fallback if gaps remain. |

### Tables

| Table | Key columns |
|---|---|
| `WeatherLocations` | configured name, lat/lon, timezone, raw config JSON |
| `WeatherStations` | source, station code, element code, distance from configured location, raw station JSON |
| `WeatherHourlySamples` | source/model/station/timestamp plus typed weather scalar columns and raw JSON |

Weather data intentionally keeps provider identity explicit. Open-Meteo model data and official station observations are not merged into a single value.

---

## LLM Integration

The first LLM feature is implemented against the private Ollama sandbox at `http://neolinux:11434`, verified with `GET /api/tags` on 2026-05-20.

The dashboard calls Ollama from server-side .NET services only. Browser/UI code does not know the Ollama host, model names, prompt templates, or raw request format.

Configuration:

```json
"Llm": {
  "Enabled": true,
  "SandboxMode": false,
  "BaseUrl": "http://neolinux:11434",
  "Model": "gemma4:e4b",
  "Think": false,
  "TimeoutSeconds": 90,
  "ConnectTimeoutSeconds": 5,
  "MaxConcurrentRequests": 1,
  "NumPredict": 1000
}
```

Key flags:

- `SandboxMode: true` — calls Ollama but writes no `LlmInteraction` rows. Use for prompt experimentation.
- `Think: false` — disables the extended-thinking phase for models that support it (e.g. gemma4). **Required for small GPUs**: with thinking enabled, the model consumes all token budget on internal reasoning and returns empty `content`. Omit the key to let Ollama use the model default.
- `NumPredict` — caps output tokens. 1000 is enough for concise health notes without thinking. If `Think: true`, set 3000+ and raise `TimeoutSeconds` accordingly (thinking alone can consume 700+ tokens before any content is written).

Implemented service boundary:

| Component | Responsibility |
|---|---|
| `LlmOptions` | Bind endpoint, model, timeout, enabled, sandbox, think, and generation flags. |
| `ILlmClient` | Internal abstraction for chat execution. |
| `OllamaLlmClient` | Ollama-specific HTTP client for `/api/chat` with `stream: false`. |
| `LlmConcurrencyLimiter` | Limits local model load with `Llm:MaxConcurrentRequests`. |
| `LlmRequestStore` | Stores and updates production interaction rows. |
| `LlmDebugLog` | Singleton ring buffer (last 3 calls) — full prompts and raw JSON, in memory only. |
| `NightLlmService` | Builds compact night-summary input from typed Oura/weather data. Skips DB writes when `SandboxMode` is on. |

Implemented persistence:

| Table | Purpose |
|---|---|
| `LlmInteractions` | Production request/response/error history, including input JSON, messages JSON, raw Ollama payloads, status, latency, model, provider, and prompt key/version. |
| `LlmPrompts` | Versioned prompt storage reserved for future DB-backed prompt overrides. |

Current UI:

- `/night/{name}/{day}` has an "LLM note" panel. Shows a **sandbox** badge when `SandboxMode` is on.
- `Generate` creates or reuses a recent matching interaction (skipped in sandbox mode).
- `Regenerate` forces a new interaction row (skipped in sandbox mode).
- Disabled/offline/timeout/error states are shown without breaking the page.
- `/debug/llm-sandbox` shows the last 3 LLM calls from `LlmDebugLog` with full prompt text and raw Ollama JSON.

Actual implementation notes live in `docs/ollama-implementation-plan.md`. The
original model inventory and broader backlog remain in `docs/ollama-llm-plan.md`.

---

## Data layer (`OuraDashboard.Data`)

### Storage strategy

Every API response is stored **twice**:
1. **Raw JSONB column** — the exact API payload, nothing discarded.
2. **Typed scalar columns** — extracted key metrics for fast queries (no JSON parsing at query time).

This means the schema can be extended later without re-fetching from Oura.

### Core entities

| Table | Key columns |
|---|---|
| `Users` | `Id`, `Name`, `OuraToken` (encrypted at rest) |
| `DailySleep` | `UserId`, `Day`, `Score`, `DeepMinutes`, `RemMinutes`, `AwakeMinutes`, `Efficiency`, `RawJson` |
| `SleepSession` | `UserId`, `Day`, `SessionStart`, `SessionEnd`, `AvgHr`, `AvgHrv`, `AvgBreath`, `HrTimeSeries` (JSONB), `HrvTimeSeries` (JSONB), `SleepStages` (JSONB), `RawJson` |
| `DailyReadiness` | `UserId`, `Day`, `Score`, `RhrContributor`, `HrvBalanceContributor`, `TemperatureDeviation`, `RawJson` |
| `HeartRateSample` | `UserId`, `Timestamp`, `Bpm`, `Source` — minute-by-minute, high volume |
| `DailyStress` | `UserId`, `Day`, `StressHigh`, `RecoveryHigh`, `DaytimeStressScore`, `RawJson` |
| `DailyActivity` | `UserId`, `Day`, `Steps`, `ActiveCalories`, `RawJson` |
| `Vo2Max` | `UserId`, `Day`, `Vo2Max`, `RawJson` |
| `DailySpo2` | `UserId`, `Day`, `BreathingDisturbanceIndex`, `Spo2Average`, `RawJson` |
| `DailyResilience` | `UserId`, `Day`, `Level` (string: limited/adequate/solid/strong/exceptional), `SleepRecovery`, `DaytimeRecovery`, `Stress`, `RawJson` |
| `Workouts` | `UserId`, `Day`, `Activity`, `Calories`, `Distance`, `Intensity`, `Source`, `StartDatetime`, `EndDatetime`, `RawJson` |
| `DailyHrvs` | Dead table — `daily_hrv` endpoint does not exist in the Oura API. Never written to; retained for migration history. |

`HeartRateSample` will be large (~1440 rows/user/day). Consider a partial index on `(UserId, Timestamp)` and a retention policy if disk matters.

### Migrations

EF Core code-first. Run from repo root:
```bash
dotnet ef database update --project src/OuraDashboard.Data --startup-project src/OuraDashboard.Web
```

---

## Sync library (`OuraDashboard.Sync`)

A .NET 10 class library containing all Oura API + write logic. Referenced by both the web app and the CLI.

### Core class: `OuraSyncService`

- `SyncAsync(string userId, int days, CancellationToken)` — fetches all endpoints for one user.
- **Upsert** on `(UserId, Day)` — safe to re-run, updates existing rows.
- Stores raw JSON blob + extracts scalars in the same transaction.
- Returns a `SyncResult` (counts, errors) that the caller can log or display.

### `SyncBackgroundService` (hosted in `OuraDashboard.Web`)

- Implements `IHostedService` / `BackgroundService`.
- Reads from a `Channel<SyncRequest>` (manual trigger) with a periodic timer fallback.
- Exposes sync state (`IsRunning`, `LastSyncAt`, `LastResult`) as a singleton for the UI to read.
- Triggered from Blazor UI via injected `ISyncTrigger.RequestSync()`.

## Sync CLI (`OuraDashboard.Sync.Cli`)

Thin console app — just parses args and calls `OuraSyncService`.

- `--days N` controls how far back to go (default 30).
- Exits with non-zero on partial failure (good for cron/systemd alerting).
- Useful for: first-time historical import, scripted backfills, OS-level cron jobs.

### Oura API endpoints fetched

| Endpoint | Notes |
|---|---|
| `GET /v2/usercollection/daily_sleep` | Daily sleep score + contributors |
| `GET /v2/usercollection/sleep` | Per-session detail: HR, HRV, stages, intra-night timeseries |
| `GET /v2/usercollection/daily_readiness` | Readiness score + contributors |
| `GET /v2/usercollection/heartrate` | Minute-by-minute HR (datetime params) |
| `GET /v2/usercollection/daily_stress` | Daytime stress + recovery |
| `GET /v2/usercollection/daily_activity` | Steps, calories, activity intensity |
| `GET /v2/usercollection/vO2_max` | VO2 max estimate (note capital O in path) |
| `GET /v2/usercollection/daily_spo2` | Blood oxygen saturation + breathing disturbance index |
| `GET /v2/usercollection/daily_resilience` | Recovery resilience level + components |
| `GET /v2/usercollection/workout` | Workout sessions: activity type, calories, distance, intensity |

### Scheduling options

**Built-in (default, recommended):** `SyncBackgroundService` inside the web app runs on configurable intervals (`Oura:SyncIntervalMinutes`, default 360; `Weather:SyncIntervalHours`, default 24). Scheduled runs can be disabled with `Oura:AutoSyncEnabled=false` or `Weather:AutoSyncEnabled=false`. The refresh/reload buttons on `/sync` send triggers through the `Channel` for immediate manual runs. Nothing extra to deploy.

**Sync.Cli via cron (alternative/additional):** useful for large historical backfills or if you want OS-level scheduling independent of the web process.
```
# /etc/cron.d/oura-sync
0 * * * * akoss cd /opt/oura-dashboard && dotnet OuraDashboard.Sync.Cli.dll --days 2
```

**Manual CLI:** `dotnet run --project src/OuraDashboard.Sync.Cli -- --days 90`

---

## Web app (`OuraDashboard.Web`)

Blazor Server (.NET 10). Uses `@rendermode InteractiveServer` throughout.
Charts are rendered with **Blazor-ApexCharts 6.1.0** (C#-native, no manual JS interop).

### Pages — current state ✅

| Route | Component | Status |
|---|---|---|
| `/` | `Home.razor` | ✅ **Redesigned (Steps 5–7 + weather)**: Two-column last-night strip (RRS badge with 14-day personal baseline color, HRV, HR>75%, Restorative, Temp, compact weather context), dual-axis HRV 30-day chart, 4-line combo chart (HR>75% + Resp, both users), shared weather annotation lanes, pattern callouts |
| `/user/{name}` | `UserDetail.razor` | ✅ **Redesigned (Step 3 + weather)**: 7-stat summary, 2 charts (HRV+Resp dual-axis; HR>75% bar + Restorative line), weather annotation lanes, heatmap table with weather markers, 7/14/30/90 day toggle, Oura scores toggle |
| `/night/{name}/{day}` | `NightDetail.razor` | ✅ **Redesigned (Step 2 + weather)**: Verdict bar (RRS color + `GenerateSummary`), compact weather context strip, charts zone, 3 collapsible metric sections, Oura scores (collapsed), daytime (collapsed), raw data (collapsed), breadcrumb, prev/next nav |
| `/compare` | `Compare.razor` | ✅ **Redesigned (Step 4 + weather)**: Dual Y-axis HRV, clustered bar HR>75%, weather annotation lanes, resp rate + temp charts, zone-alignment correlation badge, heatmap table, 30/60/90 day toggle |
| `/sync` | `Sync.razor` | ✅ Live sync state (2-second poll), per-user result counts, refresh/reload buttons, DB totals for Oura/weather with pressure/sun counts, weather context diagnostics |
| `/metrics` | `MetricsGuide.razor` | ⚠️ Removed from nav (Step 1). Content dissolved into `MetricHelp.razor` `?` popovers (Step 7). Page still exists at `/metrics` as a reference; not linked from primary nav. |
| `/debug/investigate` | `DebugInvestigate.razor` | ✅ Warning/Error log viewer (live from `AppLogSink`, clearable) + raw DB row inspector (per user/day, grouped by endpoint and source) |
| `/debug/llm-sandbox` | `LlmSandbox.razor` | ✅ Last 3 LLM calls from `LlmDebugLog`: response text, prompts sent, raw Ollama request/response JSON. Works in both sandbox and live mode. |



### Services

**`DashboardQueryService`** (Scoped) — all DB reads for the dashboard.
- `GetUserOverviewAsync(userName, days)` → `UserOverview` with one `DailyOverviewRow` per calendar day. Start date clamped to user’s earliest session so charts don’t open with empty left-side gaps.
- `GetNightDetailAsync(userName, day)` → `NightData?` — scalars + intra-night `HrvSeries`/`HeartRateSeries` + sleep stage string + contributors from all 6 tables.
- `GetNightDaysAsync(userName, days)` → `List<DateOnly>` (descending) — days that have a `long_sleep` session, used for prev/next navigation on the night detail page.
- `GetNightWeatherContextAsync(userName, day)` → `WeatherNightContext?` — sleep-window pressure change plus previous-day sun and pressure context.
- `GetWeatherDayContextsAsync(userName, start, end)` → `Dictionary<DateOnly, WeatherDayContext>` — chart annotation lane data.
- `GetRecentWeatherContextDebugAsync(...)` → recent-night weather diagnostics for `/sync`.
- Joins: `DailySleep` (score + contributors), `DailyReadinesses` (readiness score, temperature deviation/trend, contributors), `SleepSessions` (HR, HRV, respiratory rate, all duration fields, efficiency, latency, bedtime window, sleep stage string), `DailyStresses`, `DailyActivities`, `DailySpo2s`, `DailyResilienceRecords`.
- Session preference: `long_sleep` type first, then highest (deep + REM) for the day.
- Days with no data return a row with all-null metrics (so charts show gaps rather than missing points).

**`DailyOverviewRow`** record fields:
`Day`, `SleepScore`, `ReadinessScore`, `AvgHrv`, `AvgHr`, `LowestHr`, `AvgBreath`, `DeepMinutes`, `RemMinutes`, `AwakeMinutes`, `TempDeviation`, `HrAbove75Pct`, `RestorativeMinutes`

> `HrAbove75Pct` and `RestorativeMinutes` were added in Step 3. `HrAbove75Pct` is computed via a batched `HeartRateSample` query (sleep-source samples only, windowed to session bedtime); `RestorativeMinutes = (Deep + REM) / 60`.

**`NightData`** record fields:
`UserName`, `Day`, `SleepScore`, `ReadinessScore`, `TempDeviation`, `TempTrendDeviation`,
`AverageHrv`, `AverageHeartRate`, `LowestHeartRate`, `AverageBreath`,
`DeepMinutes`, `RemMinutes`, `LightSleepMinutes`, `AwakeMinutes`, `TotalSleepMinutes`, `TimeInBedMinutes`, `Efficiency`, `LatencyMinutes`, `RestlessPeriods`, `BedtimeStart`, `BedtimeEnd`, `SleepPhase5Min`,
`SleepDeepContributor`, `SleepEfficiencyContributor`, `SleepLatencyContributor`, `SleepRemContributor`, `SleepRestfulnessContributor`, `SleepTimingContributor`, `SleepTotalContributor`,
`ReadinessActivityBalance`, `ReadinessBodyTemp`, `ReadinessHrvBalance`, `ReadinessPrevDayActivity`, `ReadinessPrevNight`, `ReadinessRecoveryIndex`, `ReadinessRhr`, `ReadinessSleepBalance`,
`StressHighSec`, `RecoveryHighSec`, `Steps`, `ActiveCalories`, `Spo2Average`, `BreathingDisturbanceIndex`, `ResilienceLevel`, `ResilienceSleepRecovery`, `ResilienceDaytimeRecovery`,
`HrvSeries`, `HeartRateSeries`

**`SamplePoint`** record: `(DateTimeOffset Time, double? Value)` — one point in an intra-night timeseries.

### Shared components

| Component | Location | Purpose |
|---|---|---|
| `UserCard` | `Components/Pages/` | Home page card per user |
| `StatBox` | `Components/Shared/` | Reusable large-value + small-label tile |

### Nav sidebar (`NavMenu.razor`)

- Injects `AppLogSink` and shows a ⚠ alert badge linking to `/debug/investigate` whenever there are captured errors or warnings.
- Footer shows `BuildInfo.Version` and `BuildInfo.BuildTime` (UTC) so you can always tell which build is running.

### Known Oura API notes (from live data)

- `GET /v2/usercollection/daily_hrv` — **endpoint does not exist** in the Oura API (not just unavailable; the path is invalid). HRV data lives inside sleep sessions (`average_hrv`, `hrv` timeseries). The `DailyHrvs` table is a dead leftover.
- `GET /v2/usercollection/vO2_max` — **capital O is required** in the path. `vo2_max` (lowercase) returns 404. Both users return data once the casing is correct.
- `TemperatureDeviation` in `DailyReadiness` **is** populated from the readiness endpoint.
- ApexCharts JS **mutates the options object** in-place during chart initialization. Each chart on a page must have its own separate `ApexChartOptions<T>` instance — sharing one object causes all charts after the first to render blank or with wrong axis bounds.

---

## Custom metrics

All custom metrics are computed **on the fly** at page load time, from the intra-night HR and HRV timeseries already loaded for the night detail page. No additional DB queries are needed — the timeseries are already in `NightData.HeartRateSeries` and `NightData.HrvSeries`.

The calculator lives in `src/OuraDashboard.Web/Services/NightMetrics.cs` and is a pure static method (no DI, no DB) — easy to unit test and easy to change thresholds.

### `NightMetrics` record fields

| Field | Type | What it measures |
|---|---|---|
| `HrAbove75Pct` | `double?` | % of 5-min HR samples above 75 bpm |
| `HrAbove80Pct` | `double?` | % of 5-min HR samples above 80 bpm |
| `HrSettlingMinutes` | `int?` | Minutes from session start until 3 consecutive samples ≤ 75 bpm; null = never settled |
| `HrvBelow12Pct` | `double?` | % of HRV samples below 12 ms (poor recovery zone) |
| `Hrv12To20Pct` | `double?` | % of HRV samples in 12–20 ms (moderate zone) |
| `HrvAbove20Pct` | `double?` | % of HRV samples above 20 ms (good recovery zone) |
| `HrvEarlyHalfAvg` | `double?` | Mean HRV of the first half of the night |
| `HrvLateHalfAvg` | `double?` | Mean HRV of the second half of the night |
| `HrvDirection` | `string` | "improving" / "declining" / "flat" / "N/A" (±2 ms dead-band) |
| `HrvPeak` | `double?` | Highest single HRV sample |
| `RestorativeMinutes` | `int?` | Deep + REM combined minutes |
| `RealRecoveryScore` | `int?` | 0–100 composite (see below) |

### Real Recovery Score formula

Four components, normalised over what was actually available:

| Component | Weight | Formula |
|---|---|---|
| HR below 75 bpm | 35 pts | `(1 − pctAbove75/100) × 35` |
| Average HRV | 25 pts | `min(avgHrv / 20, 1) × 25` |
| Restorative sleep | 25 pts | `min((deep+REM) / 150, 1) × 25` |
| Respiratory rate | 15 pts | ≤14 brpm = 15; ≥18 brpm = 0; linear between |

If a component's data is unavailable (no timeseries, no session scalar), its weight drops out
and the score is re-normalised over available weight. This means the score is always on a 0–100
scale regardless of data completeness, but may reflect fewer components.

### Oura score markers on charts

The night detail page creates **separate** `ApexChartOptions<SamplePoint>` instances for the HR and HRV charts, each pre-populated with `AnnotationsYAxis` entries:

- **HRV chart**: Oura's reported average HRV (grey dashed), 20 ms zone threshold (green), 12 ms zone threshold (red).
- **HR chart**: Oura's reported average HR (grey dashed), Oura's reported lowest HR (blue dashed), 80 bpm (red), 75 bpm (orange).

This makes it visually obvious when Oura's single-number summary misrepresents the actual timeseries.

### Metrics Guide page (`/metrics`)

Every custom metric has a dedicated section on the `/metrics` page explaining:
- What it measures and how it's calculated
- Why it was added (what Oura fails to show)
- Rough threshold guidance
- Calibration caveats (most thresholds will need personalising after 30–60 days of data)

When new metrics are added, a corresponding section **must** be added to `MetricsGuide.razor`.

---

## Infrastructure

### PostgreSQL via Docker

`docker-compose.yml` runs Postgres on **port 5433** (non-default to avoid conflicts).

```yaml
services:
  postgres:
    image: postgres:17
    restart: unless-stopped
    environment:
      POSTGRES_DB: oura
      POSTGRES_USER: oura
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    ports:
      - "5433:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

Connection string (in `appsettings.Local.json`):
```
Host=localhost;Port=5433;Database=oura;Username=oura;Password=...
```

### Deployment options

**Option A — Standalone (recommended first)**
- Postgres: `docker compose up -d postgres`
- Sync: `dotnet publish` → binary on host, run via cron
- Web: `dotnet publish` → run as systemd service or manually

**Option B — Full Docker Compose**
- A second `docker-compose.full.yml` adds `web` and `sync` service containers.
- Useful if moving to a dedicated home server where you don't want .NET SDK installed.

### Production Docker notes

Current server-side compose lives in `docker/oura-dashboard/`.

- Mount `/srv/oura-dashboard/appsettings.json` into the web container as `/app/appsettings.json`; keep application settings and Oura tokens there instead of overriding nested config through compose environment variables.
- Keep `.env` limited to values the infrastructure containers need directly, currently `DB_PASSWORD` for Postgres.
- Persist ASP.NET Core Data Protection keys at `/srv/oura-dashboard/data-protection-keys` mounted to `/home/app/.aspnet/DataProtection-Keys`; otherwise antiforgery/cookie-protected payloads can become unreadable after container replacement.
- Bind the app with `ASPNETCORE_HTTP_PORTS=8085` instead of `ASPNETCORE_URLS=http://+:8085` to avoid the .NET runtime warning about `HTTP_PORTS=8080` being overridden by `URLS`.

Recent production warnings/errors to address or verify after deployment:

| Time (UTC) | Level | Category | Message |
|---|---|---|---|
| 10:27:59 | Error | `Antiforgery.DefaultAntiforgery` | An exception was thrown while deserializing the token. Likely caused by missing/past Data Protection keys after a container restart or redeploy; old browser tokens may still fail once after the key-ring fix. |
| 10:27:52 | Warning | `Hosting.Diagnostics` | `HTTP_PORTS '8080'` / `HTTPS_PORTS ''` overridden by `URLS 'http://+:8085'`; use `ASPNETCORE_HTTP_PORTS=8085`. |
| 10:27:52 | Warning | `KeyManagement.XmlKeyManager` | No XML encryptor configured; Data Protection keys may be stored unencrypted. Decide whether host-directory permissions are sufficient for this small private deployment or add key encryption. |
| 10:27:52 | Warning | `Repositories.FileSystemXmlRepository` | Data Protection keys are stored under `/home/app/.aspnet/DataProtection-Keys` inside the container and may not survive container destruction; mount a persistent key directory. |

---

## Configuration

`appsettings.example.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5433;Database=oura;Username=oura;Password=CHANGEME"
  },
  "Oura": {
    "Users": [
      { "Name": "Boo", "Token": "YOUR_TOKEN_HERE" },
      { "Name": "Maa", "Token": "YOUR_TOKEN_HERE" }
    ]
  }
}
```

`appsettings.Local.json` is gitignored — put real tokens there.

---

## Build order (implementation steps)

1. ✅ **Solution scaffold** — `dotnet new sln`, four projects, project references, `appsettings.example.json`, `docker-compose.yml`
2. ✅ **Data layer** — entities, DbContext, Npgsql/EF provider, initial migration
3. ✅ **Sync library** — Oura HTTP client, `OuraSyncService`, upsert logic
4. ✅ **Sync CLI** — thin console app wrapping the library (`--days N`)
5. ✅ **Web: `SyncBackgroundService`** — timer + Channel trigger, `ISyncTrigger`, `/sync` status page
6. ✅ **Web: overview dashboard** — `Home.razor`, `UserCard.razor`, `DashboardQueryService`, Blazor-ApexCharts sparklines
7. ✅ **Web: per-user detail page** — `UserDetail.razor`, 4 charts, per-night table, all scalar metrics
8. ✅ **Web: compare page** — `Compare.razor`, overlaid charts, side-by-side per-night table
9. ✅ **Custom metrics (on-the-fly)** — `NightMetricsCalculator.cs`: Real Recovery Score, HR % thresholds, HR settling time, HRV distribution/direction/peak; Oura score markers on HR/HRV charts; `/metrics` guide page
10. ✅ **Web: raw export page** — `/raw`, JSON download, copy-to-clipboard
11. 🔲 **Custom metrics (trend layer)** — 7-day rolling averages on user overview; autonomic state trend line
12. 🔲 **Deployment** — systemd unit file, full Docker Compose variant (`docker-compose.full.yml`)

---

## Implementation status (last updated: 2026-05-07)

### ✅ Done

- Solution scaffold, Central Package Management, EF Core entities, migrations, Docker Compose
- Full sync pipeline (all endpoints, upsert, `SyncBackgroundService`, CLI)
- End-to-end sync verified with real Oura tokens (90 days, both users)
- **Blazor UI — all initial pages** built and working
- **Unit tests** — 37 tests for `NightMetricsCalculator`, weather provider query builders, and weather classifiers in `tests/OuraDashboard.Tests` (xUnit)
- **Step 1 — Nav cleanup**: `MetricsGuide` removed from nav; `Counter.razor` + `Weather.razor` deleted
- **Step 2 — Night page redesign**: `NightDetail.razor` restructured (verdict bar, 3 collapsible metric sections, Oura scores collapsed, daytime collapsed, raw data collapsed, breadcrumb, `GenerateSummary`)
- **Step 3 — History page**: `UserDetail.razor` updated (2 charts, heatmap table, day toggle, Oura scores toggle, `HrAbove75Pct` + `RestorativeMinutes` added to `DailyOverviewRow`)
- **Step 4 — Compare page**: `Compare.razor` rewritten (dual Y-axis HRV, clustered bar, correlation badge, heatmap, day toggle)
- **Step 5+6 — Home page**: `Home.razor` rewritten; two-column last-night strip with RRS badge (14-day personal baseline color: green/amber/red), 4 other metrics per user, `→ Night` links; dual-axis HRV chart + 4-line combo chart (HR>75% + Resp both users).
- **Step 7 — `?` popovers**: `MetricHelp.razor` shared component; 8 metric keys; Bootstrap 5 `data-bs-trigger="focus"` popovers; `initPopovers()` added to `App.razor`; wired into Home, UserDetail, Compare. `StatBox.razor` extended with optional `HelpKey`.
- **NavMenu**: added “Boo’s History” and “Maa’s History” links.
- **Step 8 — Home Zone 3**: `BuildCallouts()` in `Home.razor`; 3 pattern detectors: resp-rate linear-slope trend (last 7 nights with data), HRV consecutive-improvement streak (≥3 nights), shared bad-night run (≥2 consecutive nights where both users are in the `danger` zone vs. their 14-night baseline).
- **Raw export page**: `Raw.razor` at `/raw`; `GetRawExportAsync()` added to `DashboardQueryService` (9 endpoint cases); user + date-range + endpoint selectors; JSON `<pre>` display; copy-to-clipboard via `navigator.clipboard.writeText`; added to NavMenu.
- **Weather collection**: Open-Meteo historical archive plus Estonian Environment Agency station observations stored in `WeatherHourlySamples`, with provider identity preserved.
- **Weather UI context**: `WeatherClassifiers`, `WeatherContextStrip`, and `WeatherAnnotationLane` add pressure/sun context to home, night detail, user history, and compare pages without changing Oura score colors.
- **Weather diagnostics**: `/sync` shows pressure/sun sample counts and recent-night weather context diagnostics using the same query path as the UI chips.
- **Auto-sync controls**: `Oura:AutoSyncEnabled`, `Oura:SyncIntervalMinutes`, `Weather:AutoSyncEnabled`, and `Weather:SyncIntervalHours` control scheduled sync cadence; manual syncs remain available.

### 🔲 Still to do

- **Deployment**: systemd unit file + production Docker Compose cleanup. Use a mounted `appsettings.json`, persist Data Protection keys, avoid `ASPNETCORE_URLS`/`HTTP_PORTS` override warnings, and re-check the recent production antiforgery/Data Protection warnings.
- **Custom metrics (trend layer)**: 7-day rolling averages on user overview; autonomic state trend line
- **Weather trend diagrams/correlation**: weather-only charts and correlation tooling remain future work.

---

## Known API quirks and implementation notes

### Oura API gotchas

| Issue | Fix |
|---|---|
| `daily_hrv` and `vo2_max` return **404** on free Oura tier | `OuraApiClient` returns `(null, isNotFound: true)` — callers skip silently without counting as an error |
| `heartrate` endpoint returns **400** for date ranges >30 days | `SyncHeartRateAsync` fetches in 30-day chunks (`const ChunkDays = 30`), accumulates items, then does single upsert pass |
| `bedtime_start` / `bedtime_end` in sleep sessions have **local timezone offsets** (e.g. `+03:00`) | Call `.ToUniversalTime()` before assigning to entity — Npgsql only accepts UTC for `timestamptz` |
| Heart rate sample `timestamp` also has local offset | Same fix — `.ToUniversalTime()` |
| `daily_stress.day_summary` is a **string enum** (`"restored"`, `"normal"`, `"stressful"`), not an int | Not extracted to a scalar column (stored in `RawJson` only). `DailyStress.DaytimeStress` column exists but is unused — can add a `string? DaySummary` column + migration when needed |

### Build/tooling notes

- Use `dotnet new blazor --interactivity Server --no-https` (NOT `blazorserver` — template was renamed in .NET 10)
- Always `dotnet clean` before rebuild if EF Core package version changes (stale binaries cause `MSB3277`)
- `appsettings.json` at repo root is loaded by both Web and Sync.Cli (both use `Host.CreateDefaultBuilder` / `WebApplication.CreateBuilder`)
- `appsettings.Local.json` is gitignored — real tokens go there
