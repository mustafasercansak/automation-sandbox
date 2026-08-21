# NuGet Packaging Guide / NuGet Paketleme Rehberi

Automation Sandbox packages are prepared as preview artifacts first. The `Pack`
workflow creates `.nupkg` and `.snupkg` files without publishing anywhere. The
`Release Preview Packages` workflow can optionally push to nuget.org via Trusted
Publishing (OIDC, no stored API key) when its `publish_to_nuget` input is enabled.

M5 preview packaging is implemented. Both packaging workflows build all seven packages,
validate their package/symbol pairs and required contents, and expose the artifacts
either as a workflow download or as GitHub prerelease assets. A nuget.org Trusted
Publishing policy (owner `mustafasercansak`, repo `automation-sandbox`, workflow
`release.yml`, glob `AutomationSandbox.*`) is registered; pushing is still opt-in
per run via `publish_to_nuget` rather than automatic on every release.

## Packages

| Package | Purpose |
| :--- | :--- |
| `AutomationSandbox.UiModel` | Shared UI snapshot model, score DTOs, locator repository JSON. |
| `AutomationSandbox.SelfHealing` | Heuristic resolver, engine SDK, healing reports, repository update helpers. |
| `AutomationSandbox.LlmHealing` | Claude, Gemini, OpenAI, and Ollama healing providers. |
| `AutomationSandbox.Discovery` | Windows UIA/FlaUI desktop tree capture (`net48`). |
| `AutomationSandbox.WebDiscovery` | Playwright DOM mapping and locator suggestions. |
| `AutomationSandbox.IntentAutomation` | Intent contracts, deterministic + LLM-backed planning, web and desktop candidate matching, locator recording, Playwright C#/TypeScript and FlaUI test generation, intent flow reports, and pipeline orchestration. |
| `AutomationSandbox.PlaywrightLiveExploration` | `PlaywrightLiveExplorer`: live browser page capture (Microsoft.Playwright .NET SDK) into a `WebElementInfo` snapshot. |

## Create Preview Packages

In GitHub Actions:

1. Open **Actions**.
2. Select **Pack**.
3. Click **Run workflow**.
4. Enter a version such as `0.2.0-beta.2`.
5. Download the `nupkgs` artifact.

## Create A GitHub Release

Use **Release Preview Packages** when you want the package files to appear on the
repository's **Releases** page without publishing to nuget.org:

1. Open **Actions**.
2. Select **Release Preview Packages**.
3. Click **Run workflow**.
4. Enter a version such as `0.2.0-beta.2`.
5. Keep `prerelease` enabled for preview builds.
6. Enable `publish_to_nuget` to also push the packages to nuget.org via Trusted
   Publishing, or leave it disabled to keep the release as GitHub-only assets.
7. Download `.nupkg` and `.snupkg` files from the created GitHub Release.

For the `0.2.0-beta.2` preview release, the package assets carry the Phase 2 hardening work
on top of `0.2.0-beta.1`: consensus-based acceptance for LLM picks (at least two providers
must agree), provider resilience (retry with exponential backoff, dual timeout budgets, a
`Retry-After` quota ceiling), per-provider attempt telemetry, and healing report schema v7 with explicit accepted, declined, provider-error, and retry-failed outcomes.
Note that consensus is a behaviour change: a single configured LLM provider no longer has
its pick accepted.

Locally:

```powershell
dotnet restore AutomationSandbox.sln
dotnet build AutomationSandbox.sln --configuration Release --no-restore
dotnet pack TestAutomation/SelfHealing/SelfHealing.csproj --configuration Release --no-build --output ./nupkgs /p:PackageVersion=0.2.0-beta.2
```

## Consume From A Local Folder

```powershell
dotnet nuget add source ./nupkgs --name automation-sandbox-local
dotnet add package AutomationSandbox.SelfHealing --version 0.2.0-beta.2 --source automation-sandbox-local
```

## Publish Checklist

- CI is green on `main`.
- Pack workflow produces all expected `.nupkg` and `.snupkg` files.
- GitHub Release assets include all seven packages and their symbol packages.
- Package names, README, license, repository URL, and symbols are present.
- Version follows prerelease SemVer, for example `0.2.0-beta.2`.
- Publish target is nuget.org, via a Trusted Publishing policy (no stored API key).
  The `NUGET_USER` repo secret holds the nuget.org username the OIDC login step
  presents; it is not a credential by itself and rotates automatically since the
  API key it exchanges for expires after 1 hour.

---

# Türkçe Özet

`Pack` workflow'u NuGet paketlerini artifact olarak üretir; şu aşamada nuget.org'a
otomatik yayın yapmaz. İlk güvenli adım paketleri indirip lokal feed üzerinden denemektir.

Önerilen ilk sürüm: `0.2.0-beta.2`
