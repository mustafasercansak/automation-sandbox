---
layout: default
title: How Automation Sandbox Compares - Automation Sandbox
---

# How Automation Sandbox Compares

> **TR:** Bu sayfa Automation Sandbox'ı açık kaynak (Healenium) ve ticari (Testim, Mabl, Ranorex, Functionize)
> locator self-healing yaklaşımlarıyla dürüstçe karşılaştırır. Her satır, ilgili ürünün **herkese açık
> dokümantasyonundan** doğrulanabilir. Sizin durumunuz için başka bir aracın daha doğru seçim olduğu yerleri de
> açıkça söyler.

Automation Sandbox is not trying to be Ranorex or Tosca. It solves **one** part of the test-automation problem —
a locator broke, re-resolve it from structural evidence and explain the decision — as an open, auditable .NET
library. This page is for deciding whether that scope, and that approach, fit your situation.

Every claim about another product below is sourced from that product's own public documentation. Where a product
is the better fit, this page says so.

---

## At a glance

| | **Automation Sandbox** | **Healenium** | **Testim / Mabl / Functionize** | **Ranorex** |
| :--- | :--- | :--- | :--- | :--- |
| Stack | .NET (net48 / netstandard2.0 / net8 / net10) | Java + Selenium (JS SDK community) | SaaS, language-agnostic recorder | Windows IDE, .NET scripting |
| License / cost | MIT, free, self-hosted | Apache-2.0, free; needs a backend service + DB | Commercial subscription | Commercial per-seat licence |
| Healing approach | Deterministic structural heuristic **first**; LLM only as an opt-in, quorum-gated fallback | ML over historical DOM trees stored in a backend | Vendor ML model (black box) | Proprietary "RanoreXPath" weighting |
| Explainability | Per-decision JSON + HTML: every signal's weight, providers' votes, outcome | Healing report of before/after selectors | Limited; heal shown in run history | Limited |
| Data leaving your machine | Nothing, unless you configure an LLM — then a bounded shortlist with PII/secret redaction on by default | Selector data to your self-hosted backend | Full DOM / screenshots to vendor cloud | Local |
| Where locators live | Readable JSON in your repo, versioned, diffable | In the Healenium backend DB | Vendor cloud | Ranorex object repository (`.rxrep`) |
| Desktop support | Yes — FlaUI / UI Automation (Windows) | No (web only) | Mostly web; some desktop (vendor-specific) | Yes — strong desktop |
| What runs it | Your runner: xUnit, NUnit, Playwright, FlaUI, Reqnroll | Selenium tests | Vendor runner / grid | Ranorex Studio + Ranorex agent |
| Recorder / IDE / grid / scheduler | **None** — library only | None (heals existing Selenium tests) | Yes — full platform | Yes — full studio |

---

## What each approach optimises for

**Automation Sandbox** optimises for *trust in the individual heal*. The heuristic is deterministic, so the same
tree always produces the same decision, and every decision is reconstructable from the report. The LLM is boxed in
deliberately because [multi-provider agreement on a deleted element is unreliable](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97) —
34 of 34 unanimous verdicts in four measured runs were false heals. If you need to explain to an auditor *why* a
locator changed, this is the model built for that question.

**Healenium** optimises for *drop-in healing of an existing Selenium-Java suite*. If that is your stack, Healenium
is mature, widely used, and requires no change to how you write tests. The trade-offs are operational: you run and
maintain a backend service plus a database, healing history lives there rather than in your repo, and it is
web-only.

**Testim / Mabl / Functionize** optimise for *time-to-first-test and low-code authoring*. A recorder builds the
test, the vendor's model heals it, and a non-programmer can maintain it. You are buying a whole platform — grid,
scheduling, analytics, support — and accepting a black-box healer and your test data in the vendor's cloud.

**Ranorex** optimises for *desktop and mixed desktop/web automation with a full studio*. Its object repository and
path weighting are strong for Windows apps. It is a licensed IDE-centric product, not a library you compose into
your own code.

---

## When *not* to choose Automation Sandbox

- **You need a recorder, an IDE, an execution grid, or a scheduler.** This is a library. It does not run your
  tests. Ranorex Studio, Tosca, or a SaaS platform solve a much wider problem — use one of those.
- **Your suite is Selenium + Java.** Automation Sandbox is .NET. [Healenium](https://healenium.io/) is the
  natural fit and is designed exactly for that drop-in case.
- **You want a non-programmer to author and maintain tests.** The low-code platforms (Testim, Mabl) are built for
  that; this library assumes you write and own C# test code.
- **You require a vendor SLA and support contract.** This is an MIT open-source project maintained in the open.
  Evaluate its [API stability policy](versioning-and-stability.md) and release cadence against your risk tolerance.

## When Automation Sandbox is the right call

- You maintain **.NET** UI tests (desktop, web, or both) and locators breaking on refactors is your main pain.
- You want healing **inside the runner you already use**, with no new service to operate and no test data leaving
  your infrastructure.
- You need every heal to be **auditable** — a readable locator repository in source control and a per-decision
  report — rather than a green check from a model you cannot inspect.
- You are wary of "AI-powered healing" claims and want the AI to be **optional, bounded, and never the sole
  decision maker**.

---

## See also

- [Benchmark & Calibration](benchmark-calibration.md) — measured accuracy on two real applications, and the multi-provider consensus study
- [Scope: what this is / what this isn't](https://github.com/mustafasercansak/automation-sandbox#-scope-what-this-is--what-this-isnt)
- [Documentation Hub](index.md) — including the guide for adding self-healing to an existing suite
