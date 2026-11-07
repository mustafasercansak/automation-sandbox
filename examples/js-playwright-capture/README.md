# JS/Playwright Capture Example / JS/Playwright Yakalama Örneği

A reference Node.js + [Playwright](https://playwright.dev/) script that captures a web page's DOM as `UiElementInfo`-shaped JSON - the exact input `automation-sandbox-heal` (`AutomationSandbox.Cli`, see `TestAutomation/SelfHealingCli/`) expects. It exists so a non-.NET caller doesn't have to reverse-engineer the JSON shape from C# source: the mapping rules here mirror `TestAutomation/WebDiscovery`'s own role/name/control-type detection, so a tree captured here scores consistently with a .NET-captured one.

Gerçek bir Node.js + [Playwright](https://playwright.dev/) betiği, bir web sayfasının DOM'unu `automation-sandbox-heal`'in (`AutomationSandbox.Cli`, bkz. `TestAutomation/SelfHealingCli/`) beklediği tam JSON şeklinde (`UiElementInfo`) yakalar. Amacı, .NET dışı bir çağıranın bu JSON şeklini C# kaynak kodundan tersine mühendislikle çıkarmak zorunda kalmaması: buradaki eşleme kuralları `TestAutomation/WebDiscovery`'nin kendi rol/isim/kontrol-tipi tespitini yansıtır, böylece burada yakalanan bir ağaç .NET tarafında yakalanmış biriyle tutarlı puanlanır.

---

**Deliberately minimal - not a port of `AutomationSandbox.WebDiscovery`.** Covers the regular DOM only: no open Shadow DOM traversal, no same-origin iframe traversal, no `MaxElements` bounding, and a fixed depth bound of 100 nested elements (a deeper page is cut there with a warning, so the file stays within the 256 levels `automation-sandbox-heal` reads, #638). For those, capture from .NET instead.

**Bilinçli olarak minimal - `AutomationSandbox.WebDiscovery`'nin bir portu değil.** Sadece normal DOM'u kapsar: açık Shadow DOM gezinmesi yok, aynı-kaynaklı iframe gezinmesi yok, `MaxElements` sınırlaması yok ve 100 iç içe elemanlık sabit bir derinlik sınırı var (daha derin bir sayfa uyarıyla orada kesilir, böylece dosya `automation-sandbox-heal`'in okuduğu 256 seviyenin içinde kalır, #638). Bunlar için .NET tarafından yakalayın.

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

Use a browser that is already installed instead of the one `playwright install` downloads (a Chromium of another revision, Chrome, Edge) / `playwright install` ile indirilen yerine zaten kurulu bir tarayıcı kullanın (başka bir revizyonun Chromium'u, Chrome, Edge):

```bash
node capture.mjs https://example.com tree.json --channel chrome
node capture.mjs https://example.com tree.json --executable-path "C:\Program Files\Chromium\chrome.exe"
```

`--executable-path` and `--channel` cannot be combined / `--executable-path` ve `--channel` birlikte kullanılamaz.

A local HTML file path works too, no need to construct a `file://` URL by hand / Yerel bir HTML dosya yolu da çalışır, elle `file://` URL'i oluşturmaya gerek yok:

```bash
node capture.mjs fixtures/page-old.html expected-button.json --selector "#btn-submit-old"
```

Then heal with the CLI / Sonra CLI ile healing yapın:

```bash
automation-sandbox-heal --expected expected-button.json --tree tree.json --profile balanced
```

## Heal from a failing `@playwright/test` test / Düşen bir `@playwright/test` testinden healing

`heal-fixture.mjs` prints a heal suggestion when a test fails waiting for a locator; `heal-helper.mjs` is the same logic as a plain function; `npm run heal:demo` runs it against the bundled pages. See the "Wiring It Into a TypeScript Playwright Suite" section of [docs/cli-and-ci-integration.md](../../docs/cli-and-ci-integration.md) / `heal-fixture.mjs`, bir test locator beklerken düşerse healing önerisi yazdırır; `heal-helper.mjs` aynı mantığın düz fonksiyon halidir; `npm run heal:demo` onu birlikte gelen sayfalara karşı çalıştırır. Ayrıntılar için [docs/cli-and-ci-integration.md](../../docs/cli-and-ci-integration.md) içindeki "TypeScript Playwright Suite'ine Bağlamak" bölümüne bakın.

## Try it against the bundled fixture / Birlikte gelen fixture'a karşı deneyin

Zero network dependency - two tiny HTML pages where a button's `id` changed, the classic broken-locator scenario / Sıfır ağ bağımlılığı - bir butonun `id`'sinin değiştiği, klasik kırık-locator senaryosunu temsil eden iki küçük HTML sayfası:

```bash
npm run capture:old   # fixtures/page-old.html -> expected-button.json (btn-submit-old)
npm run capture:new   # fixtures/page-new.html -> tree.json (renamed to btn-submit-new)
automation-sandbox-heal --expected expected-button.json --tree tree.json
```

This exact flow was run live (real headless Chromium, real `automation-sandbox-heal`) before this example was committed: `IsConfident: true`, healed `AutomationId: btn-submit-new`. Both captured files also validate against the published [JSON Schema](https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json) with zero errors.

Bu tam akış, bu örnek commit edilmeden önce canlı olarak çalıştırıldı (gerçek headless Chromium, gerçek `automation-sandbox-heal`): `IsConfident: true`, healed `AutomationId: btn-submit-new`. Her iki yakalanan dosya da yayınlanmış [JSON Schema](https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json)'ya karşı sıfır hatayla doğrulanıyor.

## Reproduce the organic drift dataset / Doğal değişim veri setini yeniden üretin

`organic-drift-capture.mjs` (#663) downloads the pinned releases listed in a manifest (Swagger UI 4.19.1 and 5.17.14 for the committed dataset), refuses them unless their SHA-256 and npm integrity match, replays the manifest's click steps on a probe page and writes one tree per state. Nothing is renamed or removed; the output is what the browser reported. From the repository root, after `npm install` here / Manifest'te listelenen sabitlenmiş sürümleri (commit edilen veri seti için Swagger UI 4.19.1 ve 5.17.14) indirir, SHA-256 ve npm bütünlüğü eşleşmezse reddeder, probe sayfasında manifest'teki tıklama adımlarını uygular ve her durum için bir ağaç yazar. Hiçbir şey yeniden adlandırılmaz veya silinmez; çıktı tarayıcının bildirdiğidir. Depo kökünden, burada `npm install` yaptıktan sonra:

```bash
node organic-drift-capture.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/manifest.json ./out --channel chrome
diff -r ./out/trees ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/trees   # no output = identical
```

`locator-survival.mjs` (#670) uses the same manifest and the same hash check to measure which of ten common Playwright locator styles survive the upgrade between the two releases: which still find the control, which fail loudly, and which silently bind to a different element. The write-up is [docs/blog/playwright-locator-survival.md](../../docs/blog/playwright-locator-survival.md) / Aynı manifesti ve aynı hash kontrolünü kullanarak, on yaygın Playwright locator stilinden hangilerinin iki sürüm arasındaki yükseltmede hayatta kaldığını ölçer: hangileri kontrolü hâlâ bulur, hangileri açık hata verir, hangileri sessizce başka bir elemana bağlanır. Yazı: [docs/blog/playwright-locator-survival.md](../../docs/blog/playwright-locator-survival.md):

```bash
node locator-survival.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/manifest.json --channel chrome
```

`xpath-survival.mjs` (#670) does the same for XPath: seven styles aimed at one button, over four consecutive Swagger UI releases pinned by hash in `xpath-manifest.json`, each release compared with the next / Aynısını XPath için yapar: tek bir butona yönelen yedi stil, `xpath-manifest.json` içinde hash ile sabitlenmiş art arda dört Swagger UI sürümünde, her sürüm bir sonrakiyle karşılaştırılır:

```bash
node xpath-survival.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/xpath-manifest.json --channel chrome
```

## Why this one `package.json` exists / Bu tek `package.json` neden var

The main repository is deliberately all-C#, with no JavaScript/Python/Rust tooling (see `AGENTS.md`) - `PlaywrightLiveExploration` wraps the `Microsoft.Playwright` .NET SDK precisely so the product itself never needs Node.js at runtime. This example is the one narrow, explicit exception: it exists specifically to serve callers who are *not* using .NET, so it necessarily needs its own Node.js tooling. It does not change anything about the product's own runtime dependencies.

Ana depo bilinçli olarak tamamen C#'tır, JavaScript/Python/Rust araçları yoktur (bkz. `AGENTS.md`) - `PlaywrightLiveExploration`, `Microsoft.Playwright` .NET SDK'sını tam olarak bu yüzden sarmalar: ürünün kendisi çalışma zamanında asla Node.js'e ihtiyaç duymaz. Bu örnek, bu kuralın tek, dar ve açıkça belirtilmiş istisnasıdır: özellikle .NET *kullanmayan* çağıranlara hizmet etmek için var, bu yüzden kendi Node.js araçlarına ihtiyaç duyuyor. Ürünün kendi çalışma zamanı bağımlılıkları konusunda hiçbir şeyi değiştirmiyor.
