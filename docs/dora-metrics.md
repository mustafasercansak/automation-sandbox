---
layout: default
title: DORA & Reliability Metrics - Automation Sandbox
---

# DORA & Engineering Reliability Metrics

This document outlines how **DORA (DevOps Research and Assessment)** metrics and the
self-healing engine's reliability gates are reported for **Automation Sandbox**.

## Core DORA Metrics

| Metric | Definition | Measurement method | Target tier |
| :--- | :--- | :--- | :--- |
| **Deployment Frequency (DF)** | How often tagged releases of `AutomationSandbox.*` are published. | GitHub Releases published in the window. | High / Elite (weekly beta or monthly stable) |
| **Lead Time for Changes (LTTC)** | PR creation to merge into `main`. | `merged_at - created_at`, averaged over PRs merged in the window. | Elite (< 4 hours) |
| **Change Failure Rate (CFR)** | Share of shipped changes that needed a follow-up hotfix. | `hotfix PRs / merged PRs`, where a hotfix PR is a `hotfix/` branch, a `hotfix`/`revert` title, or a `regression` / `release-blocker` label. Not every bug-fix PR - in this repo almost every PR is a bug fix, so that would carry no signal. | Elite / High (< 15-30%) |
| **Time to Restore Service (MTTR)** | Time to resolve a reported defect. | `closed_at - created_at`, averaged over issues labelled `bug` or `correctness` closed in the window (pull requests excluded). | High (< 1 week) |

A window with no qualifying activity is reported as `n/a`, never as a tier - a metric
built on zero data points is not "Elite".

---

## Self-Healing Reliability Gates

These are **configuration, not measured outcomes**. `calculate_dora.ps1` reads them
straight out of `TestAutomation/SelfHealing/SimilarityWeights.cs` so the report cannot
drift from the code.

| Gate | Source field | Purpose |
| :--- | :--- | :--- |
| **Minimum confidence** | `SimilarityWeights.MinimumConfidence` | The heuristic score a candidate must clear to be accepted without LLM fallback. |
| **Minimum evidence weight** | `SimilarityWeights.MinimumEvidenceWeight` | Rejects candidate claims backed by too few non-null structural signals. |
| **Minimum consensus votes** | `SimilarityWeights.MinimumConsensusVotes` | Independent-model agreement quorum for an LLM pick (validated to be at least 2). |

Contested false-heal suppression itself is verified by the test suite
(`BatchHealingResolverTests`, the `JointAssignmentGeneralization` probes), not by this
report.

---

## Automated Measurement

`scripts/calculate_dora.ps1` runs weekly (and on demand) via the
[DORA Metrics workflow](https://github.com/mustafasercansak/automation-sandbox/actions/workflows/dora-metrics.yml).
It writes the report to the job summary and uploads it as the `dora-report` artifact.
If the GitHub API fetch fails the workflow fails - it never publishes a report drawn
from empty data.

Run it locally:

```bash
pwsh scripts/calculate_dora.ps1 -Days 30 -Format markdown
```
