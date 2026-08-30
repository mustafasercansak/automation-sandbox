---
layout: default
title: Case Study — Healing HandBrake's Real UI Tree - Automation Sandbox
---

# Case Study: Pointing the Healer at HandBrake's Real UI Tree

> **TR:** Sentetik örnek değil — gerçek bir uygulamanın (HandBrake 1.8.2, 149 düğüm, 42 locator) canlı yakalanmış
> UI ağacında iki senaryo: (1) "Start Encode" butonunun `AutomationId`'si bir refactor'da değişiyor → motor 1.000
> skorla doğru iyileştiriyor; (2) "Summary" sekmesi tamamen siliniyor → **varsayılan eşikte motor yanlışlıkla
> komşu sekmeye (`pictureTab`) iyileştiriyor**. `CalibrationCli`'nin bu uygulama için önerdiği `Balanced` profiline
> (eşik 0.75) geçince silinen sekme senaryosu 0.744 < 0.75 olduğu için **reddediliyor ve insan incelemesine
> yönlendiriliyor**. Silinen eleman sorunu tamamen çözülmüş değil — bu yazı onu da dürüstçe gösteriyor.

All numbers below come from resolving against the committed fixture
[`HandBrake_1.8.2.tree.json`](https://github.com/mustafasercansak/automation-sandbox/blob/main/TestAutomation/ScenarioRunner/Fixtures/HandBrake_1.8.2.tree.json)
— a real capture of HandBrake 1.8.2's WPF window (149 nodes, 42 authored locators). Reproduce them with the
snippet in [§4](#4-reproduce-it).

---

## 1. The setup

HandBrake's window, captured as a `UiElementInfo` tree, contains controls like:

```
Button   'Start'       'Start Encode'
Button   'ShowQueue'   'Queue'
TabItem  'summaryTab'   'Summary'
TabItem  'pictureTab'   'Dimensions'
TabItem  'filtersTab'   'Filters'
...
```

Two refactors, each a real failure mode a UI test suite hits.

---

## 2. Scenario 1 — a control is renamed

A refactor changes the encode button's `AutomationId` from `Start` to an opaque generated value
(`ab7f1c93encode`). Everything else — control type, parent, sibling position, label, geometry — is unchanged.
The stored locator `Start` no longer resolves; the test would fail.

`SelfHealingResolver.Resolve(storedSnapshot, liveTree)` returns:

| | Value |
| :--- | :--- |
| Outcome | **healed** (`IsConfident = true`) |
| Score | **1.000** |
| Matched | `ab7f1c93encode` · "Start Encode" · Button |
| Runner-up | 0.709 (`"Add to Queue"`) |
| Component breakdown | control type `1.00` · parent `1.00` · sibling position `1.00` · name `1.00` · position `1.00` |

Every structural signal is a perfect match — only the identifier moved — so the score is exactly `1.000` and the
next-best candidate is a clear 0.29 behind. This is the 90% case, and it costs zero tokens and ~sub-millisecond
scoring on a tree this size.

---

## 3. Scenario 2 — a control is deleted

A redesign removes the **Summary** tab entirely. The stored locator `summaryTab` points at an element that no
longer exists. The engine *should* decline and ask a human — healing onto a different tab would make the test
pass while exercising the wrong screen.

### At the shipped default (`MinimumConfidence = 0.50`)

| | Value |
| :--- | :--- |
| Outcome | **false heal** — `IsConfident = true` |
| Score | **0.744** |
| Matched | `pictureTab` · "Dimensions" · TabItem |
| Runner-up | 0.684 (`filtersTab`) |
| Component breakdown | control type `1.00` · parent `1.00` · sibling position `0.86` · **name `0.10`** · position `0.78` |

The name signal correctly collapses (`Summary` vs `Dimensions` → `0.10`), but the four structural signals are all
strong — `pictureTab` is the same control type, in the same tab strip, at almost the same position — and together
they carry the score to `0.744`, over the `0.50` floor. The engine accepts a wrong element. This is exactly the
`summaryTab → pictureTab` collision documented in
[benchmark-calibration.md §7](../benchmark-calibration.md#7-whole-tree-reconciliation-the-offline-probe-98).

### After calibrating for this app

`CalibrationCli` sweeps synthetic perturbations against this exact tree and recommends a profile:

```
$ dotnet run --project samples/CalibrationCli -- \
    TestAutomation/ScenarioRunner/Fixtures/HandBrake_1.8.2.tree.json --app HandBrake

## 🏆 Recommended Profile: Balanced

| Profile      | Min Confidence | Precision | Auto-Heal Recall | False Heal Rate | Manual Review |
| :---         | :---:          | :---:     | :---:            | :---:           | :---:        |
| Aggressive   | 0.50           | 81.0 %    | 91.1 %           | 19.0 %          | 18.2 %       |
| Balanced     | 0.75           | 92.4 %    | 86.6 %           |  7.6 %          | 31.8 %       |
| Conservative | 0.90           | 98.8 %    | 72.3 %           |  1.2 %          | 46.8 %       |
```

`Balanced` and `Conservative` also apply a **per-component name gate** (#370): when the stale locator had a name,
the winning candidate's `NameScore` must clear `0.30` on its own, independently of the weighted total. That is
what takes Balanced from `9.3 %` to `7.6 %` false heals here at no recall cost — a deleted tab healing onto an
adjacent one is exactly the case where structure agrees but the label does not.

A second #370 guard, `SelfHealingEngine.ReconcileAgainstRepository`, catches this exact `summaryTab` case from
the other side: `pictureTab` is *itself* an authored locator, so once the engine re-resolves the rest of the
suite against the live tree it sees the node is already owned and declines the heal. Across the full ablation it
takes deleted-element false heals from `19 %` to `2 %` on HandBrake with no recall cost
([the measurement](../benchmark-calibration.md#14-repository-ownership-reconciliation-in-the-engine-370)); the
handful it cannot catch are deletes that land on a control no other test uses.

Re-running both scenarios with `SelfHealingEngine.Create(ThresholdProfile.Balanced)` (`MinimumConfidence = 0.75`):

| Scenario | Score | Default (0.50) | Balanced (0.75) |
| :--- | :---: | :--- | :--- |
| `Start` renamed | 1.000 | healed ✓ | **healed ✓** |
| `Summary` deleted | 0.744 | false heal ✗ | **`IsConfident = false` → `LowConfidence` → declined, routed to review ✓** |

The rename still heals; the deleted tab now falls below the bar and is handed to a human instead of silently
re-pointed. Every one of those decisions is written to the healing report with its score and component
breakdown.

---

### What this does *not* claim

- **Calibration is per-app, not a global fix.** The same `0.75` threshold buys a *different* result on ShareX
  ([§8](../benchmark-calibration.md#8-a-second-application-sharex-v2100-99-134)): ~17 % false heals versus
  HandBrake's 7.6 %. Run the calibrator on *your* tree.
- **Some deleted elements score higher than any surviving element.** On the full HandBrake ablation, deleted-element
  false heals reach `0.955` — above the ceiling of every genuinely-drifted control — so no threshold catches all of
  them ([§3](../benchmark-calibration.md#3-empirical-findings-score-distribution-overlap)). This `summaryTab` case
  is one the threshold *can* catch; others it cannot.
- **The LLM fallback does not rescue this.** Multi-provider consensus on a deleted element was wrong 34 out of 34
  times ([the false-heal study](llm-false-heal-study.md)). The engine's honest position is that deleted-element
  detection is bounded; it makes the failure *visible and declinable*, not impossible.

---

## 4. Reproduce it

The calibration table is one command (above). The two scenario resolutions need only the published
`AutomationSandbox.SelfHealing` package (which pulls `UiModel`):

```csharp
using SelfHealing;
using UiModel;

var root = UiTreeSerializer.FromJson(
    File.ReadAllText("HandBrake_1.8.2.tree.json"))!;   // the committed fixture

UiElementInfo Find(UiElementInfo n, string id) =>
    n.AutomationId == id ? n : n.Children.Select(c => Find(c, id)).First(x => x is not null);

// Scenario 1: rename 'Start' -> opaque id, keep everything else
var startExpected = UiElementSnapshot.Capture(Find(root, "Start"));
var renamed = /* clone root, set the 'Start' node's AutomationId to "ab7f1c93encode" */;
var r1 = SelfHealingResolver.Resolve(startExpected, renamed);      // Score 1.000, healed

// Scenario 2: delete the 'summaryTab' subtree
var summaryExpected = UiElementSnapshot.Capture(Find(root, "summaryTab"));
var pruned = /* clone root, remove the 'summaryTab' child */;
var r2default  = SelfHealingResolver.Resolve(summaryExpected, pruned);   // 0.744, IsConfident=true  (false heal)
var r2balanced = SelfHealingResolver.Resolve(summaryExpected, pruned,
    SimilarityWeights.FromProfile(ThresholdProfile.Balanced));           // 0.744, IsConfident=false (declined)
```

---

## See also

- [Benchmark & Calibration](../benchmark-calibration.md) — the full ablation study behind these numbers
- [Can You Trust an LLM to Fix a Broken Locator?](llm-false-heal-study.md) — why the LLM arm can't close the deletion gap
- [Adding Self-Healing to an Existing Test Suite](../integration-existing-suite.md) · [Documentation Hub](../index.md)
