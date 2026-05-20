# Ollama / LLM Integration Notes

Last updated: 2026-05-20

## Current environment

The private LLM sandbox is reachable from the development machine and intended to be reachable from the Oura dashboard server over the private network.

| Setting | Value |
|---|---|
| Sandbox host | `neolinux` |
| Ollama base URL | `http://neolinux:11434` |
| API tested | `GET /api/tags` |
| Access pattern | Dashboard server calls Ollama server-side |

Do not call Ollama directly from browser-side UI code. The Blazor Server app should hide LLM calls behind server-side services so prompts, model names, network endpoints, and future caching/storage behavior stay private and configurable.

## Available Ollama models

Observed from:

```bash
curl http://neolinux:11434/api/tags
```

| Model | Family | Parameters | Quantization | Size |
|---|---|---:|---|---:|
| `huihui-ministral-3b:latest` | `mistral3` | 8.5B | `Q4_K_M` | ~5.2 GB |
| `ministral-3b:latest` | `mistral3` | 8.5B | `Q4_K_M` | ~5.2 GB |
| `gemma4:26b` | `gemma4` | 25.8B | `Q4_K_M` | ~18.0 GB |
| `gemma4:e4b` | `gemma4` | 8.0B | `Q4_K_M` | ~9.6 GB |
| `llama3.1:8b` | `llama` | 8.0B | `Q4_K_M` | ~4.9 GB |
| `qwen3.6:27b` | `qwen35` | 27.8B | `Q4_K_M` | ~17.4 GB |

Initial default candidate: `llama3.1:8b`, unless quality testing shows one of the larger models is worth the latency.

## Intended app configuration

Add an `Llm` section later, likely in `/srv/oura-dashboard/appsettings.json` for production:

```json
"Llm": {
  "Enabled": true,
  "BaseUrl": "http://neolinux:11434",
  "Model": "llama3.1:8b",
  "TimeoutSeconds": 60
}
```

Equivalent environment variables, if needed for container deployment:

```text
Llm__Enabled=true
Llm__BaseUrl=http://neolinux:11434
Llm__Model=llama3.1:8b
Llm__TimeoutSeconds=60
```

## Target architecture

Planned components:

| Component | Responsibility |
|---|---|
| `LlmOptions` | Bind `Llm` config: enabled flag, base URL, model, timeout, optional generation parameters. |
| `ILlmClient` | Small internal interface for prompt execution, independent of Ollama-specific HTTP details. |
| `OllamaLlmClient` | Calls Ollama APIs such as `/api/generate` or `/api/chat`. |
| Prompt templates | Store reusable system prompts and task prompts in code or structured files. |
| Application services | Domain-specific services such as night summaries, trend explanations, and comparison summaries. |
| Database tables | Optional persistence for prompt requests, generated responses, model metadata, and timestamps. |

The UI should depend on domain-specific services, not on Ollama primitives. For example, a page should ask for a night interpretation and receive a typed response, rather than constructing prompts in `.razor` markup.

## Prompt direction

Likely prompt groups:

- Shared system prompt: private health dashboard assistant, cautious language, no diagnosis, explain uncertainty.
- Boo-specific user context prompt: stable preferences or interpretation notes for Boo.
- Maa-specific user context prompt: stable preferences or interpretation notes for Maa.
- Night summary prompt: one night of Oura metrics plus weather context.
- Trend prompt: 7/14/30/90-day patterns, separating observation from speculation.
- Comparison prompt: Boo/Maa shared-night context without implying causation.

Prompt storage is undecided. Reasonable options:

- C# constants/classes for the first version.
- Markdown prompt files embedded as content if prompts need frequent editing.
- Database-backed prompt versions later if generated-response reproducibility matters.

## Response persistence ideas

Do not design the database table until the first concrete feature is chosen. Likely useful fields:

- User id or comparison scope.
- Date range or night date.
- Prompt key/version.
- Model name.
- Request payload or normalized input JSON.
- Response text and optional structured JSON.
- Created timestamp and latency.
- Error text if generation failed.

Storing responses is useful for slow models, repeatability, and avoiding accidental prompt drift. For early experiments, live generation without persistence is acceptable.

## First implementation candidate

Start with a narrow feature:

1. Add `LlmOptions`, `ILlmClient`, and `OllamaLlmClient`.
2. Add a health/test method that lists models or performs a short generation.
3. Add one domain service for "night summary" using existing `NightData` and `WeatherNightContext`.
4. Show the summary on `/night/{name}/{day}` behind a button, disabled when `Llm:Enabled` is false.
5. Decide after testing whether responses should be persisted.

## Operational notes

- Keep Ollama exposed only on the private network or firewall it to the dashboard server.
- Production dashboard runs in Docker under `docker/oura-dashboard/` and mounts `/srv/oura-dashboard/appsettings.json`.
- The dashboard server must be able to resolve `neolinux`; otherwise use the sandbox server's private IP in `Llm:BaseUrl`.
- Prefer server-side calls from .NET with configured timeouts and cancellation tokens.
