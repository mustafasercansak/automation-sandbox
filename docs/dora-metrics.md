---
layout: default
title: DORA & Reliability Metrics - Automation Sandbox
---

# DORA & Engineering Reliability Metrics

This document outlines how **DORA (DevOps Research and Assessment)** metrics and domain-specific reliability indicators are measured for **Automation Sandbox**.

## Core DORA Metrics

| Metric | Definition | Measurement Method | Target Tier |
| :--- | :--- | :--- | :--- |
| **Deployment Frequency (DF)** | How often package releases (`AutomationSandbox.*`) or tagged versions are published. | Number of GitHub Releases in the evaluation period. | **High / Elite** (Weekly / Bi-weekly beta or monthly stable) |
| **Lead Time for Changes (LTTC)** | Time elapsed from PR creation to merge into `main`. | `PR Merged Timestamp - PR Created Timestamp` via GitHub API. | **Elite** (< 4 hours) |
| **Change Failure Rate (CFR)** | Percentage of merged PRs addressing defects/hotfixes. | `(Fix PRs / Total Merged PRs) * 100`. | **Elite / High** (< 15% - 30%) |
| **Time to Restore Service (MTTR)** | Time required to resolve and close reported defect issues. | `Bug Issue Closed Timestamp - Created Timestamp`. | **High** (< 1 day) |

---

## Domain-Specific Reliability (Self-Healing Engine)

Because **Automation Sandbox** is an open-source locator healing and intent test generation engine, operational reliability is extended with domain-specific accuracy signals:

1. **Contested False-Heal Suppression (100% Target):**
   - Verified via `ResolveBatch` / `ResolveBatchAsync` (1-to-1 candidate ownership reconciliation).
   - Prevents false heals on deleted/vanished UI elements when competing candidates exist.

2. **Evidence Coverage Gate (`MinimumEvidenceWeight >= 0.40`):**
   - Rejects candidate claims when non-null structural signals are insufficient, preventing spurious heals on unverified nodes.

3. **Multi-LLM Consensus Quorum (`MinimumConsensusVotes >= 2`):**
   - Independent model agreement quorum filters hallucinated candidate picks across Claude, Gemini, OpenAI, and Ollama.

---

## Automated Measurement

Metrics are calculated automatically via `scripts/calculate_dora.py` and run on a weekly schedule via the [DORA Metrics GitHub Action](https://github.com/mustafasercansak/automation-sandbox/actions/workflows/dora-metrics.yml).
