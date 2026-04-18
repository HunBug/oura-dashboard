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

### Pages — built ✅

| Route | Component | Status |
|---|---|---|
| `/` | `Home.razor` | ✅ Side-by-side user cards, 30-day sparklines (sleep score + HRV), 7 aggregate stats each, "Detail →" link per user |
| `/user/{name}` | `UserDetail.razor` | ✅ 9-stat summary, 4 charts (sleep+readiness, HRV, HR+lowest HR, respiratory rate), per-night table with "→" detail links |
| `/night/{name}/{day}` | `NightDetail.razor` | ✅ Intra-night HRV & HR charts (5-min ApexCharts timeseries), 10 scalar stats, prev/next night nav, LLM text export (copy to clipboard) |
| `/compare` | `Compare.razor` | ✅ Sleep score + HRV overlay charts (both users), side-by-side per-night table |
| `/sync` | `Sync.razor` | ✅ Live sync state (2-second poll), per-user result counts, "Refresh" button |

### Pages — planned 🔲

| Route | Purpose |
|---|---|
| `/raw` | Raw JSON export — select user, date range, endpoint; copy-paste to LLM |

### Services

**`DashboardQueryService`** (Scoped) — all DB reads for the dashboard.
- `GetUserOverviewAsync(userName, days)` → `UserOverview` with one `DailyOverviewRow` per calendar day. Start date clamped to user’s earliest session so charts don’t open with empty left-side gaps.
- `GetNightDetailAsync(userName, day)` → `NightData?` — scalars + intra-night `HrvSeries`/`HeartRateSeries` parsed from the session’s JSONB timeseries.
- `GetNightDaysAsync(userName, days)` → `List<DateOnly>` (descending) — days that have a `long_sleep` session, used for prev/next navigation on the night detail page.
- Joins: `DailySleep` (score), `DailyReadinesses` (readiness score, temperature deviation), `SleepSessions` (HRV, HR, lowest HR, respiratory rate, deep/REM/awake minutes, JSONB timeseries).
- Session preference: `long_sleep` type first, then highest (deep + REM) for the day.
- Days with no data return a row with all-null metrics (so charts show gaps rather than missing points).

**`DailyOverviewRow`** record fields:
`Day`, `SleepScore`, `ReadinessScore`, `AvgHrv`, `AvgHr`, `LowestHr`, `AvgBreath`, `DeepMinutes`, `RemMinutes`, `AwakeMinutes`, `TempDeviation`

**`NightData`** record fields:
`UserName`, `Day`, `SleepScore`, `ReadinessScore`, `TempDeviation`, `AverageHrv`, `AverageHeartRate`, `LowestHeartRate`, `AverageBreath`, `DeepMinutes`, `RemMinutes`, `AwakeMinutes`, `HrvSeries`, `HeartRateSeries`

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

### Custom metrics — planned 🔲

- **Real Recovery Score (0–100):** `% night HR < 75` (weighted) + `HRV avg > 15 ms` (binary) + `restorative sleep > 150 min` + `resp rate` (inverted). Requires `HeartRateSample` queries.
- **Autonomic State Trend:** 7-day rolling average of nocturnal HR, HRV avg, resp rate.
- **HR Settling Time:** minutes after bedtime until HR drops below 75 bpm.
- **HRV Night Direction:** early-half vs late-half HRV average.
- **% Night above HR threshold:** configurable threshold (default 75 bpm).

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
9. 🔲 **Web: raw export page** — `/raw`, JSON download, copy-to-clipboard
10. 🔲 **Custom metrics** — Real Recovery Score, HR Settling Time, HRV Night Direction (requires `HeartRateSample` queries)
11. 🔲 **Deployment** — systemd unit file, full Docker Compose variant

---

## Implementation status (last updated: 2026-04-17)

### ✅ Done

- Solution scaffold: `OuraDashboard.slnx` (4 projects, project references, `.slnx` format — .NET 10 default)
- Central Package Management via `Directory.Packages.props` — all NuGet versions pinned there, no `Version=` in `.csproj` files
- `Directory.Build.props` with `<AllowMissingPrunePackageData>true</AllowMissingPrunePackageData>` (.NET 10 workaround)
- EF Core 10.0.4 + Npgsql.EF 10.0.1 (pinned to avoid `MSB3277` version conflict; `dotnet-ef` global tool also at 10.0.6)
- All 9 EF Core entities in `src/OuraDashboard.Data/Entities/`
- `OuraDbContext` with Fluent API (JSONB columns, unique indexes, FK)
- `OuraDbContextFactory` for design-time migrations
- `InitialSchema` migration created and applied
- `docker-compose.yml` — Postgres 17 on port 5433, healthcheck, `pgdata` volume
- `OuraOptions` config model (`SectionName = "Oura"`, `Users`, `SyncIntervalMinutes`, `SyncLookbackDays`)
- `OuraApiClient` — thin HTTP wrapper, returns `(JsonDocument? Doc, bool IsNotFound)` tuple
- `OuraSyncService` — syncs all 8 endpoints per user, upsert on natural keys, returns `SyncResult`
- `ISyncTrigger` / `SyncState` / `SyncBackgroundService` — timer + `Channel<bool>` manual trigger
- `Sync.ServiceCollectionExtensions` — `AddOuraSync(addBackgroundService: bool)`
- `Data.ServiceCollectionExtensions` — `AddOuraDatabase(connectionString)`
- `Sync.Cli/Program.cs` — `--days N`, `--migrate` flags
- **End-to-end sync verified with real Oura tokens** — 90 days of data for both users

### ❌ Not started yet

- `src/OuraDashboard.Web/Program.cs` — needs `AddOuraDatabase()`, `AddOuraSync(addBackgroundService: true)`, `Configure<OuraOptions>()`
- All Blazor pages: `/sync`, `/raw`, `/`, `/user/{name}`, `/compare`
- Chart.js JS interop setup
- Deployment: systemd unit, `docker-compose.full.yml`

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

