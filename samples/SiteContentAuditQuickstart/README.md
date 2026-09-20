# Site Content Audit Quickstart / Site İçerik Denetimi Hızlı Başlangıç

This .NET 8 console sample points `SiteCrawler` at a URL, runs `ContentAnalyzer` over every page it visits, and writes the accumulated findings through `ContentAnalysisReportFileSink` to a JSON Lines log plus an HTML dashboard — the full "give it a URL, find the writing mistakes, report them" flow end to end.

Bu .NET 8 console örneği, `SiteCrawler`'ı bir URL üzerinde çalıştırır, ziyaret ettiği her sayfada `ContentAnalyzer`'ı işletir ve biriken bulguları `ContentAnalysisReportFileSink` ile bir JSON Lines günlüğüne ve bir HTML panosuna yazar — "bir URL ver, yazım hatalarını bul, raporla" akışının uçtan uca hâli.

---

## What this scenario demonstrates / Bu senaryo neleri gösterir

1. **Crawl**: `SiteCrawler.CrawlAsync` walks the site breadth-first from one starting URL, staying same-origin by default and bounded by `SiteCrawlOptions`.
2. **Analyze**: each captured page runs through `ContentAnalyzer.AnalyzeAsync` (heuristics always; add `ClaudeContentAnalysisProvider` for spelling/grammar/meaning review when `ANTHROPIC_API_KEY` is set).
3. **Report**: every page's findings are recorded via `ContentAnalysisReportFileSink` into `site-content-audit-report.json` (append-only JSON Lines) and `site-content-audit-report.html` (a human-readable dashboard), written to the current working directory.
4. **Resilience**: a page that fails to navigate or capture is recorded in `SiteCrawlResult.Failures` rather than aborting the whole crawl.

With no arguments, the sample audits a tiny bundled 3-page fixture site (`wwwroot/`) with planted findings (a leftover `TODO`, a duplicated word, repeated punctuation) and an off-origin link, and verifies all of them were caught — this is the mode CI runs, so the check stays network-independent and deterministic.

---

## How to run / Nasıl çalıştırılır

This sample launches a real headless Chromium via the `Microsoft.Playwright` .NET SDK.
If browsers aren't already cached on your machine, do a one-time download first (after
a Debug build): `pwsh samples/SiteContentAuditQuickstart/bin/Debug/net8.0/playwright.ps1 install chromium`.
/ Bu örnek, `Microsoft.Playwright` .NET SDK'sı üzerinden gerçek bir headless Chromium
başlatır. Tarayıcılar makinenizde önbelleğe alınmamışsa, önce (bir Debug build sonrası)
tek seferlik bir indirme yapın: `pwsh samples/SiteContentAuditQuickstart/bin/Debug/net8.0/playwright.ps1 install chromium`.

Audit the bundled fixture site (no arguments) / Ekli örnek siteyi denetler (argümansız):

```bash
dotnet run --project samples/SiteContentAuditQuickstart
```

Audit a real site by passing its URL / Gerçek bir siteyi URL'sini vererek denetleyin — for
example, this project's own published docs site / örneğin bu projenin kendi yayınlanmış
dokümantasyon sitesi:

```bash
dotnet run --project samples/SiteContentAuditQuickstart -- https://mustafasercansak.github.io/automation-sandbox/
```

Or point it at any other site you maintain / Ya da bakımını yaptığınız başka herhangi bir
siteye yönlendirin:

```bash
dotnet run --project samples/SiteContentAuditQuickstart -- https://your-site.example.com/
```

Optionally set `ANTHROPIC_API_KEY` first to add spelling/grammar/meaning review on top of the
always-on heuristics / İsteğe bağlı olarak her zaman çalışan sezgisel kontrollerin üzerine
yazım/dilbilgisi/anlam incelemesi eklemek için önce `ANTHROPIC_API_KEY`'i ayarlayın.

Or via PowerShell verification script (bundled fixture only) / Veya PowerShell doğrulama
betiği ile (yalnızca ekli örnek site):

```powershell
pwsh ./samples/SiteContentAuditQuickstart/verify.ps1
```
