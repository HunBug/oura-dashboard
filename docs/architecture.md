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
| `DailyReadiness` | `UserId`, `Day`, `Score`, `RhrContributor`, `HrvBalanceContributor`, `RawJson` |
| `HeartRateSample` | `UserId`, `Timestamp`, `Bpm`, `Source` — minute-by-minute, high volume |
| `DailyStress` | `UserId`, `Day`, `StressHigh`, `RecoveryHigh`, `DaytimeStressScore`, `RawJson` |
| `DailyHrv` | `UserId`, `Day`, `AvgHrv5Min`, `RawJson` |
| `DailyActivity` | `UserId`, `Day`, `Steps`, `ActiveCalories`, `RawJson` |
| `Vo2Max` | `UserId`, `Day`, `Vo2Max`, `RawJson` |

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
| `GET /v2/usercollection/sleep` | Per-session detail: HR, HRV, stages |
| `GET /v2/usercollection/daily_readiness` | Readiness score + contributors |
| `GET /v2/usercollection/heartrate` | Minute-by-minute HR (datetime params) |
| `GET /v2/usercollection/daily_stress` | Daytime stress + recovery |
| `GET /v2/usercollection/daily_hrv` | Daytime HRV average |
| `GET /v2/usercollection/daily_activity` | Steps, calories, activity intensity |
| `GET /v2/usercollection/vo2_max` | VO2 max estimate |

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

Blazor Server (.NET 10). Read-only against the DB — no Oura API calls here.

### Pages

| Route | Purpose |
|---|---|
| `/` | Side-by-side overview: both users, last 30 days, key trend sparklines |
| `/user/{name}` | Per-user deep-dive: HR distribution, HRV trend, custom recovery score, cycle overlays (Maa) |
| `/compare` | Shared nights: sync/divergence analysis |
| `/raw` | Raw JSON export — select user, date range, endpoint; copy-paste to LLM |
| `/sync` | Sync status: last run, per-user results, "Refresh" trigger button |

### Custom metrics (computed at query time, not stored)

- **Real Recovery Score (0–100):** `% night HR < 75` (weighted) + `HRV avg > 15 ms` (binary) + `restorative sleep > 150 min` + `resp rate` (inverted). Replaces Oura's sleep score for Maa.
- **Autonomic State Trend:** 7-day rolling average of nocturnal HR, HRV avg, resp rate — plotted as a trend line.
- **HR Settling Time:** how many minutes after bedtime until HR drops below 75 bpm.
- **HRV Night Direction:** early-half vs late-half HRV average.
- **% Night above HR threshold:** configurable threshold (default 75, 80 bpm).

### Charts

Blazor components wrapping **Chart.js** via JS interop (lightweight, no full JS framework).

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

1. **Solution scaffold** — `dotnet new sln`, four projects, project references, `appsettings.example.json`, `docker-compose.yml`
2. **Data layer** — entities, DbContext, Npgsql/EF provider, initial migration
3. **Sync library** — Oura HTTP client, `OuraSyncService`, upsert logic
4. **Sync CLI** — thin console app wrapping the library
5. **Web: `SyncBackgroundService`** — timer + Channel trigger, `ISyncTrigger`, `/sync` status page
6. **Web: layout + raw export page** — simplest useful thing first
7. **Web: overview dashboard** — sparklines, side-by-side table
8. **Web: per-user deep-dive** — charts, custom metrics, cycle overlay
9. **Deployment** — systemd unit file, full Docker Compose variant
