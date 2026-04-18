# oura-dashboard

Private Oura Ring data dashboard for a local home server. Pulls raw data from the Oura API into PostgreSQL, then serves a Blazor Server web app for analysis and visualisation.

**Users:** Boo + Maa (two Oura rings, one dashboard).

## Projects

| Project | Type | Purpose |
|---|---|---|
| `OuraDashboard.Data` | Class library | EF Core entities, DbContext, migrations |
| `OuraDashboard.Sync` | Class library | Oura API client, `OuraSyncService`, upsert logic |
| `OuraDashboard.Sync.Cli` | Console app | Bulk / historical imports (`--days N`) |
| `OuraDashboard.Web` | Blazor Server app | Dashboard, charts, sync management |

## Pages

| Route | What you get |
|---|---|
| `/` | Side-by-side 30-day overview cards for both users — sleep score, readiness, HRV, HR, respiratory rate, deep/REM sparklines |
| `/user/{name}` | Full per-user detail: 4 charts, 9 aggregate stats, per-night data table with "→" links |
| `/night/{name}/{day}` | Single-night drill-down: intra-night HRV & HR charts, all scalars, LLM-ready text export |
| `/compare` | Boo vs Maa side-by-side — sleep score + HRV overlay charts, per-night comparison table |
| `/sync` | Live sync status, per-user result counts, manual Refresh button |

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (for PostgreSQL)

### 1. Start PostgreSQL

```bash
docker compose up -d postgres
```

Runs on port **5433** (non-default to avoid conflicts with a local 5432).

### 2. Configure secrets

Copy `appsettings.example.json` to `src/OuraDashboard.Web/appsettings.json` and fill in your Oura personal access tokens and the connection string. ⚠️ Keep this file out of git.

### 3. Run migrations

```bash
dotnet run --project src/OuraDashboard.Sync.Cli -- --migrate
```

(The `dotnet ef database update` command doesn't work directly because `OuraDbContextFactory` uses a fallback connection string. The CLI reads the real connection string from `appsettings.json`.)

### 4. Bulk-import historical data (first time)

```bash
dotnet run --project src/OuraDashboard.Sync.Cli -- --days 90
```

### 5. Start the dashboard

```bash
dotnet run --project src/OuraDashboard.Web
```

Open `http://localhost:5195`.

The background sync starts automatically at startup and runs every 60 minutes (configurable via `Oura:SyncIntervalMinutes`). Use the `/sync` page to trigger an immediate sync.

## Deployment

See [docs/architecture.md](docs/architecture.md) for the full design, configuration reference, and deployment options (standalone binary vs full Docker Compose).

