# Distribution kit — LLM false-heal write-up

Channel-specific framing for publishing [`llm-false-heal-study.md`](llm-false-heal-study.md). This file is an
internal promotion playbook and is **excluded from the published docs site** (see `exclude:` in `_config.yml`).
Issue [#342](https://github.com/mustafasercansak/automation-sandbox/issues/342).

**Canonical URL** (use this everywhere as the link target / cross-post canonical):
`https://mustafasercansak.github.io/automation-sandbox/docs/blog/llm-false-heal-study.html`

Repo: `https://github.com/mustafasercansak/automation-sandbox`

---

## Sequencing checklist

Do these in order. Do not fire everything at once.

1. **Day 0 — merge & publish.** Merge the branch. Confirm GitHub Pages rebuilt and the canonical URL renders (mermaid, tables, links all OK).
2. **Day 0 — dev.to** as the "home" long-form copy, `canonical_url` pointing at the Pages URL. dev.to is the most tolerant of a technical deep-dive and gives the piece a comment surface.
3. **Day 1, morning (your time ~09:00, which is a US-night / EU-morning) — Hacker News** "Show HN". Post, then do not touch it. Reply to substantive comments only, no thread-bumping.
4. **Day 1, ~4h after HN — r/dotnet.** Different framing (see below), link to the Pages URL, not to HN.
5. **Day 2 — r/QualityAssurance and r/softwaretesting**, one at a time, a few hours apart. These subs are stricter about self-promotion — lead with the finding, mention the tool once at the end.
6. **Day 2 — Medium + Hashnode** re-posts with `canonical` set. Low effort, some long-tail SEO.
7. **Day 3+ — newsletters.** Submit the dev.to or Pages link (below).
8. **Week 2 — awesome-list PRs** (below). Do these once the piece has a few points of external validation (HN points, comments) so the PR reviewer sees it is real.
9. **LinkedIn** — you already have the audience there. Post the English write-up (not just the TR series) linking the canonical URL, framed for an international / institutional reader.

---

## Show HN

**Title** (HN title rules: no editorializing, ≤ 80 chars):

```
Show HN: Locator healing for .NET tests, with the LLM kept out of the decision
```

Alternative if the first feels too broad:

```
Show HN: I measured whether LLM consensus can tell a moved UI element from a deleted one
```

**First comment** (post immediately after submitting):

```
I maintain UI tests (desktop via UI Automation, web via Playwright) and got tired of
locators breaking on every refactor. Commercial tools "heal" this with a black-box AI
and you can't see how the pick was made.

Automation Sandbox does it heuristic-first: a deterministic C# structural scorer
(control type, parent, sibling position, name, geometry) decides on its own in ~20ms
for a 3,000-control tree, zero tokens. An LLM is an opt-in fallback only when the
heuristic isn't confident, and a pick needs at least two independent providers to
agree before it's even considered.

The reason it's built that way is a measurement I couldn't argue with. When a refactor
*deletes* an element, the engine must decline, not latch onto a neighbour. I tested
whether multi-provider LLM agreement could be that "it's gone" detector. Across four
live runs: every one of 34 unanimous verdicts on a deleted element was a false heal —
including 7 cases where three different model families agreed on the same wrong answer.
The consensus check helps, but only because providers *disagree* with each other, not
because any model recognises absence. Full data and method:
https://mustafasercansak.github.io/automation-sandbox/docs/blog/llm-false-heal-study.html

It's MIT, pure C#/.NET, 7 NuGet packages, plugs into xUnit/NUnit/Playwright/FlaUI.
I'd genuinely like feedback on the design — especially from anyone who has shipped
locator self-healing and hit the deleted-element problem.
```

**Rules of engagement:** Answer technical questions. Do not argue with dismissive comments. Do not ask for upvotes anywhere. If it doesn't get traction, that's fine — the dev.to/Pages copy still stands and the Reddit posts are independent.

---

## r/dotnet

Flair: `Project` (or the sub's equivalent). r/dotnet is fine with a "here's what I built" post if it's technical and not salesy.

**Title:**

```
I built an explainable locator self-healing engine for .NET tests — and measured why the LLM can't be the one deciding
```

**Body:**

```
Locators breaking on every UI refactor is the tax on maintaining a UI test suite.
Commercial tools heal it with an opaque AI; you get a green test and no idea why.

I've been building **Automation Sandbox** (MIT, pure C#/.NET) as an open take on this:

- **Heuristic-first, deterministic.** A structural similarity scorer (control type, parent,
  sibling position, name, geometry) runs in ~20ms on a 3,000-control tree, zero cost,
  zero tokens. Every score is broken down component-by-component in a JSON report.
- **LLM is opt-in and quorum-gated.** Only runs when the heuristic isn't confident;
  a pick needs ≥ 2 independent providers (Claude / Gemini / OpenAI-compatible / local
  Ollama) to name the same candidate. Works with the runners you already use —
  xUnit, NUnit, Playwright, FlaUI.
- **Nothing is applied silently.** Default mode changes no locators; auto-heal commits
  only after the retried action actually passes.

The part I want to share is the measurement behind that design. When a refactor deletes
an element outright, the engine has to decline. I tested whether LLM consensus could
detect that. Four live runs, up to seven providers: **34 out of 34 unanimous verdicts
on a deleted element were false heals.** The agreement rate does separate "moved" from
"gone" better than any heuristic signal I tried — but every time the models actually
agreed on a deleted element, they were unanimously, confidently wrong.

Write-up with the full methodology, the threshold sweeps, and the reproduction commands:
https://mustafasercansak.github.io/automation-sandbox/docs/blog/llm-false-heal-study.html

Repo: https://github.com/mustafasercansak/automation-sandbox

Happy to answer questions about the scoring, the provider plumbing, or the benchmark.
```

---

## r/QualityAssurance and r/softwaretesting

These communities downvote anything that smells like a product launch. Lead with the finding as a *testing* problem; the tool is a footnote.

**Title:**

```
Measured: can you trust an AI to fix a broken locator? 34/34 unanimous LLM verdicts on a deleted element were wrong
```

**Body:**

```
"Self-healing locators" is a headline feature in most commercial test tools now. I wanted
to know how it behaves in the one case that actually matters: when a UI change *deletes*
the element your test was using. The tool should fail and make a human look — not quietly
re-point the step at a nearby button and pass green.

I ran a controlled experiment. Take a real app's UI tree (HandBrake, then ShareX),
systematically mutate its locators — rename, relabel, move, and delete — and see what
a healing engine does. For the delete case I also asked multiple independent LLMs
(up to seven providers) and required them to agree before accepting a pick.

Results across four runs:
- On elements that still existed, unanimous agreement was right 52/52 times.
- On deleted elements, unanimous agreement was wrong 34/34 times. Every time.
- In 7 of those, three different model families agreed on the same non-existent element.
- The structural score of a deleted element's "best" wrong neighbour (0.665–0.955)
  overlaps the score of a genuinely moved element (0.749–0.874), so no confidence
  threshold separates them either.

The consensus check isn't useless — it rejects a lot of bad heals — but only because
the models *disagree* with each other, not because any of them knows the element is gone.

Full data, charts, and how to reproduce:
https://mustafasercansak.github.io/automation-sandbox/docs/blog/llm-false-heal-study.html

(This is from an open-source .NET project I maintain; happy to share but the finding
stands on its own regardless of tooling.)
```

---

## Cross-post notes (dev.to / Medium / Hashnode)

- **Title:** *Can You Trust an LLM to Fix a Broken Locator? I Measured It.*
- **dev.to:** paste the English body of the write-up. Front matter: `canonical_url: https://mustafasercansak.github.io/automation-sandbox/docs/blog/llm-false-heal-study.html`. Tags: `dotnet`, `testing`, `ai`, `opensource`.
- **Medium:** use the "Import a story" feature with the canonical URL — it sets the canonical link automatically. Publish to no publication first; submit to *Better Programming* / *ITNEXT* after.
- **Hashnode:** set the canonical URL in post settings. Cross-post to the `.NET` and `Testing` topics.
- Keep the mermaid charts as static images on Medium/Hashnode (export from the Pages render) — neither renders mermaid reliably.
- Every re-post links back to the **repo** at the end, and to `docs/benchmark-calibration.md` for "the full study".

---

## Newsletter / aggregator submissions

Submit the canonical URL (or the dev.to copy). One line of context, no pitch.

| Outlet | How to submit |
| :--- | :--- |
| The .NET Weekly (`dotnetweekly.com`) | submission form on the site |
| .NET News (Reddit-sourced) | covered automatically if r/dotnet gains traction |
| Dev Leader Weekly (Nick Cosentino) | email / DM with the link |
| TestGuild (Joe Colantonio) | "submit a topic" form; angle: the false-heal measurement |
| Ministry of Testing — The Testing Planet | community feed post + newsletter suggestion |
| Awesome .NET newsletter / Cool GitHub Repos | tends to pick up repos that trend; no action needed beyond the repo being visible |
| Playwright community (Discord `#showcase`) | short post, .NET SDK angle |
| FlaUI discussions (GitHub) | a "projects using FlaUI" style note if such a thread exists |

---

## awesome-list PRs

One-line entry text, ready to paste. Match each list's existing formatting on submission.

- **awesome-dotnet** (`quozd/awesome-dotnet`) → *Testing* section:
  `* [Automation Sandbox](https://github.com/mustafasercansak/automation-sandbox) - Explainable locator self-healing and intent-driven test generation for desktop (FlaUI) and web (Playwright); heuristic-first with an opt-in, quorum-gated LLM fallback.`

- **awesome-dotnet-core** (`thangchung/awesome-dotnet-core`) → *Testing*:
  same entry text.

- **awesome-playwright** (`mxschmitt/awesome-playwright`) → *Tooling* / *Utilities*:
  `* [Automation Sandbox](https://github.com/mustafasercansak/automation-sandbox) - Structural locator self-healing for Playwright .NET, with a per-decision audit report and an opt-in multi-provider LLM fallback.`

- **awesome-test-automation** (`atinfo/awesome-test-automation`) → *.NET* or *Frameworks*:
  `* [Automation Sandbox](https://github.com/mustafasercansak/automation-sandbox) - Open, explainable locator healing engine for .NET UI tests; deterministic heuristic scorer first, LLM consensus only as a guarded fallback.`

- **awesome-selfhosted / awesome-ai-tools** — not a fit; skip.

PR description for each: one sentence on what it is, a link to the write-up as evidence it is a real and measured project, and a note that it is MIT and actively maintained (link the recent release).
