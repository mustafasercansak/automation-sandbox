# AutomationSandbox.Cli

A language- and platform-agnostic command-line wrapper around `AutomationSandbox.SelfHealing`'s deterministic locator-healing engine. `SelfHealingResolver.Resolve` operates purely on the framework-agnostic `UiElementInfo` tree, so the only thing standing between "any language, any CI" and this engine was packaging - this tool removes that barrier. Any caller, in any language, produces two JSON files shaped like `UiTreeSerializer.ToJson`'s output and shells out to one command; no C# project, no NuGet reference, no FlaUI/Playwright .NET dependency is needed on the calling side.

## Install

```bash
dotnet tool install -g AutomationSandbox.Cli --prerelease
```

(`--prerelease` is required during the `0.x` beta series; drop it once a stable `1.0` ships.)

## Use

```bash
automation-sandbox-heal --expected stale-element.json --tree live-tree.json --profile balanced
```

- `--expected` - a single `UiElementInfo` JSON object: the stale/broken locator's last known snapshot.
- `--tree` - a `UiElementInfo` JSON tree: a freshly captured snapshot of the live application.
- `--profile` - `balanced` (default), `conservative`, or `aggressive`. See `SimilarityWeights.FromProfile` in the main repository's `AGENTS.md` for what each trades off.

Both JSON inputs follow a published [JSON Schema](https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json) - point your editor or validator at it for autocomplete and validation without reading any C# source. Add `"$schema": "https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json"` to your own JSON files if your tooling supports it.

Prints a `HealResult` as JSON to stdout (nothing else goes to stdout - diagnostic logging goes to stderr, so stdout is always pure, parseable JSON). Exit code `0` means `IsConfident` (safe to accept `Matched`); `1` means not confident (do not auto-accept); `2` means an error occurred before a decision could be made (bad input, missing file).

## Example: a non-.NET caller

Any language works - all it needs to produce is JSON shaped like `UiTreeSerializer.ToJson`'s output. A minimal Python example, assuming your own tooling has already captured the stale element and the live tree into dicts with the same shape:

```python
import json
import subprocess

with open("expected.json", "w") as f:
    json.dump(expected_element, f)
with open("tree.json", "w") as f:
    json.dump(live_tree, f)

result = subprocess.run(
    ["automation-sandbox-heal", "--expected", "expected.json", "--tree", "tree.json", "--profile", "balanced"],
    capture_output=True, text=True,
)
heal_result = json.loads(result.stdout)  # stdout is always pure JSON; diagnostics go to stderr
if result.returncode == 0:
    print(f"Healed: use {heal_result['Matched']['AutomationId']} instead")
else:
    print("Not confident enough to auto-heal; falling back to manual review")
```

## Using it in CI without hand-writing the install/parse plumbing

The repository ships a composite GitHub Action, `.github/actions/heal`, that wraps the install + run + JSON-parsing steps above into one line:

```yaml
- uses: mustafasercansak/automation-sandbox/.github/actions/heal@main
  with:
    expected: path/to/stale-element.json
    tree: path/to/live-tree.json
    profile: balanced
```

By default this step fails the job when the result is not confident - the same exit-code contract as the CLI itself, so a bare `uses:` line is already a working pass/fail gate. Add `continue-on-error: true` and read the `is-confident` / `result` outputs if you want to branch on the outcome instead of failing the build.

## Desktop and web alike

`SelfHealingResolver` draws no platform distinction - it scores whatever `UiElementInfo` tree it is handed, whether that tree came from FlaUI (Windows desktop) or Playwright (web). The CLI's own test fixtures prove this directly: one is a web-flavored element rename, the other is the repository's own established WinForms "flagship demo" scenario (`txtEmail` renamed to `textBox1`, including its documented ~0.057 runner-up margin against sibling `txtLastName`). Capture your desktop tree with any UIA-capable tool in any language, shape it the same way, and this CLI heals it exactly as it would a web locator.

## Why a CLI, not just the library

The `AutomationSandbox.SelfHealing`/`AutomationSandbox.UiModel` packages are the right choice for a .NET project that already captures its own `UiElementInfo` trees (via `Discovery`, `WebDiscovery`, or a hand-rolled capture adapter) and wants to call the resolver in-process. This CLI is for everything else: a non-.NET test suite, a shell script in a CI pipeline, or any tool that can produce a JSON tree but has no reason to take on a .NET dependency just to get a heal decision.

## Full documentation

See the main repository: <https://github.com/mustafasercansak/automation-sandbox>
