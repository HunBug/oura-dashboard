# Ollama LLM Implementation State

Last updated: 2026-05-20

## Current State

Slices 1–4 are implemented:

- Server-side Ollama connector using `/api/chat` with `stream: false`.
- Persisted production LLM interactions, including success and failure rows.
- Prompt persistence tables for future editable/versioned prompts.
- First domain feature: a generated night note on `/night/{name}/{day}`.
- `SandboxMode` flag — calls Ollama but skips all DB writes.
- `LlmDebugLog` singleton — last 3 LLM calls in memory with full prompt and raw JSON.
- `/debug/llm-sandbox` page for ad hoc inspection of recent calls.
- `Think: bool?` flag — disables extended thinking for models like gemma4 (`Think: false`). Required on small GPUs: thinking models consume the full token budget on internal reasoning and return empty `content`. `LlmClientException` carries `RawRequestJson`/`RawResponseJson` and surfaces `done_reason` in the error message when the token limit is hit mid-think.

Diagnostics, prompt-management UI, trend summaries, and memory remain
follow-up slices.

Configuration is controlled by the `Llm` section. `appsettings.example.json`
documents the full option set and keeps `Enabled` false by default. Real local
or production settings should set at least:

```json
"Llm": {
  "Enabled": true,
  "BaseUrl": "http://neolinux:11434",
  "Model": "gemma4:e4b",
  "Think": false,
  "TimeoutSeconds": 90,
  "ConnectTimeoutSeconds": 5,
  "NumPredict": 1000
}
```

`Think: false` is required for gemma4 on a small GPU — see Slice 4 notes.

## Slice 1: Connector and Configuration

Status: implemented.

- `src/OuraDashboard.Web/Services/Llm/LlmOptions.cs` binds the `Llm`
  configuration section.
- `src/OuraDashboard.Web/Services/Llm/ILlmClient.cs` defines the provider-neutral
  chat completion interface.
- `src/OuraDashboard.Web/Services/Llm/OllamaLlmClient.cs` calls Ollama
  `/api/chat` with `stream: false`.
- `src/OuraDashboard.Web/Services/Llm/LlmConcurrencyLimiter.cs` enforces
  `Llm:MaxConcurrentRequests`.
- `src/OuraDashboard.Web/Program.cs` registers options, `HttpClient`, and LLM
  services.

Behavior:

- Ollama is called only from server-side services, never browser-side code.
- The HTTP handler uses `ConnectTimeoutSeconds`; each request uses
  `TimeoutSeconds`.
- Response text is truncated to `MaxResponseChars` before storing/displaying.
- Timeouts, connection failures, HTTP failures, and malformed responses are
  converted into stored failure states.

## Slice 2: Persistence

Status: implemented.

- `src/OuraDashboard.Data/Entities/LlmInteraction.cs` stores production
  request/response/error rows.
- `src/OuraDashboard.Data/Entities/LlmPrompt.cs` reserves versioned prompt
  storage for editable prompt overrides.
- `src/OuraDashboard.Data/Migrations/20260520120000_AddLlmPersistence.cs`
  creates `LlmInteractions` and `LlmPrompts`.
- `src/OuraDashboard.Data/Migrations/OuraDbContextModelSnapshot.cs` has been
  updated for the new tables.
- `src/OuraDashboard.Web/Services/Llm/LlmRequestStore.cs` reads latest night
  interactions, reuses cache hits, inserts running rows, and records terminal
  success/failure states.

Current statuses:

- `running`
- `succeeded`
- `failed`
- `timed_out`
- `cancelled`

The first implementation stores prompt/input/messages JSON, Ollama request and
response JSON, model, provider, parameters, token counts when returned, latency,
and sanitized error fields.

## Slice 3: Night Summary Feature

Status: implemented.

- `src/OuraDashboard.Web/Services/NightLlmService.cs` builds compact typed JSON
  from `NightData`, `NightMetrics`, and `WeatherNightContext`.
- Prompt defaults live in
  `src/OuraDashboard.Web/Services/Llm/PromptCatalog.cs` with versioned keys:
  `shared.health_dashboard.system.v1` and `night.summary.v1`.
- The service hashes prompt key/version, model, parameters, user/day, input JSON,
  and messages for cache reuse.
- `src/OuraDashboard.Web/Components/Pages/NightDetail.razor` shows an "LLM note"
  panel with generate/regenerate, status, response/error, model, latency,
  timestamp, and prompt key.

Generate behavior:

- If no note exists, `Generate` can reuse a recent identical successful/running
  interaction based on `RequestCacheTtlHours`.
- If a note already exists, `Regenerate` forces a new interaction row.
- The page remains usable when LLM is disabled or Ollama fails.

## Slice 4: Sandbox Mode and Debug Log

Status: implemented.

- `Llm:SandboxMode` bool added to `LlmOptions`. When `true`, the service calls
  Ollama normally but skips all `LlmRequestStore` operations — no rows are
  inserted or updated in `LlmInteractions`.
- `NightLlmService.GetLatestNoteAsync` returns `null` in sandbox mode (nothing
  is persisted to look up).
- `NightLlmService.IsSandboxMode` exposed so the UI can show a **sandbox** badge
  on the NightDetail panel and a different empty-state message.
- `src/OuraDashboard.Web/Services/Llm/LlmDebugLog.cs` — singleton ring buffer
  (capacity 3) that captures every LLM call regardless of sandbox mode. Stores
  timestamp, scope, model, messages JSON (full prompt), raw Ollama request JSON,
  raw Ollama response JSON, response text, error code/message, latency, and
  whether the call was a sandbox call.
- `src/OuraDashboard.Web/Components/Pages/LlmSandbox.razor` — `/debug/llm-sandbox`
  page. Shows last 3 entries from `LlmDebugLog`, newest first. Each entry has
  collapsible sections for prompts sent, raw request JSON, and raw response JSON.
  Includes a Refresh button to pull the latest from the singleton.

To use sandbox mode, set in local config:

```json
"Llm": {
  "Enabled": true,
  "SandboxMode": true
}
```

## Verification

Last verified on 2026-05-20:

```bash
dotnet build src/OuraDashboard.Web/OuraDashboard.Web.csproj -v:minimal
dotnet test tests/OuraDashboard.Tests/OuraDashboard.Tests.csproj --no-build
```

Result: web build passed; test suite passed with 37 tests.

The solution-level `dotnet build` exited unsuccessfully in this environment
without reporting diagnostics, so the concrete web project and test project were
used for verification.

## Local Tooling

EF migration work uses the repo-local `dotnet-ef` tool manifest pinned to the
project EF Core version (`10.0.4`):

```bash
dotnet tool restore
dotnet tool run dotnet-ef --version
```

Then migrations can be generated with:

```bash
dotnet tool run dotnet-ef migrations add MigrationName \
  --project src/OuraDashboard.Data \
  --startup-project src/OuraDashboard.Web
```

## Follow-Up Slices

1. Add LLM diagnostics to `/sync`: enabled state, base URL host, model, health,
   available models, and recent failures.
2. Add prompt resolver backed by active `LlmPrompt` rows, with code defaults as
   fallback.
3. Add prompt-management UI with version activation rather than in-place edits.
4. Add trend summaries for `/user/{name}` windows after night notes prove useful.
