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
| `/` | Morning briefing: last-night strip (RRS, HRV, HR>75%, Restorative, Temp, weather context) for both users; dual-axis HRV 30-day chart; 4-line HR/Resp combo chart with weather annotation lanes; auto-generated pattern callouts |
| `/user/{name}` | Per-person history: 7-stat summary bar, HRV+Resp dual-axis chart, HR>75% chart, weather annotation lanes, heatmap table with weather markers; 7/14/30/90-day toggle |
| `/night/{name}/{day}` | Single-night deep dive: RRS verdict bar, weather context strip, intra-night HR + HRV charts with zone annotations, 3 collapsible metric sections, breadcrumb + prev/next nav |
| `/compare` | Boo vs Maa: dual-axis HRV overlay, clustered HR>75% bar, weather annotation lanes, correlation badge, heatmap table |
| `/sync` | Live sync status (2s poll), per-user result counts, manual refresh/reload buttons, DB totals, weather context diagnostics |
| `/raw` | Raw JSON export: user + date-range + endpoint selector, copy-to-clipboard |
| `/metrics` | Metrics guide: explanation of every custom metric (not linked from main nav) |
| `/debug/investigate` | Warning/Error log viewer + raw DB row inspector |
| `/debug/llm-sandbox` | Last 3 LLM calls: full prompts sent and raw Ollama JSON, in-memory only |

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

Background sync starts automatically unless disabled. Oura runs every 360 minutes by default (`Oura:AutoSyncEnabled`, `Oura:SyncIntervalMinutes`); weather runs every 24 hours by default (`Weather:AutoSyncEnabled`, `Weather:SyncIntervalHours`). Use the `/sync` page to trigger manual syncs.

## Deployment

See [docs/architecture.md](docs/architecture.md) for the full design, configuration reference, API quirks, and deployment options.

LLM integration (Slices 1–4) is implemented against the private Ollama instance at `http://neolinux:11434`. `/night/{name}/{day}` generates persisted Ollama-backed notes when `Llm:Enabled=true`. Set `Llm:SandboxMode=true` to call Ollama without writing any DB rows — useful for prompt experimentation. `/debug/llm-sandbox` shows the last 3 LLM calls (full prompts + raw JSON) from the current session. Current implementation state lives in [docs/ollama-implementation-plan.md](docs/ollama-implementation-plan.md); the broader design backlog lives in [docs/ollama-llm-plan.md](docs/ollama-llm-plan.md).

For LLM-assisted development, start new sessions with [docs/llm-context.md](docs/llm-context.md).
