# JS/Playwright Capture Example / JS/Playwright Yakalama Örneği

A reference Node.js + [Playwright](https://playwright.dev/) script that captures a web page's DOM as `UiElementInfo`-shaped JSON - the exact input `automation-sandbox-heal` (`AutomationSandbox.Cli`, see `TestAutomation/SelfHealingCli/`) expects. It exists so a non-.NET caller doesn't have to reverse-engineer the JSON shape from C# source: the mapping rules here mirror `TestAutomation/WebDiscovery`'s own role/name/control-type detection, so a tree captured here scores consistently with a .NET-captured one.

Gerçek bir Node.js + [Playwright](https://playwright.dev/) betiği, bir web sayfasının DOM'unu `automation-sandbox-heal`'in (`AutomationSandbox.Cli`, bkz. `TestAutomation/SelfHealingCli/`) beklediği tam JSON şeklinde (`UiElementInfo`) yakalar. Amacı, .NET dışı bir çağıranın bu JSON şeklini C# kaynak kodundan tersine mühendislikle çıkarmak zorunda kalmaması: buradaki eşleme kuralları `TestAutomation/WebDiscovery`'nin kendi rol/isim/kontrol-tipi tespitini yansıtır, böylece burada yakalanan bir ağaç .NET tarafında yakalanmış biriyle tutarlı puanlanır.

---

**Deliberately minimal - not a port of `AutomationSandbox.WebDiscovery`.** Covers the regular DOM only: no open Shadow DOM traversal, no same-origin iframe traversal, no `MaxDepth`/`MaxElements` bounding. For those, capture from .NET instead.

**Bilinçli olarak minimal - `AutomationSandbox.WebDiscovery`'nin bir portu değil.** Sadece normal DOM'u kapsar: açık Shadow DOM gezinmesi yok, aynı-kaynaklı iframe gezinmesi yok, `MaxDepth`/`MaxElements` sınırlaması yok. Bunlar için .NET tarafından yakalayın.

---

## Install / Kurulum

```bash
cd examples/js-playwright-capture
npm install
npx playwright install chromium
```

## Use / Kullanım

Capture a full tree (for `--tree`) / Tam bir ağaç yakalayın (`--tree` için):

```bash
node capture.mjs https://example.com tree.json
```

Capture a single element (for `--expected`) with a CSS selector / Bir CSS seçiciyle tek bir eleman yakalayın (`--expected` için):

```bash
node capture.mjs https://example.com expected.json --selector "#submit-button"
```

A local HTML file path works too, no need to construct a `file://` URL by hand / Yerel bir HTML dosya yolu da çalışır, elle `file://` URL'i oluşturmaya gerek yok:

```bash
node capture.mjs fixtures/page-old.html expected-button.json --selector "#btn-submit-old"
```

Then heal with the CLI / Sonra CLI ile healing yapın:

```bash
automation-sandbox-heal --expected expected-button.json --tree tree.json --profile balanced
```

## Try it against the bundled fixture / Birlikte gelen fixture'a karşı deneyin

Zero network dependency - two tiny HTML pages where a button's `id` changed, the classic broken-locator scenario / Sıfır ağ bağımlılığı - bir butonun `id`'sinin değiştiği, klasik kırık-locator senaryosunu temsil eden iki küçük HTML sayfası:

```bash
npm run capture:old   # fixtures/page-old.html -> expected-button.json (btn-submit-old)
npm run capture:new   # fixtures/page-new.html -> tree.json (renamed to btn-submit-new)
automation-sandbox-heal --expected expected-button.json --tree tree.json
```

This exact flow was run live (real headless Chromium, real `automation-sandbox-heal`) before this example was committed: `IsConfident: true`, healed `AutomationId: btn-submit-new`. Both captured files also validate against the published [JSON Schema](https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json) with zero errors.

Bu tam akış, bu örnek commit edilmeden önce canlı olarak çalıştırıldı (gerçek headless Chromium, gerçek `automation-sandbox-heal`): `IsConfident: true`, healed `AutomationId: btn-submit-new`. Her iki yakalanan dosya da yayınlanmış [JSON Schema](https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json)'ya karşı sıfır hatayla doğrulanıyor.

## Why this one `package.json` exists / Bu tek `package.json` neden var

The main repository is deliberately all-C#, with no JavaScript/Python/Rust tooling (see `AGENTS.md`) - `PlaywrightLiveExploration` wraps the `Microsoft.Playwright` .NET SDK precisely so the product itself never needs Node.js at runtime. This example is the one narrow, explicit exception: it exists specifically to serve callers who are *not* using .NET, so it necessarily needs its own Node.js tooling. It does not change anything about the product's own runtime dependencies.

Ana depo bilinçli olarak tamamen C#'tır, JavaScript/Python/Rust araçları yoktur (bkz. `AGENTS.md`) - `PlaywrightLiveExploration`, `Microsoft.Playwright` .NET SDK'sını tam olarak bu yüzden sarmalar: ürünün kendisi çalışma zamanında asla Node.js'e ihtiyaç duymaz. Bu örnek, bu kuralın tek, dar ve açıkça belirtilmiş istisnasıdır: özellikle .NET *kullanmayan* çağıranlara hizmet etmek için var, bu yüzden kendi Node.js araçlarına ihtiyaç duyuyor. Ürünün kendi çalışma zamanı bağımlılıkları konusunda hiçbir şeyi değiştirmiyor.
