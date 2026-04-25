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

**Built-in (default, recommended):** `SyncBackgroundService` inside the web app runs on a configurable interval (e.g. `"SyncIntervalMinutes": 60` in appsettings). The "Refresh" button on `/sync` sends a trigger through the `Channel` for an immediate out-of-schedule run. Nothing extra to deploy.

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
| `/` | `Home.razor` | ✅ Side-by-side user cards, 30-day sparklines (sleep score + HRV), 7 aggregate stats each, "Detail →" link per user |
| `/user/{name}` | `UserDetail.razor` | ✅ **Redesigned (Step 3)**: 7-stat summary, 2 charts (HRV+Resp dual-axis; HR>75% bar + Restorative line), heatmap table, 7/14/30/90 day toggle, Oura scores toggle |
| `/night/{name}/{day}` | `NightDetail.razor` | ✅ **Redesigned (Step 2)**: Verdict bar (RRS color + `GenerateSummary`), charts zone, 3 collapsible metric sections, Oura scores (collapsed), daytime (collapsed), raw data (collapsed), breadcrumb, prev/next nav |
| `/compare` | `Compare.razor` | ✅ **Redesigned (Step 4)**: Dual Y-axis HRV, clustered bar HR>75%, resp rate + temp charts, zone-alignment correlation badge, heatmap table, 30/60/90 day toggle |
| `/sync` | `Sync.razor` | ✅ Live sync state (2-second poll), per-user result counts, "Refresh" button |
| `/metrics` | `MetricsGuide.razor` | ⚠️ Removed from nav (Step 1). Page still exists; not yet dissolved into `?` popovers (Step 7). |

### Pages — redesign target 🔲

See `docs/redesign-plan.md` for full detail on each page. Summary of route + component changes:

| Route | Component | Change | Status |
|---|---|---|---|
| `/` | `Home.razor` | **Redesign** | 🔲 Pending (Steps 5–6) |
| `/night/{name}/{day}` | `NightDetail.razor` | **Restructure** | ✅ Done (Step 2) |
| `/user/{name}` | `UserDetail.razor` | **Enhance** | ✅ Done (Step 3) |
| `/compare` | `Compare.razor` | **Redesign** | ✅ Done (Step 4) |
| `/sync` | `Sync.razor` | No change | ✅ No action needed |
| `/metrics` | `MetricsGuide.razor` | **Remove from nav → `?` popovers** | 🔲 Pending (Step 7) |

**Removed entirely (Step 1):** `Counter.razor`, `Weather.razor`. `UserCard.razor` kept (used on Home).

### Pages — planned 🔲

| Route | Purpose |
|---|---|
| `/raw` | Raw JSON export — select user, date range, endpoint; copy-paste to LLM |

### Services

**`DashboardQueryService`** (Scoped) — all DB reads for the dashboard.
- `GetUserOverviewAsync(userName, days)` → `UserOverview` with one `DailyOverviewRow` per calendar day. Start date clamped to user’s earliest session so charts don’t open with empty left-side gaps.
- `GetNightDetailAsync(userName, day)` → `NightData?` — scalars + intra-night `HrvSeries`/`HeartRateSeries` + sleep stage string + contributors from all 6 tables.
- `GetNightDaysAsync(userName, days)` → `List<DateOnly>` (descending) — days that have a `long_sleep` session, used for prev/next navigation on the night detail page.
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
10. 🔲 **Web: raw export page** — `/raw`, JSON download, copy-to-clipboard
11. 🔲 **Custom metrics (trend layer)** — 7-day rolling averages on user overview; autonomic state trend line
12. 🔲 **Deployment** — systemd unit file, full Docker Compose variant

---

## Implementation status (last updated: 2026-04-25)

### ✅ Done

- Solution scaffold, Central Package Management, EF Core entities, migrations, Docker Compose
- Full sync pipeline (all endpoints, upsert, `SyncBackgroundService`, CLI)
- End-to-end sync verified with real Oura tokens (90 days, both users)
- **Blazor UI — all initial pages** built and working
- **Unit tests** — 25 tests for `NightMetricsCalculator` / RRS formula in `tests/OuraDashboard.Tests` (xUnit)
- **Step 1 — Nav cleanup**: `MetricsGuide` removed from nav; `Counter.razor` + `Weather.razor` deleted
- **Step 2 — Night page redesign**: `NightDetail.razor` restructured (verdict bar, 3 collapsible metric sections, Oura scores collapsed, daytime collapsed, raw data collapsed, breadcrumb, `GenerateSummary`)
- **Step 3 — History page**: `UserDetail.razor` updated (2 charts, heatmap table, day toggle, Oura scores toggle, `HrAbove75Pct` + `RestorativeMinutes` added to `DailyOverviewRow`)
- **Step 4 — Compare page**: `Compare.razor` rewritten (dual Y-axis HRV, clustered bar, correlation badge, heatmap, day toggle)

### 🔲 Still to do

- **Step 5 — Home page Zone 1**: Two-column morning briefing (last night, both users, 5 metrics, RRS color vs 14-day personal baseline)
- **Step 6 — Home page Zone 2**: Dual-axis HRV trend chart + 4-line combo chart (HR>75% + Resp)
- **Step 7 — `?` popovers**: Dissolve MetricsGuide into inline Bootstrap 5 popovers on every metric label
- **Step 8 — Home Zone 3**: Pattern callout engine (deferred last)
- **Raw export page** (`/raw`): date-range JSON export / copy to LLM
- **Deployment**: systemd unit, `docker-compose.full.yml`

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

### Next implementation step: wire up `OuraDashboard.Web/Program.cs`

```csharp
// In src/OuraDashboard.Web/Program.cs, after var builder = WebApplication.CreateBuilder(args):
builder.Services.Configure<OuraOptions>(builder.Configuration.GetSection(OuraOptions.SectionName));
builder.Services.AddOuraDatabase(builder.Configuration.GetConnectionString("Default")!);
builder.Services.AddOuraSync(addBackgroundService: true);
```

Then build the `/sync` page first (simplest — just reads `ISyncTrigger.State` and calls `RequestSync()`).

