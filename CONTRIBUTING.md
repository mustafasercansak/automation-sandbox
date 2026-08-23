# Contributing to Automation Sandbox

Thank you for your interest in contributing to **Automation Sandbox**! This guide outlines our engineering workflows, lifecycle policies, coding conventions, and contribution guidelines.

---

## 1. Issue-First Workflow & Definition of Ready (DoR)

Every change in this repository corresponds to a tracked GitHub issue.

### Definition of Ready (DoR) — Gate Before Starting Work
Before any issue or task can be moved to **"Ready"** or before any implementation work begins, the issue **MUST** have the following fields explicitly defined:
1. **Priority**: `P0 (Urgent/Blocker)`, `P1 (High)`, `P2 (Medium)`, or `P3 (Low/Backlog)`
2. **Estimate (`Est.` / `Estimate`)**: A concrete time estimate (e.g. `Est: 30m`, `Est: 1h`, `Est: 2h`, `Est: 1d`, `Est: 2d`)
3. **Size**: T-Shirt sizing (`XS` <1h, `S` 1h-2h, `M` 2h-4h, `L` 1d-2d, `XL` >2d)
4. **Iteration**: Execution target — `Current Iteration (Now)`, `Next Iteration (Next)`, or `Backlog (Future)`
5. **Problem Statement**: Clear, reproducible explanation of the problem, defect, or motivation
6. **Proposed Changes / Scope**: Actionable checklist of changes
7. **Acceptance Criteria**: Concrete checkboxes (`- [ ] ...`) defining when the issue is resolved

> [!IMPORTANT]
> **Strict Policy:** If `Priority`, `Estimate`, `Size`, or `Iteration` is missing, work **MUST NOT BE STARTED** and the item must not be moved to "Ready".

### Branch Naming Convention
Create a dedicated branch per issue:
- Bug fixes: `fix/<issue-number>-<short-description>` (e.g. `fix/225-update-test-dependencies-warnings`)
- Features / enhancements: `feat/<issue-number>-<short-description>` (e.g. `feat/223-package-security-audit-policy`)
- Performance / Refactoring: `perf/<issue-number>-<short-description>` or `refactor/...`

---

## 2. Technology Stack & Target Framework Constraints

- **Language:** Modern C# (`<LangVersion>latest</LangVersion>`).
- **Target Frameworks:**
  - **Cross-Platform Core Libraries (`UiModel`, `SelfHealing`, `LlmHealing`, `WebDiscovery`, `IntentAutomation`, `PlaywrightLiveExploration`):** Multi-targeted for `netstandard2.0;net8.0` (+ `net10.0` conditionally). No Windows/FlaUI dependencies allowed here; must build and run cross-platform on Linux, macOS, and Windows.
  - **Windows Desktop Discovery & Demos (`Discovery`, `WinFormsApp`, `WpfApp`):** .NET Framework 4.8 (`net48`) and .NET 8.0-windows (`net8.0-windows`).
  - **Test Suite (`ScenarioRunner`):** Multi-targeted for `net48` (Windows) and `net8.0` (Linux/macOS/cross-platform).
- **Package Management:** Managed via `Directory.Build.props` for versioning and pack metadata.

> [!WARNING]
> Core libraries target `netstandard2.0`, so modern .NET Core-only constructs (`record` types, `init` accessors, `KeyValuePair` deconstruction in `foreach`, `Math.Clamp`, `DateOnly`, `Index`/`Range` `^1`, `Dictionary.TryAdd`) cannot be used in shared libraries or `ScenarioRunner`'s `net48` leg.

---

## 3. Security, Auditing & Quality Gates

- **Mandatory Package Security Auditing (#223):** MSBuild `NuGetAudit` is enabled repo-wide (`NuGetAuditMode=all`, `NuGetAuditLevel=moderate`). In CI (`ContinuousIntegrationBuild=true`), any High or Critical advisory (`NU1903` direct, `NU1904` transitive) causes a **hard build failure**.
- **Zero Build Warnings:** Code must compile cleanly with `0 Warning(s), 0 Error(s)` across all targeted frameworks. Analyzer warnings (such as `xUnit.analyzers` `xUnit2031`) must be resolved rather than suppressed.
- **Testing Rules:**
  - **Assert behavior, not implementation:** Tests should assert what components compute and decide, allowing refactoring without breaking tests.
  - **Account for tests during refactors:** No tests should quietly vanish or be removed without justification.
  - **Skip Handling:** Live tests requiring external API keys or live GUI environments must use `[SkippableFact]` so they report `[SKIP]` rather than a false `[PASS]` when credentials/environment are absent.

---

## 4. Development Commands

```bash
# Build the entire solution
dotnet build AutomationSandbox.sln --configuration Debug

# Run the full test suite
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --configuration Debug --no-build

# Audit packages for security vulnerabilities and outdated versions
dotnet list package --vulnerable --include-transitive
dotnet list package --outdated
```

---

## 5. Pull Request Lifecycle & Checklist

### "In Review" State Transition & Definition of Done (DoD)
When a Pull Request (PR) is opened:
1. The corresponding issue and PR **MUST IMMEDIATELY transition to "In Review"**.
2. The PR description must reference the issue (`Fixes #xyz` or `Closes #xyz`) and declare the metadata:
   ```markdown
   - **Issue:** Fixes #xyz
   - **Priority:** P1 (High)
   - **Size:** S (Small)
   - **Iteration:** Current Iteration (Now)
   - **Estimate:** 1h
   ```

### Definition of Done (DoD) — Completion Gate
> [!CAUTION]
> **Strict Policy:** Every single task item and acceptance criterion checkbox (`- [x]`) in the issue, implementation plan, and PR description **MUST be completed and checked off**. If any criterion or task remains unchecked (`- [ ]`), the work is **NOT complete** and transitioning to the next phase (review approval, PR merge, or issue resolution) is **STRICTLY BLOCKED**.

### Pull Request Checklist
When submitting a pull request, ensure:
- [ ] Linked issue in the description (`Fixes #xyz` or `Closes #xyz`).
- [ ] `Priority`, `Size`, `Iteration`, and `Estimate` metadata included in PR description.
- [ ] All acceptance criteria in the issue are verified and checked `[x]`.
- [ ] Solution builds with `0 Warning(s)` and `0 Error(s)`.
- [ ] Full test suite passes (`dotnet test`).
- [ ] No High/Critical vulnerable packages introduced (`dotnet list package --vulnerable --include-transitive`).
