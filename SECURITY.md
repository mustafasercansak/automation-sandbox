# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 0.2.x   | :white_check_mark: |
| < 0.2.0 | :x:                |

## Reporting a Vulnerability

We take the security and privacy of Automation Sandbox seriously. If you believe you have discovered a security vulnerability in this repository, please follow these steps:

1. **Do not create a public issue or pull request.**
2. Report the vulnerability privately using [GitHub Private Vulnerability Reporting](https://github.com/mustafasercansak/automation-sandbox/security/advisories/new).
3. If GitHub Private Vulnerability Reporting is unavailable, contact the project maintainer directly via GitHub profile details.

Please include:
- A clear description of the vulnerability.
- Step-by-step instructions (or proof of concept) to reproduce the issue.
- The affected component (`UiModel`, `SelfHealing`, `LlmHealing`, `IntentAutomation`, `Discovery`, `WebDiscovery`, etc.) and runtime environment (OS, .NET SDK).
- Any potential impact on user applications, credentials, or telemetry.

### What to Expect
- You will receive an acknowledgment of your report within 48 hours.
- We will provide status updates as we validate, address, and verify the fix.
- Once a fix is verified and released, we will publish a security advisory acknowledging your contribution (unless you prefer anonymity).

## LLM Healing & DOM Privacy Model

Automation Sandbox can send UI element properties (such as element names, control types, and structural snapshots) to external LLM providers during low-confidence healing when configured. For details on how untrusted UI/DOM inputs, prompt injection defenses, and PII considerations are structured, please refer to [docs/llm-security-model.md](docs/llm-security-model.md).
