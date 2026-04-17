# oura-dashboard

Private Oura Ring data dashboard for local home server use.

Fetches and stores raw data from the Oura API into a local PostgreSQL database, then provides a web dashboard for analysis, visualisation, and LLM-ready raw data export.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `OuraDashboard.Data` | Class library | EF Core entities, DbContext, migrations |
| `OuraDashboard.Sync` | Console / Worker app | Oura API client, pulls data into DB (run manually or via cron) |
| `OuraDashboard.Web` | Blazor Server app | Dashboard, charts, raw JSON export |

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (for PostgreSQL)

### 1. Start PostgreSQL

```bash
docker compose up -d postgres
```

Runs on port **5433** (non-default, in case 5432 is already in use).

### 2. Configure secrets

Copy `appsettings.example.json` to `appsettings.Local.json` (gitignored) and fill in your Oura personal access tokens and the connection string.

### 3. Run migrations

```bash
dotnet ef database update --project src/OuraDashboard.Data --startup-project src/OuraDashboard.Web
```

### 4. Fetch data

```bash
dotnet run --project src/OuraDashboard.Sync -- --days 30
```

### 5. Start the dashboard

```bash
dotnet run --project src/OuraDashboard.Web
```

Open `http://localhost:5000`.

## Deployment

See [docs/architecture.md](docs/architecture.md) for the full design and deployment options (standalone vs Docker Compose).

