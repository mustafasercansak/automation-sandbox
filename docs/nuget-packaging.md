---
layout: default
title: NuGet Packaging Guide - Automation Sandbox
---

# NuGet Packaging Guide / NuGet Paketleme Rehberi

Automation Sandbox packages are prepared as preview artifacts first. The `Pack`
workflow creates `.nupkg` and `.snupkg` files without publishing anywhere. The
`Release Preview Packages` workflow can optionally push to nuget.org via Trusted
Publishing (OIDC, no stored API key) when its `publish_to_nuget` input is enabled.
Published packages are available from
[nuget.org](https://www.nuget.org/profiles/mustafasercansak), as well as from the
GitHub prerelease assets.

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

For the public API surface definition, semantic versioning rules, and checkable criteria required to graduate to 1.0 GA, see the [API Stability & Beta-Exit Criteria](versioning-and-stability.md) guide.

## Create Preview Packages

In GitHub Actions:

1. Open **Actions**.
2. Select **Pack**.
3. Click **Run workflow**.
4. Leave version blank to use `Directory.Build.props` (or specify an explicit override).
5. Download the `nupkgs` artifact.

## Create A GitHub Release

Use **Release Preview Packages** when you want the package files to appear on the
repository's **Releases** page without publishing to nuget.org:

1. Ensure the release notes file exists at `docs/release-notes/v<version>.md`. The release workflow validates this file and fails if it is missing or empty.
2. Open **Actions**.
3. Select **Release Preview Packages**.
4. Click **Run workflow**.
5. Leave version blank to use `Directory.Build.props` (or specify an explicit override).
6. Keep `prerelease` enabled for preview builds.
7. Enable `publish_to_nuget` to also push the packages to nuget.org via Trusted
   Publishing, or leave it disabled to keep the release as GitHub-only assets.
8. Download `.nupkg` and `.snupkg` files from the created GitHub Release.

The latest preview includes the Phase 3 measurement work and the opt-in
`ResolveBatch` / `ResolveBatchAsync` ownership guard on top of the Phase 2 consensus and
provider-resilience work. A single configured LLM provider still does not constitute
consensus, and batch reconciliation remains a collision guard rather than an absence
detector; see the release notes and benchmark guide for the measured limits.

Locally:

```powershell
dotnet restore AutomationSandbox.sln
dotnet build AutomationSandbox.sln --configuration Release --no-restore
dotnet pack TestAutomation/SelfHealing/SelfHealing.csproj --configuration Release --no-build --output ./nupkgs
```

## Consume From nuget.org

Install the latest prerelease package:

```powershell
dotnet add package AutomationSandbox.SelfHealing --prerelease
```

The other six packages use the same version. Add only the packages whose APIs the
consumer uses; NuGet restores their package dependencies transitively.

For a complete first run, use the [Published Package Quickstart](consumer-quickstart.md).
The checked-in consumer project has no repository project references, and its verification
script restores exclusively from nuget.org into a clean temporary package directory. Run
it from the repository root:

```powershell
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1
```

## Consume From A Local Folder

```powershell
dotnet nuget add source ./nupkgs --name automation-sandbox-local
dotnet add package AutomationSandbox.SelfHealing --prerelease --source automation-sandbox-local
```

## Publish Checklist

- CI is green on `main`.
- Pack workflow produces all expected `.nupkg` and `.snupkg` files.
- GitHub Release assets include all seven packages and their symbol packages.
- Package names, README, license, repository URL, and symbols are present.
## Version Bumps / Sürüm Güncelleme

The package version is authoritatively defined in a single location:
- `Directory.Build.props` -> `<Version>X.Y.Z-prerelease</Version>`

When bumping the version:
1. Update `<Version>` in `Directory.Build.props`.
2. Update `<PackageReference>` in `samples/HeuristicHealingQuickstart/HeuristicHealingQuickstart.csproj`.
3. Run `dotnet test --filter PackageVersionDriftTests` to ensure consistency across the repository.

The packaging workflows (`pack.yml`, `release.yml`) and `verify.ps1` resolve `<Version>` dynamically from `Directory.Build.props` when no explicit version override is supplied.

---

# Türkçe Özet

`Pack` workflow'u NuGet paketlerini yalnızca artifact olarak üretir ve herhangi bir
feed'e yayın yapmaz. `Release Preview Packages` workflow'u ise `publish_to_nuget`
etkinleştirildiğinde saklanan bir API anahtarı olmadan Trusted Publishing (OIDC) ile
nuget.org'a yayın yapabilir. Yayınlanan yedi paketin tamamı nuget.org'da ve GitHub
prerelease asset'lerinde bulunur. Yerel doğrulama için `Pack` artifact'leri ayrıca lokal
feed üzerinden tüketilebilir. Yayınlanan prerelease paketi doğrudan tüketmek için:

```powershell
dotnet add package AutomationSandbox.SelfHealing --prerelease
```

İlk çalıştırmanın tamamı için [Yayınlanmış Paket Hızlı Başlangıcını](consumer-quickstart.md)
kullanın. Repository içi project reference içermeyen consumer örneğini yalnızca nuget.org
kaynağından temiz bir geçici paket dizinine restore edip çalıştırmak için aşağıdaki komutu
repository kökünden çalıştırın:

```powershell
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1
```
