# oura-dashboard

Private Oura Ring data dashboard for a home server. Pulls raw data from the Oura API into PostgreSQL, then serves a Blazor Server web app for analysis and visualisation.

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
| `/` | Morning briefing: last-night strip (RRS, HRV, HR>75%, Restorative, Temp) for both users; dual-axis HRV 30-day chart; 4-line HR/Resp combo chart; auto-generated pattern callouts |
| `/user/{name}` | Per-person 30-day history: 7-stat summary bar, HRV+Resp dual-axis chart, HR>75% chart, heatmap table; 7/14/30/90-day toggle |
| `/night/{name}/{day}` | Single-night deep dive: RRS verdict bar, intra-night HR + HRV charts with zone annotations, 3 collapsible metric sections, breadcrumb + prev/next nav |
| `/compare` | Boo vs Maa: dual-axis HRV overlay, clustered HR>75% bar, correlation badge, heatmap table |
| `/sync` | Live sync status (2s poll), per-user result counts, manual Refresh button |
| `/raw` | Raw JSON export: user + date-range + endpoint selector, copy-to-clipboard |
| `/metrics` | Metrics guide: explanation of every custom metric (not linked from main nav) |

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

See [docs/architecture.md](docs/architecture.md) for the full design, configuration reference, API quirks, and deployment options.

For LLM-assisted development, start new sessions with [docs/llm-context.md](docs/llm-context.md).

