# CLAUDE.md — oura-dashboard

This file gives Claude Code the context it needs to work effectively in this repo.
Read `docs/architecture.md` for the full reference. This file is the compact version.

## Project summary

Private Oura Ring health dashboard. Two users (Boo + Maa). Home server deployment.

- **Data source:** Oura Cloud API → PostgreSQL 17
- **Frontend:** Blazor Server (.NET 10), Bootstrap 5, Blazor-ApexCharts 6.1.0
- **Philosophy:** distributions and trends, not point-in-time scores

## Repository layout

```
src/OuraDashboard.Data/       → EF Core entities, DbContext, migrations
src/OuraDashboard.Sync/       → Oura API client + OuraSyncService
src/OuraDashboard.Sync.Cli/   → CLI for bulk imports (--days N, --migrate)
src/OuraDashboard.Web/        → Blazor Server app (all UI + background sync)
  Components/Pages/           → All .razor pages
  Services/
    DashboardQueryService.cs  → All DB reads (Scoped)
    NightMetrics.cs           → Pure static custom metric calculator
    DebugInvestigationService.cs
    BuildInfo.cs              → Static: Version + BuildTime (read from DLL write-time)
    AppLogSink.cs             → Singleton ILoggerProvider capturing Warning+ entries in memory
tests/OuraDashboard.Tests/    → xUnit, 25 tests for NightMetricsCalculator
docs/architecture.md          → Full design reference
```

## Must-know conventions

1. **Central Package Management** — versions in `Directory.Packages.props` only. No `Version=` in `.csproj` files.
2. **App version** — bump `<Version>` in `Directory.Build.props` before every meaningful release or deployment. The comment in that file and `push.sh` remind you.
3. **Secrets** — `appsettings.Local.json` is gitignored. Never commit tokens.
4. **ApexCharts** — each chart needs its own `ApexChartOptions<T>` instance. Shared instances cause blank charts.
5. **Timezones** — Oura timestamps have local offsets. Call `.ToUniversalTime()` before assigning to EF entities.
6. **Custom metrics** — `NightMetrics.cs` is a pure static method (no DI, no DB). Keep it that way.
7. **Upsert** — all sync operations upsert on `(UserId, Day)`. Safe to re-run.

## Key API quirks

| Quirk | Fix |
|---|---|
| `daily_hrv` endpoint doesn't exist | `DailyHrvs` DB table is a dead leftover — ignore it |
| `vO2_max` needs capital O | Use `vO2_max` in URL path |
| `heartrate` returns 400 for >30 days | Fetch in 30-day chunks |
| Bedtime timestamps have local timezone offsets | `.ToUniversalTime()` before saving |

## Common commands

```bash
# Start Postgres
docker compose up -d postgres

# Apply EF migrations (via CLI, not dotnet ef directly)
dotnet run --project src/OuraDashboard.Sync.Cli -- --migrate

# Bulk import
dotnet run --project src/OuraDashboard.Sync.Cli -- --days 90

# Run web app → http://localhost:5195
dotnet run --project src/OuraDashboard.Web

# Run tests
dotnet test tests/OuraDashboard.Tests
```

## Pages

| Route | File |
|---|---|
| `/` | `Home.razor` — morning briefing (RRS strip + trend charts) |
| `/user/{name}` | `UserDetail.razor` — per-person history |
| `/night/{name}/{day}` | `NightDetail.razor` — single-night deep dive |
| `/compare` | `Compare.razor` — Boo vs Maa overlay |
| `/sync` | `Sync.razor` — sync status + trigger + DB totals (Oura days + weather samples per source) |
| `/raw` | `Raw.razor` — raw JSON export |
| `/metrics` | `MetricsGuide.razor` — custom metric explanations |
| `/debug/investigate` | `DebugInvestigate.razor` — Warning/Error log viewer (from `AppLogSink`) + raw DB row inspector |
| `/debug/llm-sandbox` | `LlmSandbox.razor` — last 3 LLM calls from `LlmDebugLog`: prompts + raw Ollama JSON, in memory only |

## Custom metrics (NightMetrics.cs)

`RealRecoveryScore` (0–100): HR<75bpm (35%) + avg HRV (25%) + restorative sleep (25%) + resp rate (15%).
Components drop out gracefully if data is missing; score renormalises to available weight.

Other computed fields: `HrAbove75Pct`, `HrAbove80Pct`, `HrSettlingMinutes`, `HrvDirection` (improving/declining/flat), `HrvEarlyHalfAvg`, `HrvLateHalfAvg`, `HrvPeak`, `RestorativeMinutes`.

## What's done / what's next

All core UI is complete. Outstanding work:
- **Deployment**: `docker-compose.full.yml` + systemd unit file
- **Custom metrics trend layer**: 7-day rolling averages on UserDetail
