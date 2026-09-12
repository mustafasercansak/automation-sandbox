# Unreleased: public API audit (#400)

## Breaking changes — next minor release only

Ship this together with the `AutomationSandbox.*` namespace migration (#399) in the
next breaking release. Do not publish it as a patch to the existing stable API contract.
The release workflow owns the eventual package version; this change does not publish packages.

- `ScoreComponents`, `PlaywrightLocatorSuggestion`, `IntentElementCandidate`, and
  `IntentDesktopElementCandidate` now take named constructor arguments and expose get-only
  properties. Replace `new ScoreComponents { NameScore = 0.8 }` with
  `new ScoreComponents(nameScore: 0.8)`. Apply the same lower-camel-case parameter convention
  to the other three types. Rebuild consumers against the new packages.
- `IntentElementCandidate` copies its supplied locator suggestions into a read-only list.
  Later changes to the input list cannot change the candidate's suggestions. Step and element
  references remain editable; this is not deep immutability of a captured tree.
- Intent matching/generation helpers `AssertionCodeEmitter`, `CodeGenerationUtilities`,
  `IntentCandidateReviewEvaluator`, `IntentRecordingLookupTable` (generic and static), and
  `IntentTextScoring` are internal. Use the public planners, exploration bridges, and test
  generators instead of depending on their implementation helpers.

## Consumer documentation

All seven packages include XML IntelliSense files. Missing public comments fail the
library build, and package validation checks the documentation for each target framework.
The [API audit](../public-api-audit.md) explains why editable configuration, trees,
persisted documents, and extension results remain mutable.
