---
layout: default
title: Adding Self-Healing to an Existing Test Suite - Automation Sandbox
---

# Adding Self-Healing to an Existing Test Suite

> **TR:** Bu rehber, sıfırdan değil, **hâlihazırda çalışan** bir Playwright / NUnit / xUnit / Reqnroll test paketine
> Automation Sandbox'ı nasıl ekleyeceğinizi anlatır. Önerilen yol: önce `Observe` modunda çalıştır, healing raporunu
> oku, `CalibrationCli` ile eşiği kendi uygulamana göre ayarla, sonra `AutoHeal`'e geç. İlk günden `AutoHeal` ile
> başlama.

The [Published Package Quickstart](consumer-quickstart.md) takes you from `dotnet add package` to a first
persisted heal on a synthetic tree. This guide is the next step: you already have a suite — dozens or hundreds of
tests, a runner you are committed to, locators that break on every refactor — and you want healing *added to it*
with the smallest possible change and no surprise behaviour.

---

## 1. Where the engine sits

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

---

## 2. Choose a healing mode — and do not start at AutoHeal

`HealingMode` controls what happens after a confident candidate is found:

| Mode | On a broken locator | Persists? | Retries the action? | Use it for |
| :--- | :--- | :---: | :---: | :--- |
| `Observe` | Records what it *would* have picked, then rethrows | No | No | **Week 1.** Your test still fails; you get a healing report showing the candidate and its score. |
| `Review` (shipped default) | Routes the candidate to report telemetry, then rethrows | No | No | A suite where a human approves every heal out-of-band. |
| `AutoHeal` | Retries the action with the candidate; persists the new locator **only if the retry passes** | Yes (on retry success) | Yes | Steady state, after calibration. |
| `FailClosed` | Does nothing, rethrows immediately | No | No | High-consequence runs where a heal must never be attempted. |

> `Observe` and `Review` **do not make a broken test pass** — they add diagnostic telemetry to the failure. Only
> `AutoHeal` retries. This is deliberate: you see the engine's judgement on real breakages before you let it act.

---

## 3. The first-week rollout

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

---

## 4. Runner-specific wiring

The engine is runner-agnostic; only the fixture plumbing differs. `SelfHealing.Testing` ships helpers for the two
xUnit/NUnit shapes so you are not managing temp repository files by hand.

### Playwright (.NET)

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

### NUnit

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

### xUnit

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

### Reqnroll / SpecFlow

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

### Desktop (FlaUI / UI Automation)

Identical shape; `captureTreeRoot` comes from `AutomationSandbox.Discovery` on a Windows host, and the resolved
`UiElementInfo` drives your FlaUI `AutomationElement` lookup. See [Desktop Automation](desktop-automation.md).

---

## 5. Common questions

- **Does this replace my Page Objects?** No. A Page Object still exposes `SubmitButton`; its *implementation*
  routes through `ExecuteWithHealingAsync` instead of a raw locator call.
- **What about a deleted element?** The engine is designed to decline rather than heal onto a neighbour — but this
  is [a hard, measured limit](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97),
  not a solved problem. Keep `AutoHeal` paired with a
  published healing report so a wrong heal is visible, and consider the `Conservative` profile for suites where a
  false-green run is expensive.
- **Does anything get sent to an LLM?** Only if you configure a provider *and* the heuristic is not confident, and
  then only a bounded top-N shortlist with [PII/secret redaction on by default](llm-security-model.md). No provider,
  no network calls.

---

## See also

- [Published Package Quickstart](consumer-quickstart.md) · [Getting Started](getting-started.md)
- [Benchmark & Calibration](benchmark-calibration.md) — choosing a threshold profile for your app
- [Healing Reports & Dashboard](healing-reports.md) — reading the per-decision audit trail
- [Documentation Hub](index.md) — including how this compares with other healers
