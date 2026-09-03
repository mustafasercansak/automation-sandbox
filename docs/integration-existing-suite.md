---
layout: default
title: Adding Self-Healing to an Existing Test Suite - Automation Sandbox
---

# Adding Self-Healing to an Existing Test Suite / Mevcut Bir Test Paketine Self-Healing Ekleme

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

The [Published Package Quickstart](consumer-quickstart.md) takes you from `dotnet add package` to a first
persisted heal on a synthetic tree. This guide is the next step: you already have a suite — dozens or hundreds of
tests, a runner you are committed to, locators that break on every refactor — and you want healing *added to it*
with the smallest possible change and no surprise behaviour.

### 1. Where the engine sits

Automation Sandbox does not run your tests, assert for you, or own your test lifecycle. It wraps **one thing**: the
step where you locate an element and act on it. Everywhere your test currently does

```
find(locator) → act(element)
```

you instead call `ExecuteWithHealingAsync`, which does `find → act`, and *only if that throws a
locator-resolution error* captures the live tree, scores candidates, and (depending on mode) retries.

```csharp
await engine.ExecuteWithHealingAsync<bool>(
    locatorKey:      "Checkout.SubmitButton",       // stable key you choose; identifies this locator in the repo + reports
    expected:        storedSnapshot,                 // the UiElementInfo you captured when the test was written
    action:          async el => { await ClickAsync(el); return true; },  // your existing click/type/read
    captureTreeRoot: () => CaptureLiveTree());        // your backend's "snapshot the screen/DOM now" call
```

`ExecuteWithHealingAsync<T>` on the engine expects the action to return `Task<T>`. The `SelfHealing.Testing`
fixtures (below) add a non-generic overload for void actions, so in a test you usually write
`el => ClickAsync(el)` directly.

Three things you own and bring: a **locator key naming scheme**, a **stored snapshot** per locator, and the two
callbacks that bind to your backend (`action` and `captureTreeRoot`). Everything else is the engine.

### 2. Choose a healing mode — and do not start at AutoHeal

`HealingMode` controls what happens after a confident candidate is found:

| Mode | On a broken locator | Persists? | Retries the action? | Use it for |
| :--- | :--- | :---: | :---: | :--- |
| `Observe` | Records what it *would* have picked, then rethrows | No | No | **Week 1.** Your test still fails; you get a healing report showing the candidate and its score. |
| `Review` (shipped default) | Routes the candidate to report telemetry, then rethrows | No | No | A suite where a human approves every heal out-of-band. |
| `AutoHeal` | Retries the action with the candidate; persists the new locator **only if the retry passes** | Yes (on retry success) | Yes | Steady state, after calibration. |
| `FailClosed` | Does nothing, rethrows immediately | No | No | High-consequence runs where a heal must never be attempted. |

> `Observe` and `Review` **do not make a broken test pass** — they add diagnostic telemetry to the failure. Only
> `AutoHeal` retries. This is deliberate: you see the engine's judgement on real breakages before you let it act.

### 3. The first-week rollout

1. **Wire one test in `Observe` mode.** Pick a test that breaks often. Wrap its locate-and-act step. Run the suite.
2. **Read the healing report.** Every attempt writes a schema-v8 JSON record and an HTML view — the candidate, the
   per-component score breakdown (control type, parent, sibling position, name, geometry), the runner-up, the
   evidence coverage. See [Healing Reports](healing-reports.md).
3. **Calibrate against your app.** Capture one representative tree and run the calibrator — it sweeps synthetic
   renames, drifts, shifts and removals against *your* structure and recommends a threshold profile:

   ```bash
   dotnet run --project samples/CalibrationCli -- your-app-tree.json --app YourApp
   ```

   UI structure matters: a dense grid-heavy app and a sparse form behave very differently
   ([why](benchmark-calibration.md#8-a-second-application-sharex-v2100-99-134)).
4. **Widen to more tests, still in `Observe`.** Let it run for a few CI cycles. Confirm the candidates it proposes
   on real breakages are the ones you would have picked by hand.
5. **Promote to `AutoHeal` with the calibrated profile.** Now heals are applied and persisted on retry success.
   Keep the healing report artifact published in CI so every applied heal stays auditable.
6. **Keep the locator repository in source control.** It is readable JSON you own. Review its diffs like code — an
   unexpected heal shows up as a repository change in a PR.

### 4. Runner-specific wiring

The engine is runner-agnostic; only the fixture plumbing differs. `SelfHealing.Testing` ships helpers for the two
xUnit/NUnit shapes so you are not managing temp repository files by hand.

#### Playwright (.NET)

Your `action` closure runs Playwright calls against the resolved element; `captureTreeRoot` produces a live DOM
snapshot via `AutomationSandbox.WebDiscovery` (`PlaywrightApplicationConnector` from an already-open page) or
`AutomationSandbox.PlaywrightLiveExploration` (`PlaywrightLiveExplorer.CaptureAsync(url)`).
`PlaywrightLocatorEmitter.Suggest(element)` turns a resolved node back into a ranked list of Playwright locators.
The maintained [`samples/PlaywrightEndToEndQuickstart`](https://github.com/mustafasercansak/automation-sandbox/tree/main/samples/PlaywrightEndToEndQuickstart)
is the exact end-to-end wiring.

```csharp
// before: a bare Playwright locator that breaks when the selector changes
await page.Locator("#btn-submit").ClickAsync();

// after: same click, healed if the selector no longer resolves
await engine.ExecuteWithHealingAsync<bool>(
    "Checkout.Submit",
    storedSnapshot,
    async el =>
    {
        var selector = PlaywrightLocatorEmitter.Suggest(el)[0].Expression;
        await page.Locator(selector).ClickAsync();
        return true;
    },
    () => CaptureDomSnapshot(page));   // your WebDiscovery-backed snapshot call
```

#### NUnit

```csharp
using NUnit.Framework;
using SelfHealing.Testing;

[TestFixture]
public class CheckoutTests : SelfHealingTestBase   // brings a temp repo + engine, disposed for you
{
    [Test]
    public async Task Submits_the_order()
    {
        await ExecuteWithHealingAsync(
            "Checkout.Submit", storedSnapshot,
            el => ClickAsync(el), () => CaptureTree());   // Func<UiElementInfo, Task>
    }
}
```

#### xUnit

```csharp
using SelfHealing.Testing;

public class CheckoutTests : IClassFixture<SelfHealingTestFixture>
{
    private readonly SelfHealingTestFixture _healing;
    public CheckoutTests(SelfHealingTestFixture healing) => _healing = healing;

    [Fact]
    public async Task Submits_the_order() =>
        await _healing.ExecuteWithHealingAsync(
            "Checkout.Submit", storedSnapshot,
            el => ClickAsync(el), () => CaptureTree());   // Func<UiElementInfo, Task>
}
```

#### Reqnroll / SpecFlow

Resolve one `SelfHealingEngine` in your DI container (or a `[BeforeScenario]` hook) and call it from step
definitions. The `locatorKey` is a natural fit for the step's target — `"LoginPage.Username"`,
`"Checkout.SubmitButton"` — so healing reports read in the same vocabulary as your `.feature` files.

```csharp
[Binding]
public class CheckoutSteps
{
    private readonly SelfHealingEngine _engine;
    public CheckoutSteps(SelfHealingEngine engine) => _engine = engine;

    [When("the customer submits the order")]
    public async Task WhenTheCustomerSubmitsTheOrder() =>
        await _engine.ExecuteWithHealingAsync<bool>(
            "Checkout.SubmitButton", _snapshots.Checkout.Submit,
            async el => { await _driver.ClickAsync(el); return true; },
            () => _driver.CaptureTree());
}
```

#### Desktop (FlaUI / UI Automation)

Identical shape; `captureTreeRoot` comes from `AutomationSandbox.Discovery` on a Windows host, and the resolved
`UiElementInfo` drives your FlaUI `AutomationElement` lookup. See [Desktop Automation](desktop-automation.md).

### 5. Deleted elements — repository ownership reconciliation

The failure mode the engine cannot solve with a single locator's structure alone: an element is
**deleted**, and the next-best structural match is a *different* control the suite already tests. The
scorer sees a strong parent/type/position match and heals onto it — a false green.

`SelfHealingEngine` has an opt-in guard for the common shape of this. Turn it on with
`reconcileAgainstRepository: true`:

```csharp
var engine = SelfHealingEngine.Create(
    ThresholdProfile.Balanced,
    repository: repo,
    mode: HealingMode.AutoHeal,
    reconcileAgainstRepository: true);
```

Before accepting a confident match, the engine re-resolves every *other* locator in the repository
against the same captured tree. If the candidate it wants is already the confident identity of
another locator, the heal is declined (`HealResolutionStatus.OwnershipConflict`) and routed to
review. It is heuristic-only — no extra LLM calls — and costs one resolution per other repository
entry, only on a heal attempt.

The `Balanced` and `Conservative` profiles also apply two per-component gates the weighted score
cannot override: a **name gate** (a named locator's candidate must clear a `NameScore` floor) and a
**descendant gate** (a container locator's candidate must still hold the same direct child control
types — this is what catches an unnamed panel healing onto a structurally identical sibling that
holds something else).

Measured on the project's two real fixtures (Balanced profile, every locator deleted in turn):
deleted-element false heals drop from **40 % → 0 %** (HandBrake) and **28 % → 0 %** (ShareX), with
**no change** to genuine-rename recall
([the measurement](benchmark-calibration.md#15-a-non-structural-signal-for-the-uncontested-residual-375)).
All three guards are on by default in `Balanced` and `Conservative`; `reconcileAgainstRepository`
is the one that also needs the engine flag above.

### 6. Common questions

- **Does this replace my Page Objects?** No. A Page Object still exposes `SubmitButton`; its *implementation*
  routes through `ExecuteWithHealingAsync` instead of a raw locator call.
- **What about a deleted element?** Turn on repository ownership reconciliation (Section 5)
  — it declines the most common shape (healing onto another test's control) at no recall cost. Past that it is
  [a hard, measured limit](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97),
  not a solved problem: keep `AutoHeal` paired with a published healing report so a wrong heal is visible, and
  consider the `Conservative` profile for suites where a false-green run is expensive.
- **Does anything get sent to an LLM?** Only if you configure a provider *and* the heuristic is not confident, and
  then only a bounded top-N shortlist with [PII/secret redaction on by default](llm-security-model.md). No provider,
  no network calls.

---

## 🇹🇷 Türkçe Kılavuz

[Yayımlanmış Paket Hızlı Başlangıcı](consumer-quickstart.md) sizi `dotnet add package`'ten sentetik bir ağaç
üzerindeki ilk kalıcı iyileştirmeye götürür. Bu rehber bir sonraki adımdır: zaten bir paketiniz var — onlarca ya
da yüzlerce test, bağlı kaldığınız bir koşucu, her refactor'da kırılan locator'lar — ve iyileştirmeyi mümkün olan
en küçük değişiklikle ve sürpriz davranış olmadan *ona eklemek* istiyorsunuz.

### 1. Motor nerede durur

Automation Sandbox testlerinizi çalıştırmaz, sizin adınıza doğrulama yapmaz ve test yaşam döngünüzü sahiplenmez.
**Tek bir şeyi** sarar: bir elemanı bulup üzerinde işlem yaptığınız adımı. Testinizin şu an yaptığı her yerde

```
find(locator) → act(element)
```

bunun yerine `ExecuteWithHealingAsync` çağırırsınız; bu, `find → act` yapar ve *yalnızca bu bir
locator-çözümleme hatası fırlatırsa* canlı ağacı yakalar, adayları skorlar ve (moda bağlı olarak) yeniden dener.

```csharp
await engine.ExecuteWithHealingAsync<bool>(
    locatorKey:      "Checkout.SubmitButton",       // seçtiğiniz kararlı anahtar; bu locator'ı depoda + raporlarda tanımlar
    expected:        storedSnapshot,                 // test yazıldığında yakaladığınız UiElementInfo
    action:          async el => { await ClickAsync(el); return true; },  // mevcut click/type/read çağrınız
    captureTreeRoot: () => CaptureLiveTree());        // backend'inizin "ekranı/DOM'u şimdi yakala" çağrısı
```

Motordaki `ExecuteWithHealingAsync<T>`, action'ın `Task<T>` döndürmesini bekler. Aşağıdaki `SelfHealing.Testing`
fikstürleri, void action'lar için jenerik olmayan bir aşırı yükleme ekler; bu yüzden bir testte genellikle
doğrudan `el => ClickAsync(el)` yazarsınız.

Sahiplendiğiniz ve getirdiğiniz üç şey: bir **locator anahtarı adlandırma şeması**, locator başına bir **kayıtlı
snapshot** ve backend'inize bağlanan iki geri çağrı (`action` ve `captureTreeRoot`). Geri kalan her şey motordur.

### 2. Bir healing modu seçin — ve AutoHeal ile başlamayın

`HealingMode`, güvenli bir aday bulunduktan sonra ne olacağını kontrol eder:

| Mod | Kırık bir locator'da | Kalıcılaştırır mı? | Action'ı yeniden dener mi? | Ne için |
| :--- | :--- | :---: | :---: | :--- |
| `Observe` | Ne *seçecek olduğunu* kaydeder, sonra yeniden fırlatır | Hayır | Hayır | **1. Hafta.** Testiniz yine başarısız olur; adayı ve skorunu gösteren bir healing raporu alırsınız. |
| `Review` (kutudan çıkan varsayılan) | Adayı rapor telemetrisine yönlendirir, sonra yeniden fırlatır | Hayır | Hayır | Her iyileştirmeyi bir insanın harici olarak onayladığı bir paket. |
| `AutoHeal` | Action'ı adayla yeniden dener; yeni locator'ı **yalnızca yeniden deneme başarılıysa** kalıcılaştırır | Evet (yeniden deneme başarısında) | Evet | Kalibrasyondan sonra kararlı durum. |
| `FailClosed` | Hiçbir şey yapmaz, hemen yeniden fırlatır | Hayır | Hayır | Bir iyileştirmenin asla denenmemesi gereken yüksek sonuçlu koşular. |

> `Observe` ve `Review` **kırık bir testi geçirmez** — hataya tanısal telemetri ekler. Yalnızca `AutoHeal` yeniden
> dener. Bu bilinçlidir: motorun harekete geçmesine izin vermeden önce, gerçek kırılmalardaki kararını görürsünüz.

### 3. İlk hafta yaygınlaştırması

1. **Bir testi `Observe` modunda bağlayın.** Sık kırılan bir test seçin. Bul-ve-uygula adımını sarın. Paketi
   çalıştırın.
2. **Healing raporunu okuyun.** Her deneme bir şema-v8 JSON kaydı ve bir HTML görünümü yazar — aday, bileşen
   bazlı skor dökümü (kontrol türü, ebeveyn, kardeş konumu, isim, geometri), ikinci aday, kanıt kapsamı. Bkz.
   [Healing Raporları](healing-reports.md).
3. **Uygulamanıza göre kalibre edin.** Temsili bir ağaç yakalayın ve kalibratörü çalıştırın — *sizin* yapınıza
   karşı sentetik yeniden adlandırmalar, kaymalar ve silmeler tarar ve bir eşik profili önerir:

   ```bash
   dotnet run --project samples/CalibrationCli -- your-app-tree.json --app YourApp
   ```

   UI yapısı önemlidir: yoğun tablo ağırlıklı bir uygulama ile seyrek bir form çok farklı davranır
   ([neden](benchmark-calibration.md#8-a-second-application-sharex-v2100-99-134)).
4. **Hâlâ `Observe` modunda daha fazla teste yayın.** Birkaç CI döngüsü çalışmasına izin verin. Gerçek
   kırılmalarda önerdiği adayların, elle seçeceğiniz adaylar olduğunu doğrulayın.
5. **Kalibre edilmiş profille `AutoHeal`'e yükseltin.** Artık iyileştirmeler uygulanır ve yeniden deneme
   başarısında kalıcılaştırılır. Her uygulanan iyileştirmenin denetlenebilir kalması için healing raporu
   artifact'ını CI'da yayımlanmış tutun.
6. **Locator deposunu kaynak kontrolünde tutun.** Sahip olduğunuz okunabilir JSON'dur. Diff'lerini kod gibi
   inceleyin — beklenmedik bir iyileştirme, bir PR'de depo değişikliği olarak görünür.

### 4. Koşucuya özel bağlama

Motor koşucudan bağımsızdır; yalnızca fikstür tesisatı değişir. `SelfHealing.Testing`, geçici depo dosyalarını
elle yönetmemeniz için iki xUnit/NUnit şekli için yardımcılar sunar.

#### Playwright (.NET)

`action` closure'ınız, çözülen eleman üzerinde Playwright çağrıları çalıştırır; `captureTreeRoot`,
`AutomationSandbox.WebDiscovery` (zaten açık bir sayfadan `PlaywrightApplicationConnector`) veya
`AutomationSandbox.PlaywrightLiveExploration` (`PlaywrightLiveExplorer.CaptureAsync(url)`) aracılığıyla canlı bir
DOM snapshot'ı üretir. `PlaywrightLocatorEmitter.Suggest(element)`, çözülen bir düğümü sıralı bir Playwright
locator listesine geri çevirir. Sürdürülen
[`samples/PlaywrightEndToEndQuickstart`](https://github.com/mustafasercansak/automation-sandbox/tree/main/samples/PlaywrightEndToEndQuickstart)
tam uçtan uca bağlamadır.

```csharp
// önce: seçici değiştiğinde kırılan çıplak bir Playwright locator'ı
await page.Locator("#btn-submit").ClickAsync();

// sonra: aynı click, seçici artık çözülmüyorsa iyileştirilir
await engine.ExecuteWithHealingAsync<bool>(
    "Checkout.Submit",
    storedSnapshot,
    async el =>
    {
        var selector = PlaywrightLocatorEmitter.Suggest(el)[0].Expression;
        await page.Locator(selector).ClickAsync();
        return true;
    },
    () => CaptureDomSnapshot(page));   // WebDiscovery destekli snapshot çağrınız
```

#### NUnit

```csharp
using NUnit.Framework;
using SelfHealing.Testing;

[TestFixture]
public class CheckoutTests : SelfHealingTestBase   // sizin için oluşturulup dispose edilen geçici bir repo + engine getirir
{
    [Test]
    public async Task Submits_the_order()
    {
        await ExecuteWithHealingAsync(
            "Checkout.Submit", storedSnapshot,
            el => ClickAsync(el), () => CaptureTree());   // Func<UiElementInfo, Task>
    }
}
```

#### xUnit

```csharp
using SelfHealing.Testing;

public class CheckoutTests : IClassFixture<SelfHealingTestFixture>
{
    private readonly SelfHealingTestFixture _healing;
    public CheckoutTests(SelfHealingTestFixture healing) => _healing = healing;

    [Fact]
    public async Task Submits_the_order() =>
        await _healing.ExecuteWithHealingAsync(
            "Checkout.Submit", storedSnapshot,
            el => ClickAsync(el), () => CaptureTree());   // Func<UiElementInfo, Task>
}
```

#### Reqnroll / SpecFlow

DI konteynerinizde (veya bir `[BeforeScenario]` hook'unda) tek bir `SelfHealingEngine` çözün ve onu adım
tanımlarından çağırın. `locatorKey`, adımın hedefine doğal olarak oturur — `"LoginPage.Username"`,
`"Checkout.SubmitButton"` — böylece healing raporları `.feature` dosyalarınızla aynı sözcük dağarcığıyla okunur.

```csharp
[Binding]
public class CheckoutSteps
{
    private readonly SelfHealingEngine _engine;
    public CheckoutSteps(SelfHealingEngine engine) => _engine = engine;

    [When("the customer submits the order")]
    public async Task WhenTheCustomerSubmitsTheOrder() =>
        await _engine.ExecuteWithHealingAsync<bool>(
            "Checkout.SubmitButton", _snapshots.Checkout.Submit,
            async el => { await _driver.ClickAsync(el); return true; },
            () => _driver.CaptureTree());
}
```

#### Masaüstü (FlaUI / UI Automation)

Aynı şekil; `captureTreeRoot` bir Windows makinesinde `AutomationSandbox.Discovery`'den gelir ve çözülen
`UiElementInfo`, FlaUI `AutomationElement` aramanızı sürer. Bkz. [Masaüstü Otomasyonu](desktop-automation.md).

### 5. Silinen elemanlar — depo sahiplik uzlaştırması

Motorun tek bir locator'ın yapısıyla çözemediği başarısızlık modu: bir eleman **siliniyor** ve bir sonraki en iyi
yapısal eşleşme, paketin zaten test ettiği *farklı* bir kontrol. Skorlayıcı güçlü bir ebeveyn/tür/konum eşleşmesi
görür ve ona iyileşir — yanlış bir yeşil.

`SelfHealingEngine`'in bunun yaygın şekli için isteğe bağlı bir koruması vardır.
`reconcileAgainstRepository: true` ile açın:

```csharp
var engine = SelfHealingEngine.Create(
    ThresholdProfile.Balanced,
    repository: repo,
    mode: HealingMode.AutoHeal,
    reconcileAgainstRepository: true);
```

Güvenli bir eşleşmeyi kabul etmeden önce, motor depodaki *diğer tüm* locator'ları aynı yakalanmış ağaca karşı
yeniden çözer. İstediği aday zaten başka bir locator'ın güvenli kimliğiyse, iyileştirme reddedilir
(`HealResolutionStatus.OwnershipConflict`) ve incelemeye yönlendirilir. Yalnızca heuristiktir — ek LLM çağrısı yok
— ve yalnızca bir iyileştirme denemesinde, diğer her depo girdisi başına bir çözümlemeye mal olur.

`Balanced` ve `Conservative` profilleri ayrıca ağırlıklı skorun geçersiz kılamadığı iki bileşen bazlı kapı
uygular: bir **isim geçidi** (isimli bir locator'ın adayı bir `NameScore` tabanını aşmalıdır) ve bir **alt öğe
geçidi** (bir konteyner locator'ının adayı aynı doğrudan alt öğe kontrol türlerini hâlâ tutmalıdır — isimsiz bir
panelin, başka bir şey tutan yapısal olarak özdeş bir kardeşe iyileşmesini yakalayan budur).

Projenin iki gerçek fikstüründe ölçüldü (Balanced profil, her locator sırayla siliniyor): silinen-eleman yanlış
iyileştirmeleri **%40 → %0** (HandBrake) ve **%28 → %0** (ShareX) düşüyor, gerçek yeniden adlandırma geri
çağırmasında **değişiklik olmadan**
([ölçüm](benchmark-calibration.md#15-a-non-structural-signal-for-the-uncontested-residual-375)). Üç korumanın
tümü `Balanced` ve `Conservative`'de varsayılan açıktır; yukarıdaki motor bayrağına da ihtiyaç duyan
`reconcileAgainstRepository`'dir.

### 6. Sık sorulan sorular

- **Bu, Page Object'lerimin yerini alır mı?** Hayır. Bir Page Object hâlâ `SubmitButton`'ı sunar; *uygulaması*
  çıplak bir locator çağrısı yerine `ExecuteWithHealingAsync` üzerinden geçer.
- **Silinen bir eleman ne olacak?** Depo sahiplik uzlaştırmasını açın (5. Bölüm) — en yaygın şekli (başka bir
  testin kontrolüne iyileşme) geri çağırma maliyeti olmadan reddeder. Bunun ötesinde
  [sert, ölçülmüş bir sınırdır](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97),
  çözülmüş bir problem değil: bir yanlış iyileştirmenin görünür olması için `AutoHeal`'i yayımlanmış bir healing
  raporuyla eşleştirin ve yanlış-yeşil bir koşunun pahalı olduğu paketler için `Conservative` profilini düşünün.
- **Bir LLM'e bir şey gönderilir mi?** Yalnızca bir sağlayıcı yapılandırırsanız *ve* heuristik güvenli değilse; o
  zaman da yalnızca [PII/gizli bilgi maskelemesi varsayılan açık](llm-security-model.md) sınırlı bir top-N kısa
  liste. Sağlayıcı yoksa ağ çağrısı yoktur.

---

## See also / Ayrıca bakınız

- [Published Package Quickstart](consumer-quickstart.md) · [Getting Started](getting-started.md)
- [Benchmark & Calibration](benchmark-calibration.md) — choosing a threshold profile for your app
- [Healing Reports & Dashboard](healing-reports.md) — reading the per-decision audit trail
- [Documentation Hub](index.md) — including how this compares with other healers
