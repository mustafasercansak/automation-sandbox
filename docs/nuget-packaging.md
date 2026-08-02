# NuGet Packaging Guide / NuGet Paketleme Rehberi

Automation Sandbox packages are prepared as preview artifacts first. The `Pack`
workflow creates `.nupkg` and `.snupkg` files, but it does not publish to nuget.org
until a package feed and API key policy are chosen.

## Packages

| Package | Purpose |
| :--- | :--- |
| `AutomationSandbox.UiModel` | Shared UI snapshot model, score DTOs, locator repository JSON. |
| `AutomationSandbox.SelfHealing` | Heuristic resolver, engine SDK, healing reports, repository update helpers. |
| `AutomationSandbox.LlmHealing` | Claude, Gemini, OpenAI, and Ollama healing providers. |
| `AutomationSandbox.Discovery` | Windows UIA/FlaUI desktop tree capture (`net48`). |
| `AutomationSandbox.WebDiscovery` | Playwright DOM mapping and locator suggestions. |

## Create Preview Packages

In GitHub Actions:

1. Open **Actions**.
2. Select **Pack**.
3. Click **Run workflow**.
4. Enter a version such as `0.1.0-preview.1`.
5. Download the `nupkgs` artifact.

Locally:

```powershell
dotnet restore AutomationSandbox.sln
dotnet build AutomationSandbox.sln --configuration Release --no-restore
dotnet pack TestAutomation/SelfHealing/SelfHealing.csproj --configuration Release --no-build --output ./nupkgs /p:PackageVersion=0.1.0-preview.1
```

## Consume From A Local Folder

```powershell
dotnet nuget add source ./nupkgs --name automation-sandbox-local
dotnet add package AutomationSandbox.SelfHealing --version 0.1.0-preview.1 --source automation-sandbox-local
```

## Publish Checklist

- CI is green on `main`.
- Pack workflow produces all expected `.nupkg` and `.snupkg` files.
- Package names, README, license, repository URL, and symbols are present.
- Version follows prerelease SemVer, for example `0.1.0-preview.1`.
- Publish target is decided: nuget.org, GitHub Packages, or internal feed.
- API key is stored as a GitHub Actions secret before adding any push step.

---

# Türkçe Özet

`Pack` workflow'u NuGet paketlerini artifact olarak üretir; şu aşamada nuget.org'a
otomatik yayın yapmaz. İlk güvenli adım paketleri indirip lokal feed üzerinden denemektir.

Önerilen ilk sürüm: `0.1.0-preview.1`
