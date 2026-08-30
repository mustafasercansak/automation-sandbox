# AutomationSandbox.LlmHealing

LLM provider implementations for locator resolution behind the shared `ILlmHealingProvider` interface: Claude, Gemini, offline Ollama, and an OpenAI provider that accepts any OpenAI-compatible endpoint (Groq, Cerebras, OpenRouter, Cloudflare Workers AI, Azure OpenAI, or a local gateway). All four share one HTTP base with retry and exponential backoff, dual timeout budgets, and a `Retry-After` quota guard. Authentication headers are set per request, never on a shared `HttpClient`, so keys cannot leak across vendors.

## Install

```bash
dotnet add package AutomationSandbox.LlmHealing --prerelease
```

## Target frameworks

- `netstandard2.0`
- `net8.0`
- `net10.0`

## Typical use

```csharp
// Providers are created from environment variables; absent keys are skipped.
IReadOnlyList<ILlmHealingProvider> providers = LlmProviderFactory.CreateConfiguredProviders();
```

Environment variables: `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `OPENAI_API_KEY`, `MISTRAL_API_KEY` + `MISTRAL_MODEL`, `NVIDIA_API_KEY` + `NVIDIA_MODEL`, `CLOUDFLARE_API_TOKEN` + `CLOUDFLARE_ACCOUNT_ID` + `CLOUDFLARE_MODEL`, `OLLAMA_CLOUD_API_KEY` + `OLLAMA_CLOUD_MODEL`, and `OLLAMA_HOST`/`OLLAMA_MODEL`/`OLLAMA_ENABLED` for a local Ollama daemon. Everything is optional — the factory simply returns fewer providers.

This package is a transitive dependency of `AutomationSandbox.SelfHealing`; reference it directly when you want to construct or evaluate providers yourself (`LlmHealingEvaluator`).

## Related packages

- `AutomationSandbox.SelfHealing` — consumes these providers with an independent-agreement quorum.
- `AutomationSandbox.UiModel` — the snapshot model the prompts are built from.

## Documentation

Provider configuration, the consensus rule, and the data-disclosure boundary are covered in the [Automation Sandbox documentation](https://github.com/mustafasercansak/automation-sandbox/tree/main/docs) — see `docs/llm-providers.md` and `docs/llm-security-model.md`.
