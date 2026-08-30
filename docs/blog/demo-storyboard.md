# Demo GIF storyboard — README hero

Internal production notes for the 30-second README demo. Issue
[#345](https://github.com/mustafasercansak/automation-sandbox/issues/345). Excluded from the published docs site
(see `exclude:` in `_config.yml`).

**Goal:** a searcher scanning the README for ~10 seconds must see (1) a locator break, (2) the engine heal it, and
(3) *the explanation* — not just a green check. The explanation is the differentiator; the GIF must show it.

**Constraints:** ≤ 30s, loops cleanly, < 3 MB, readable at GitHub's README render width (~830px), committed under
`docs/assets/`.

---

## Capture path — pick one

### Option A (recommended): scripted terminal, asciinema → GIF

Deterministic, tiny file, no video codec noise, text stays crisp at any width.

1. Drive [`samples/PlaywrightEndToEndQuickstart`](../../samples/PlaywrightEndToEndQuickstart) (it already demonstrates
   a real refactor heal + a false-heal decline across two app versions) or a thin script around
   `HeuristicHealingQuickstart`.
2. Record: `asciinema rec demo.cast --cols 100 --rows 28 --idle-time-limit 1.5`
3. Convert: `agg demo.cast docs/assets/demo.gif --font-size 20 --theme monokai` (agg = asciinema gif generator)
4. Trim to < 3 MB: `gifsicle -O3 --lossy=80 --colors 128 docs/assets/demo.gif -o docs/assets/demo.gif`

### Option B: real browser screen capture

More visceral (you see the actual button move), but larger and needs careful cropping. Record the Playwright
sample's headed run at 1280×720, crop to the app + a terminal strip, export at ≤ 12 fps.

---

## Shot list (target ~26s + 2s hold for the loop)

| # | Time | On screen | Caption overlay (optional) |
| :--- | :--- | :--- | :--- |
| 1 | 0–3s | `dotnet test` starts; one test line visible: `Checkout_submits_the_order` | — |
| 2 | 3–7s | Test fails — red. Message: `locator '#btn-submit' not found` | "The UI was refactored. The locator is gone." |
| 3 | 7–11s | Engine log lines: `capturing live tree… 3,031 nodes`, `scoring candidates…` | — |
| 4 | 11–16s | Score table: top candidate `#checkout-confirm` **0.78**, runner-up `0.32`; component bars for ControlType / Parent / Sibling / Name / Position | "Heuristic scorer — zero tokens, ~20ms" |
| 5 | 16–20s | `retrying action with healed candidate… PASS`; test line goes green | — |
| 6 | 20–26s | Cut to the HTML healing report: `Outcome: accepted`, the per-component weights, `RunnerUpScore 0.32`, `AgreedProviders: []` | "Every heal is written to an audit report." |
| 7 | 26–28s | Hold on the report, fade to loop | — |

Key: shot 4 and shot 6 are the ones that must be legible if a viewer only glances. If time forces a cut, cut
shot 3, never 4 or 6.

---

## What shipped (#352 / #353)

The GIF in `docs/assets/demo.gif` is a **terminal render of a real `HeuristicHealingQuickstart`
run**, not a screen capture. It follows the shot list above but drops shot 6's separate
HTML-report cut, folding the report's real fields (`Outcome: accepted`, `Score 0.90`,
`RunnerUpScore 0.60`, `Source: heuristic`, `EvidenceCoverage 1.0`) into the final frames as
`healing-report.json` text. Every number is verbatim from that run's emitted report.

Regenerate it with [`gen-demo-gif.py`](gen-demo-gif.py) — the header explains how to refresh
the source numbers first. A future capture that adds the live red→green test run and the
rendered HTML report (shots 1–2, 6) can supersede it.

## After capture (checklist)

- [x] Commit `docs/assets/demo.gif`
- [x] Replace the `<!-- TODO -->` placeholder in `README.md` with the embed
- [x] Add the same GIF near the top of `docs/index.md`
- [ ] Check it on github.com at mobile width
