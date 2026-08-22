# Contributing to Automation Sandbox

Thank you for your interest in contributing to **Automation Sandbox**! This guide outlines our engineering workflows, coding conventions, and contribution guidelines.

---

## 1. Issue-First Workflow

Every change in this repository corresponds to a tracked GitHub issue:

1. **Check existing issues or open a new one** describing the problem, motivation, or technical debt before writing code.
2. Structure new issues with clear **Problem**, **Proposed Fix/Scope**, and concrete **Acceptance criteria** checkboxes (`- [ ] ...`).
3. Create a dedicated branch per issue:
   - Bug fixes: `fix/<issue-number>-<short-description>` (e.g. `fix/190-resolveasync-candidate-list`)
   - Features / enhancements: `feat/<issue-number>-<short-description>` (e.g. `feat/195-repo-governance-security-templates`)
   - Performance / Refactoring: `perf/<issue-number>-<short-description>` or `refactor/...`

---

## 2. Technology Stack & Multi-Targeting

- **Language:** Modern C# (`<LangVersion>latest</LangVersion>`).
- **Target Frameworks:**
  - **Cross-Platform Core Libraries (`UiModel`, `SelfHealing`, `LlmHealing`, `WebDiscovery`, `IntentAutomation`):** Multi-targeted for `netstandard2.0;net8.0` (+ `net10.0` conditionally). No Windows/FlaUI dependencies allowed here; must build and run cross-platform on Linux, macOS, and Windows.
  - **Windows Desktop Discovery & Demos (`Discovery`, `WinFormsApp`, `WpfApp`):** .NET Framework 4.8 (`net48`) and .NET 8.0-windows (`net8.0-windows`).
  - **Test Suite (`ScenarioRunner`):** Multi-targeted for `net48` (Windows) and `net8.0` (Linux/macOS/cross-platform).
- **Package Management:** Managed via `Directory.Build.props` for versioning and license metadata.

---

## 3. Development & Testing Guidelines

### Building & Running Tests

```bash
# Build the entire solution
dotnet build AutomationSandbox.sln --configuration Debug

# Run the test suite (cross-platform pure-logic, benchmark, and web tests)
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --configuration Debug

# Run specific tests or benchmarks
dotnet test TestAutomation/ScenarioRunner/ScenarioRunner.csproj --filter "SyntheticTreeBenchmarkTests" --configuration Debug
```

### Testing Rules

- **Assert behavior, not implementation:** Tests should assert what components compute and decide, allowing refactoring without breaking tests.
- **Account for tests during refactors:** No tests should quietly vanish or be removed without justification.
- **Cross-platform compatibility:** Core libraries must stay compatible with `netstandard2.0` and avoid .NET Core-only APIs (such as `record` types, positional records, `Math.Clamp`, `DateOnly`, `Index`/`Range`, etc.) in `UiModel`, `SelfHealing`, and `LlmHealing`.

---

## 4. Pull Request Checklist

When submitting a pull request:
- [ ] Link the issue in the description (e.g. `Resolves #123`).
- [ ] Provide a clear summary of changes and rationale.
- [ ] Ensure all acceptance criteria in the issue are checked `[x]`.
- [ ] Run the full test suite (`dotnet test`) and ensure all tests pass.
- [ ] If changing performance-critical paths, provide before/after benchmark results.
