# Organic drift dataset: Swagger UI 4.19.1 to 5.17.14 (#663)

Two genuine releases of a third-party application (Swagger UI, Apache-2.0, the unmodified `swagger-ui-dist` npm packages), captured as `UiElementInfo` trees in three page states: `initial`, `operation-open` and `try-it-out`.

| File | What it is |
| :--- | :--- |
| `manifest.json` | Release URLs, SHA-256 and npm integrity of each package, viewport, settle time and the click steps of each state. |
| `probe.html` | The page that mounts the release. The OpenAPI document inside it is ours; the application code is the downloaded release, unchanged. |
| `trees/<version>/<state>.json` | The original captures. Nothing was renamed, deleted or edited. |
| `capture-environment.json` | Browser, Playwright, Node and OS of the capture. |
| `ground-truth.json` | For every interactive element of the old version in each state: the element of the new version that plays the same role, or `null`. Each entry names both elements so a reviewer can check it by reading. |

## Reproduce

```bash
cd examples/js-playwright-capture && npm install
node organic-drift-capture.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/manifest.json ./out --channel chrome
diff -r ./out/trees ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/trees
```

A package whose hash does not match the manifest is refused.

## What it is not

One application pair, mild drift (three operation buttons changed their accessible name; three look-alike buttons were added, one of which carries the old name). **No element was removed**, so it says nothing about a deleted element healing onto a neighbour. The ground truth was assigned by the maintainer from the DOM, not independently confirmed. It is not the external validation #401 asks for. `OrganicDriftDatasetTests` pins the measured outcome per profile.
