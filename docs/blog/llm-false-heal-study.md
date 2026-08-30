---
layout: default
title: Can You Trust an LLM to Fix a Broken Locator? - Automation Sandbox
---

# Can You Trust an LLM to Fix a Broken Locator?

*A measured study of multi-provider LLM consensus as a locator-healing signal — and why Automation Sandbox keeps the LLM out of the decision.*

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English](#-english)
> - [🇹🇷 Türkçe Özet](#-türkçe-özet)

---

## 🇬🇧 English

### TL;DR

When a UI refactor **deletes** an element, a self-healing engine must decline and ask a human — not latch onto a neighbouring button. We tested whether asking several independent LLMs and requiring them to agree can act as that "the element is gone" detector.

Across **four live multi-provider runs** (2026-08-16 to 2026-08-18, 133 usable scenarios):

- On elements that **still existed** (moved/relabelled), unanimous provider agreement was **correct 52 / 52 times**.
- On elements that had been **deleted**, unanimous agreement was **wrong 34 / 34 times** — every single time, a confident pick of the wrong neighbour.
- In 7 of those 34, **three independently-sourced model families** (Cloudflare/Qwen, Mistral, OpenRouter/gpt-oss) agreed on the *same* non-existent element at once.
- Widening the provider pool from 3 to 7 did **not** reduce the failure rate (25% → 45% → 58% → 40% agreement on deleted elements, no downward trend; 100% wrong throughout).

**The consensus check is real protection — but it comes from providers _disagreeing_ with each other, not from any model recognising that the element is gone.** When they happen to agree on a deleted element, they are unanimously, confidently wrong. That is why in Automation Sandbox the LLM is an opt-in fallback gated by an independent-agreement quorum, and a heal is only ever committed after the retried action actually succeeds — the model is never the decision maker.

Full data, methodology, and the trade-off curves live in [docs/benchmark-calibration.md](../benchmark-calibration.md) (§3, §4, §6). This page is the standalone story; that page is the source of truth for every number.

---

### 1. The problem: a deleted element has no right answer

Locator healing handles the easy 90%: an `AutomationId` changes, a label is reworded, a control moves 100px in a layout pass. The engine re-resolves the element from structural evidence — control type, parent, sibling position, name similarity, geometry — and retries.

The dangerous case is the other kind of change: the element is **deleted outright**. A checkout button is removed; the test that clicked it should now *fail loudly* so a human looks at it. What it must not do is quietly heal onto the "Cancel" button next to where "Submit" used to be, pass green, and hide a real regression — a "false heal".

Automation Sandbox's pure-heuristic scorer cannot solve this case on its own, and [we proved that rigorously](../benchmark-calibration.md#5-offline-absence-signal-investigation-95): a surviving sibling in a deleted control's container is structurally **indistinguishable** from a control that genuinely moved next to that sibling. Their similarity-score distributions overlap (`[0.665, 0.955]` for deleted-element decoys vs `[0.749, 0.874]` for true compound drift), so **no confidence threshold, runner-up margin, cluster-density, or control-type filter can draw a line between them.**

That negative result is what motivated the LLM experiment. Semantic reasoning asks a different question than geometry does — maybe independent models, forced to agree, could tell "moved" from "gone".

### 2. Method: controlled multi-signal ablation

Natural locator drift across real releases is too sparse to benchmark. So we invert it: take a real, organically-built application UI tree and **systematically mutate** its authored locators.

- **Source tree:** HandBrake 1.8.2 (WPF), captured live — 149 nodes, 42 unique authored locators. A second application, ShareX v21.0.0 (WinForms), is used in [§8 of the calibration doc](../benchmark-calibration.md#8-a-second-application-sharex-v2100-99-134) to check that findings are not HandBrake-specific.
- **Five mutation tiers:** pure rename (opaque ID), name/label drift, position shift, compound (text + layout), and **element removal** (whole subtree deleted).
- **The two questions:** for tiers 1–4 the engine should find the successor; for **removal** it should decline.

**Leakage protection.** Every candidate `AutomationId` in a mutated tree is rewritten to the *same* opaque format (`ablation-` + SHA-256 hex), not just the target. Otherwise a model could solve the scenario by spotting the "odd one out". IDs carry no semantic hint — which means these scores are a **lower bound**: in production, a descriptive `btnSaveDocument` gives the model more to work with than a synthetic hash does.

**LLM evaluation protocol.** Temperature 0. Every provider's raw vote recorded alongside the agreement telemetry (`AgreedProviders`, `ProviderAttempts`, `ProviderErrors`). A scenario counts as **usable** only when ≥ 2 providers actually returned an answer — one opinion can neither agree nor disagree with anything. Token cost is managed by targeting the two informative subsets: 25 `CompoundDrift` + 42 `RemovedElement` scenarios.

### 3. The heuristic baseline (no LLM)

Running the deterministic scorer at the shipped default (`MinimumConfidence = 0.50`) over all 176 HandBrake scenarios:

| Mutation tier | n | Correct | False heals | Missed (review) | Score range |
| :--- | :---: | :---: | :---: | :---: | :---: |
| `RenamedAutomationId` | 42 | 40 | 0 | 2 | `[1.000, 1.000]` |
| `NameDrift` | 25 | 23 | 0 | 2 | `[0.877, 0.950]` |
| `PositionShift` | 42 | 34 | 0 | 8 | `[0.832, 0.955]` |
| `CompoundDrift` | 25 | 6 | 2 | 17 | `[0.749, 0.874]` |
| **`RemovedElement`** | **42** | **25** | **17** | **0** | `[0.344, 0.955]` |

**40.5% of deleted elements are false-healed** at the default threshold. On ShareX the same figure is **57.1%**. Raising the threshold trades this away against auto-heal recall linearly — at `0.90` the false-heal rate on removed elements is still 5.5% while three-quarters of *all* locators are forced to manual review. There is no free setting.

This is the gap the LLM was asked to close.

### 4. The result: consensus separates the bands — then fails as a gate

Four runs, 2026-08-16 to 2026-08-18, as the provider pool grew from 3 to 7 and Groq's model was swapped twice:

| Run | Date | Usable (n) | `CompoundDrift` unanimous | `RemovedElement` unanimous |
| :--- | :--- | ---: | :--- | :--- |
| [31959334927](https://github.com/mustafasercansak/automation-sandbox/actions/runs/31959334927) | 08-16 | 19 | 6 / 7 | 3 / 12 |
| [31961463762](https://github.com/mustafasercansak/automation-sandbox/actions/runs/31961463762) | 08-16 | 39 | 16 / 17 | 10 / 22 |
| [31963741937](https://github.com/mustafasercansak/automation-sandbox/actions/runs/31963741937) | 08-16 | 33 | 14 / 14 | 11 / 19 |
| [32163961433](https://github.com/mustafasercansak/automation-sandbox/actions/runs/32163961433) | 08-18 | 42 | 16 / 17 | 10 / 25 |
| **Total** | | **133** | **52 / 55** | **34 / 78** |

Providers reached unanimous agreement on **94.5%** of scenarios where a successor existed, and on **43.6%** where the element was gone. That gap is real — it is the *only* signal in the entire project that separates the two populations at all.

**But unanimity is not a safe acceptance gate:**

> Every one of the **52** unanimous verdicts on a surviving element was correct.
> Every one of the **34** unanimous verdicts on a deleted element was a false heal.
> Zero exceptions in either direction, across four runs with four different provider sets.

An earlier draft of this analysis reported "33%" wrong — that figure pooled both mutation types into one denominator and understated the real rate. Read per type, **the deleted-element rate is 100%: agreement never once happened to be right about an absence.**

### 5. Why: the mechanism is disagreement, not recognition

The hypothesis predicted two safe outcomes on a deleted element — providers decline, or providers scatter across different decoys. In practice:

- **Declining essentially never happens.** Across all 78 usable `RemovedElement` scenarios, `AllDeclined` occurred exactly **once**. Every other provider that answered pointed at a specific — wrong — candidate.
- **Every correct rejection came from providers contradicting each other**, not from any model saying "that element is gone". The models do not know the control was deleted; each confidently names a *different* neighbour, and the engine rejects the heal only because the votes fail to match.

Widening the pool made this clearer, not better. Run 31961463762 recorded 10 unanimous false heals; **7 of those 10 had three independent model families agreeing on the same non-existent element** — three vendors, three architectures, one wrong answer, unanimously. Going 3 → 7 providers moved the removed-element agreement rate 25% → 45% → 58% → 40% with no downward trend and 100% wrong throughout.

The protection is a **byproduct of independence**, and independence alone does not bound the failure rate: a genuinely capable, independent reasoner finds a deleted control's surviving neighbour a *convincing* answer often enough that adding more reasoners does not reliably break the tie. It also cannot be strengthened by asking for more confidence — the failing cases are already maximally confident.

### 6. What this means for the design

Automation Sandbox is built around this result rather than despite it:

| Design choice | Why |
| :--- | :--- |
| **Heuristic-first, deterministic.** A pure C# structural scorer decides on its own, ~23ms for 3,000 controls, zero tokens. | The LLM is never on the default path. Most healing never touches a model. |
| **LLM is opt-in and quorum-gated** (`MinimumConsensusVotes`, default 2). | A single model's confidence is worthless here. Agreement is *permission* to consider a pick — never evidence it is correct. |
| **A heal commits only after the retried action succeeds** (`HealingMode.AutoHeal`); the shipped default is `Review`, which routes every candidate to telemetry and changes nothing. | A wrong pick that cannot actually perform the test step is caught before it is persisted. |
| **Every decision is written to an audit report** (schema-v8 JSON + HTML) — which signal contributed what weight, which providers voted, what the outcome was. | "The AI healed it" is not an acceptable answer. You can see exactly why. |
| **Joint locator reconciliation** (opt-in): when two locators would claim the same live element, at most one wins. | Recovers a *subset* of deleted-element cases — the ones where the neighbour is itself another test's real target. Not a general absence detector; the uncontested cases remain out of reach and are documented as a known limitation. |

**The honest bottom line:** unassisted absence detection is mathematically bounded by the structural score overlap. Automation Sandbox does not claim to have solved it. It makes the failure *visible and declinable* instead of silent and green.

### 7. Reproduce this yourself

From a clean clone (needs the .NET SDK; the LLM run needs provider API keys):

```bash
# 1. The deterministic heuristic baseline and threshold sweep — no keys, no tokens
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj \
  --filter "FullyQualifiedName~LocatorAblationTests.HandBrakeFixture_RunsEndToEndAndReportsMetrics"

dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj \
  --filter "FullyQualifiedName~LocatorAblationTests.HandBrakeFixture_ThresholdSweep"

# 2. Calibrate against your own captured UI tree
dotnet run --project samples/CalibrationCli -- <your-tree.json> --app YourApp

# 3. The live multi-provider consensus evaluation (set provider keys as env vars first)
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj \
  --filter "FullyQualifiedName~LocatorAblationTests.HandBrakeFixture_LlmConsensus_LiveEvaluation"
```

The consensus evaluation also runs as the [Ablation Consensus Evaluation](https://github.com/mustafasercansak/automation-sandbox/actions/workflows/ablation-consensus.yml) and [Nightly Multi-Provider Consensus](https://github.com/mustafasercansak/automation-sandbox/actions/workflows/nightly-consensus.yml) workflows; raw per-provider votes are published as run artifacts.

Every claim on this page is guarded by a committed regression test — see the `Regression guards:` lines throughout [docs/benchmark-calibration.md](../benchmark-calibration.md).

### 8. What this study does *not* establish

- **One dataset family.** Two real applications (HandBrake, ShareX), one mutation methodology. The direction is consistent across both; the *magnitude* is not portable (40.5% vs 57.1% false-heal on deleted elements at the same setting).
- **Non-determinism band.** A run over 42 removal scenarios carries roughly ±14% statistical uncertainty. The 34/34 result is striking precisely because it left no room for that band to matter, but a fifth run could see a unanimous verdict that is accidentally correct.
- **Production ID fidelity.** Ablation IDs are opaque hashes; real descriptive IDs would give models more signal. The LLM numbers here are a *lower bound* on production performance — which does not change the finding that agreement on a deleted element is unreliable.
- **Prompting is not exhausted.** This tests one prompt design (bounded top-N shortlist, temperature 0). It does not prove no prompt could do better — it proves that *provider agreement*, as a mechanism, does not carry the safety guarantee people assume it does.

---

## 🇹🇷 Türkçe Özet

**Soru:** Bir arayüz değişikliği bir elemanı *sildiğinde*, birden fazla bağımsız LLM'e sorup "hepsi aynı cevapta anlaşsın" kuralı koymak, "bu eleman artık yok" tespitçisi olarak çalışır mı?

**Ölçüm** (16–18 Ağustos 2026, 4 canlı çok-sağlayıcılı koşu, 133 kullanılabilir senaryo):

- Hâlâ **var olan** (taşınmış/yeniden adlandırılmış) elemanlarda oybirliği **52/52 doğru**.
- **Silinmiş** elemanlarda oybirliği **34/34 yanlış** — her seferinde, yanlış komşuyu kendinden emin biçimde seçti.
- Bu 34'ün 7'sinde **üç ayrı model ailesi** (Cloudflare/Qwen, Mistral, OpenRouter/gpt-oss) aynı var olmayan elemanda aynı anda anlaştı.
- Sağlayıcı havuzunu 3'ten 7'ye çıkarmak hata oranını **düşürmedi** (silinen elemanda anlaşma: %25 → %45 → %58 → %40, düşüş yok; baştan sona %100 yanlış).

**Sonuç:** Konsensüs kontrolü gerçek bir koruma sağlıyor — ama bu koruma sağlayıcıların *birbiriyle anlaşamamasından* geliyor, hiçbir modelin "eleman gitti" demesinden değil. Anlaştıklarında, oybirliğiyle ve kendinden emin biçimde yanılıyorlar.

**Bu yüzden Automation Sandbox'ta LLM:** varsayılan yolda değil; opsiyonel bir yedek; bağımsız-anlaşma çoğunluğuyla kapılı; ve bir iyileştirme yalnızca yeniden denenen aksiyon *gerçekten başarılı olduğunda* kalıcı hâle geliyor. Model asla karar verici değil.

Tam veri, metodoloji ve denge eğrileri: [docs/benchmark-calibration.md](../benchmark-calibration.md) (§3, §4, §6 — Türkçe kılavuz aynı dosyada).

---

## See also

- [Benchmark & Calibration](../benchmark-calibration.md) — the full study, both languages, all regression-test references
- [LLM Security Model](../llm-security-model.md) — PII/secret redaction, prompt-injection hardening, what leaves your machine
- [LLM Providers](../llm-providers.md) — configuring Claude, Gemini, OpenAI-compatible, and offline Ollama
- [Getting Started](../getting-started.md) · [Documentation Hub](../index.md)
