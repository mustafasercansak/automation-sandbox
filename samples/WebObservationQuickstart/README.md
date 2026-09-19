# Web Observation Quickstart / Web Gözlem Hızlı Başlangıç

This .NET 8 console sample launches real headless Chromium via the `Microsoft.Playwright` .NET SDK to demonstrate `PlaywrightWebSession` (long-lived sessions, interaction, storage-state persistence) and `AutomationSandbox.ContentAnalysis` working together end to end, against a tiny fixture app served over a real local HTTP server.

Bu .NET 8 console örneği, `Microsoft.Playwright` .NET SDK ile gerçek bir headless Chromium başlatarak `PlaywrightWebSession` (uzun ömürlü oturum, etkileşim, storage-state kalıcılığı) ile `AutomationSandbox.ContentAnalysis`'in gerçek bir yerel HTTP sunucusu üzerinden sunulan küçük bir örnek uygulamaya karşı birlikte nasıl çalıştığını uçtan uca gösterir.

---

## What this scenario demonstrates / Bu senaryo neleri gösterir

1. **Authenticate once**: fills a login form (`FillAsync`/`ClickAsync`) on a fresh `PlaywrightWebSession`, then `SaveStorageStateAsync`.
2. **Reuse the session without logging in again**: a brand-new session started with `StartAsync(storageStatePath: ...)` opens the dashboard already authenticated.
3. **Content-quality check**: `ContentAnalyzer` flags a deliberately planted leftover `TODO` placeholder on the dashboard.
4. **Observation layer**: the session's `NetworkResponses`/`ConsoleMessages` catch a deliberately broken image reference (a real 404), proving the observation layer surfaces real page problems, not just navigation/DOM state.

A real HTTP server is required here (not a `file://` page): Playwright's storage state does not carry `localStorage` for `file://` origins — confirmed while building the storage-state feature (#450) — so the sample runs an in-process loopback file server (`LoopbackFileServer.cs`) over its `wwwroot/` fixture app.

---

## How to run / Nasıl çalıştırılır

This sample launches a real headless Chromium via the `Microsoft.Playwright` .NET SDK.
If browsers aren't already cached on your machine, do a one-time download first (after
a Debug build): `pwsh samples/WebObservationQuickstart/bin/Debug/net8.0/playwright.ps1 install chromium`.
/ Bu örnek, `Microsoft.Playwright` .NET SDK'sı üzerinden gerçek bir headless Chromium
başlatır. Tarayıcılar makinenizde önbelleğe alınmamışsa, önce (bir Debug build sonrası)
tek seferlik bir indirme yapın: `pwsh samples/WebObservationQuickstart/bin/Debug/net8.0/playwright.ps1 install chromium`.

Single command from repository root / Depo kökünden tek bir komutla:

```bash
dotnet run --project samples/WebObservationQuickstart
```

Or via PowerShell verification script / Veya PowerShell doğrulama betiği ile:

```powershell
pwsh ./samples/WebObservationQuickstart/verify.ps1
```
