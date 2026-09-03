---
layout: default
title: macOS Desktop Discovery via AXUIElement — Research - Automation Sandbox
---

# macOS Desktop Discovery via AXUIElement — Research Findings

> **TR:** Bu bir araştırma notudur (#373), üretim kodu içermez. Sibling: #17 (Linux / AT-SPI). Bugün masaüstü
> keşfi (`AutomationSandbox.Discovery`) yalnızca Windows'tur (FlaUI/UIA3). macOS'ta karşılığı **Accessibility API**
> — `AXUIElement` (`ApplicationServices.framework`). **Sonuç:** yapısal sinyaller (rol, başlık/değer, sınırlayıcı
> kutu, ebeveyn/kardeş) `AXUIElement`'ten güvenilir alınabilir. `AutomationId` durumu Linux'tan **daha iyi**:
> `kAXIdentifierAttribute` (`accessibilityIdentifier`) gerçek bir eşdeğerdir ve XCUITest ekosistemi yüzünden AppKit
> uygulamalarında Linux'tan daha sık doldurulur — ama Electron/Catalyst'te yine seyrek. **İki büyük fark:**
> (1) `AXUIElement` bir **C API**'dir, yönetilen binding yoktur — P/Invoke kaçınılmaz (Linux'un saf-yönetilen
> D-Bus yolunun aksine); en temiz seçenek JSON yayan küçük bir Swift yardımcı ikili dosyası. (2) **İzin duvarı:**
> `AXIsProcessTrusted()` kullanıcı uygulamayı System Settings → Privacy & Security → Accessibility'de elle
> onaylayana kadar `false` döner — GitHub-hosted macOS runner'larında verilmez, yani testler yalnızca self-hosted
> Mac veya yerel olur.

All API behaviour below is from Apple's public documentation (`ApplicationServices` / HIServices headers,
`NSAccessibility` protocol) and the `AXUIElement` reference; the toolkit-coverage notes reflect the documented
`NSAccessibility` conformance of AppKit / Catalyst / Chromium plus community reports. No live macOS walk was run
for this note — the interop reality (§3) and the permission gate (§6) are the parts that decide the
recommendation, and both are unambiguous from the documentation.

---

## 1. Verdict

| Question | Answer |
| :--- | :--- |
| Can the macOS Accessibility API feed the signals `SimilarityScorer` uses? | **Yes** — `ControlType` (role + subrole), `Name`, `BoundingRectangle`, parent/sibling structure are all first-class `AXUIElement` attributes. |
| Is there an `AutomationId` equivalent? | **Yes — `kAXIdentifierAttribute`.** It is a real, developer-settable stable id (`NSAccessibilityElement.accessibilityIdentifier`), and the XCUITest ecosystem pushes AppKit developers to set it. Coverage is better than Linux but still not universal — Electron and some Catalyst apps leave it empty (see §3). |
| Can the cross-platform boundary stay clean? | **Yes** — a separate `AutomationSandbox.MacDiscovery` package (mirroring `AutomationSandbox.Discovery`), `UiModel` untouched. |
| Is native interop required? | **Yes, unavoidably.** `AXUIElement` is a C API in `ApplicationServices.framework` with no managed binding. Either P/Invoke into `ApplicationServices` + `CoreFoundation`, or ship a tiny Swift helper binary that emits JSON (recommended — see §3). |
| Can it run on hosted CI? | **No.** The Accessibility permission (`AXIsProcessTrusted`) cannot be granted non-interactively on GitHub-hosted macOS runners. Tests are self-hosted-Mac or local-only. |
| Recommended next step | Same as Linux (#17): a time-boxed spike that builds the capture path against 2–3 real apps (an AppKit app, a Catalyst app, an Electron app) and runs the output through `LocatorAblationHarness`, to measure how `kAXIdentifier` coverage varies by toolkit before committing to a shipped backend. |

---

## 2. What the macOS Accessibility API is, and how you talk to it

macOS accessibility is the **`AXUIElement`** API — the rough equivalent of Windows UI Automation and Linux
AT-SPI2. Every app that adopts the `NSAccessibility` protocol (AppKit does automatically; SwiftUI and Catalyst
bridge to it; Chromium implements it directly) publishes a tree of opaque `AXUIElementRef` handles. Assistive
tech — VoiceOver, and here a test tool — reads it.

Unlike AT-SPI2, this is **not** an IPC protocol you can speak from any language. It is a C API in
`ApplicationServices.framework` (specifically the HIServices sub-framework), and every call marshals a
`CFTypeRef` to and from the target process:

1. `AXUIElementCreateApplication(pid_t)` → the root `AXUIElementRef` for a running app (get the pid from
   `NSRunningApplication` / `libproc`). `AXUIElementCreateSystemWide()` gives the system-wide element for
   hit-testing.
2. Per element, `AXUIElementCopyAttributeValue(element, kAX…Attribute, &value)` reads one attribute; the value is
   a `CFTypeRef` you must `CFRelease`. `AXUIElementCopyAttributeNames` lists what an element supports.
3. `AXUIElementCopyMultipleAttributeValues(element, CFArray of names, options, &values)` reads **several
   attributes in one round trip** — the closest thing to a batch primitive (see §5).
4. Navigation is via attributes: `kAXChildrenAttribute` (→ `CFArray` of `AXUIElementRef`),
   `kAXParentAttribute`, `kAXWindowsAttribute` on the app element.

There is **no mature managed binding**. `.NET for macOS` (ex-Xamarin.Mac) binds AppKit but not a clean AX
wrapper. The realistic paths are in §3.

---

## 3. Interop: P/Invoke vs. a Swift helper — the headline engineering finding

Linux (#17) got to stay pure managed because AT-SPI2 is D-Bus. macOS does not have that luxury. Two options:

### Option A — P/Invoke into `ApplicationServices` + `CoreFoundation`

`[DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]` for the `AX*`
functions, and `[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]` for the CF
memory dance (`CFRelease`, `CFStringCreateWithCString`, `CFArrayGetCount`, `CFArrayGetValueAtIndex`,
`CFGetTypeID`, `CFNumberGetValue`, plus `AXValueGetValue` to unwrap `CGPoint`/`CGSize`). Workable, but it is
~30 P/Invoke signatures, manual `CFTypeRef` lifetime management in C#, and `AXValueRef` unwrapping for geometry —
a lot of unsafe surface for a research-tier backend, and every marshalling bug is a leak or a crash in the
target app's address space via the AX bridge.

### Option B — a tiny Swift helper binary that emits JSON (recommended)

A ~150-line Swift executable (`axdump`) linked against `ApplicationServices` that takes a pid, walks the tree
with `AXUIElementCopyMultipleAttributeValues`, and writes a `UiElementInfo`-shaped JSON tree to stdout. The
`MacDiscovery` package then just runs it (`Process.Start`) and deserialises with the existing
`UiTreeSerializer` — the same shape as `captureTreeRoot` already expects. This keeps **zero unsafe code in the
.NET packages**, isolates all CF memory management in a language built for it, and the helper is independently
testable. Cost: a compiled artifact per architecture (`arm64` / `x86_64`, or a universal binary) shipped in the
NuGet package, and a Swift toolchain in the build. This mirrors how several cross-platform tools (e.g. `tmux`
accessibility bridges, `nvda`-style helpers) solve the same problem.

**Recommendation: Option B.** The AX API's memory model is hostile to P/Invoke and the helper is small.

---

## 4. Signal-by-signal mapping

| `UiElementInfo` field | `AXUIElement` source | Quality |
| :--- | :--- | :--- |
| `ControlType` | `kAXRoleAttribute` (e.g. `AXButton`, `AXStaticText`, `AXTextField`) + `kAXSubroleAttribute` (`AXCloseButton`, `AXSecureTextField`, …) | Good. Vocabulary differs from UIA and AT-SPI — a role map is needed (§7). Subrole meaningfully disambiguates buttons/fields. |
| `Name` | `kAXTitleAttribute`, else `kAXDescriptionAttribute`, else `kAXValueAttribute` (for static text), else `kAXTitleUIElementAttribute` → that element's title (label-for association) | Good, and slightly richer than UIA/AT-SPI because of the explicit label-for attribute. Empty on most containers — same as everywhere. |
| `AutomationId` | `kAXIdentifierAttribute` | **Fair-to-good.** Real stable id (`accessibilityIdentifier`). Set by AppKit developers who write XCUITest; **empty on Electron** (Chromium exposes `AXDOMIdentifier` for *web* content only) and inconsistent on Catalyst/SwiftUI. Better than Linux's `AccessibleId`, not as reliable as Windows `AutomationId`. |
| `ClassName` | No direct equivalent. `kAXRoleDescription` (localised, unstable) or the subrole is the closest. | Poor. Do not rely on it; leave empty when absent. |
| `BoundingRectangle` | `kAXPositionAttribute` (`AXValue` wrapping `CGPoint`) + `kAXSizeAttribute` (`AXValue` wrapping `CGSize`); unwrap with `AXValueGetValue` | Good. Screen coordinates, top-left origin after flipping (`AX` reports a top-left origin already, unlike AppKit's bottom-left). Offscreen/hidden elements report real but off-screen rects — the engine already excludes unusable ones. |
| `ParentControlType` | `kAXParentAttribute` → resolve → its `kAXRoleAttribute` | Good (one extra hop, or track during the walk). |
| `ParentAutomationId` | parent's `kAXIdentifierAttribute` | Same coverage as `AutomationId` above. |
| `SiblingIndex` | `kAXIndexAttribute` if present; otherwise the element's position in the parent's `kAXChildrenAttribute` array | Good — the array order is stable within a capture. `kAXIndexAttribute` is not universal, so derive it from the children array during the walk. |
| `SiblingCount` | length of parent's `kAXChildrenAttribute` | Good. |
| filtering (visible / enabled) | `kAXEnabledAttribute` (bool), `kAXHiddenAttribute`, `kAXFocusedAttribute`; no direct "showing" — infer from a non-empty on-screen rect | Adequate — maps onto `DiscoveryOptions` filtering, with the "showing" check falling back to rect intersection with the screen. |

---

## 5. Performance: `AXUIElementCopyMultipleAttributeValues`, and the per-process IPC cost

Every `AXUIElementCopyAttributeValue` is a synchronous cross-process message to the target app, serviced on that
app's main thread. A naïve walk fetching ~8 attributes per node for a 3,000-node tree is ~24,000 messages —
seconds, and it contends with the target app's UI thread.

macOS has **no whole-subtree batch call** (no AT-SPI `Cache.GetItems` equivalent). The mitigations:

- **`AXUIElementCopyMultipleAttributeValues`** fetches all the attributes for **one** element in a single
  round trip — cuts the per-node cost from ~8 messages to 1. This is the single most important optimisation and
  every walker should use it.
- **`AXUIElementSetMessagingTimeout`** — bound a hung app so one unresponsive element doesn't stall the capture.
- Honour `DiscoveryOptions` (`MaxDepth`, `MaxElements`, `Timeout`) aggressively, as on Linux for Electron.
- The walk cannot be parallelised across the tree usefully — messages serialise on the target's main thread
  anyway — but the helper can pipeline (request child N+1's attributes while marshalling child N).

Realistic expectation: tens to low hundreds of milliseconds for a normal app window, not the synthetic
benchmark's "thousands of controls in ~15 ms". Same order as the Linux Electron case.

---

## 6. The permission gate — why this cannot run on hosted CI

macOS gates the entire AX API behind the **Accessibility** privacy permission (TCC):

- `AXIsProcessTrusted()` returns `false` until the *specific binary* (by code-signing identity / path) is toggled
  on in **System Settings → Privacy & Security → Accessibility**.
- `AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt: true])` does not grant anything — it just opens
  that Settings pane and returns `false`. There is no programmatic "request and get" like a `getUserMedia`
  prompt.
- Every `AXUIElement*` call on an untrusted process returns `kAXErrorAPIDisabled` / `kAXErrorNotAuthorized`.

**CI implications:**

- **GitHub-hosted macOS runners: not possible.** The runner process is not in the TCC allow-list and there is no
  supported way to add it non-interactively. `tccutil` can only *reset*, not grant; direct `TCC.db` writes are
  blocked by SIP for the system store and unreliable for the user store across macOS versions.
- **Self-hosted Mac runner: possible but operational.** Grant the runner agent (and the `dotnet`/`swift` binaries
  it spawns, or a wrapper app bundle) Accessibility permission once, manually, and it persists. This is the only
  path to real macOS discovery tests in CI.
- **Local dev: fine.** A developer grants their terminal / IDE once.

This is a materially higher operational cost than Linux, where `at-spi-bus-launcher` under a headless D-Bus
session is enough. It is the main reason this stays research-tier.

---

## 7. Backend sketch

Mirrors `AutomationSandbox.Discovery` (Windows) so the healing engine sees the same `UiElementInfo`:

- **New package `AutomationSandbox.MacDiscovery`**, `net8.0` (macOS). `UiModel` stays dependency-free. The package
  carries the compiled `axdump` Swift helper (universal binary) as content; no unsafe C# (Option B, §3).
- **`AxApplicationConnector`** — resolves the target app's pid (`NSRunningApplication` by bundle id, or by
  process name), checks `AXIsProcessTrusted` and surfaces a clear "grant Accessibility permission" exception if
  not, then hands the pid to the helper.
- **`AxTreeWalker`** — conceptually the analogue of `UiTreeWalker`, but the walk lives in the Swift helper
  (`AXUIElementCopyMultipleAttributeValues` per node, honour depth/element/timeout limits, emit JSON). The .NET
  side is `Process.Start` + `UiTreeSerializer.FromJson`.
- **`AxRoleMap`** — `AXRole` (+ `AXSubrole`) → the ControlType vocabulary the scorer and existing fixtures use.
  Decide, as for Linux, whether to normalise toward the UIA names (`AXButton`→`Button`, `AXStaticText`→`Text`,
  `AXTextField`→`Edit`, `AXRadioButton`→`RadioButton`, `AXTabGroup`→`Tab`, `AXGroup`→`Pane`) for shared
  cross-platform fixtures, or keep AX names and compare like-for-like within the platform.
- **No new public API in `SelfHealing`** — `SelfHealingEngine.ExecuteWithHealingAsync` already takes a
  `Func<UiElementInfo> captureTreeRoot`; the macOS connector supplies it.

---

## 8. Recommendation

1. Not beta-blocking; stays `P2` / research until there is demand. Sits behind #17 (Linux) — Linux is the more
   common CI target and has the simpler interop story.
2. Before any shipped backend, run the same **measurement spike** #17 §8 defines: capture real trees from an
   AppKit app, a Catalyst app, and an Electron app, feed them through `LocatorAblationHarness` / `TreeCalibrator`,
   and publish the false-heal / precision numbers per toolkit. The variable to quantify here is
   **`kAXIdentifier` coverage** — the hypothesis is AppKit is close to Windows and Electron is close to the
   Linux "no stable id" regime.
3. If it ships, it ships as an explicitly **experimental** `AutomationSandbox.MacDiscovery`, documenting the
   Swift-helper dependency, the per-toolkit identity coverage, and the hosted-CI limitation up front.

## See also

- [Linux Desktop Discovery via AT-SPI2 — Research](linux-desktop-atspi-research.md) — the sibling investigation, and the shared "measure before shipping" gate
- [Benchmark & Calibration](benchmark-calibration.md) — why the structural-only regime is the hard one
- [Desktop Automation](desktop-automation.md) — the Windows (FlaUI/UIA3) path this would mirror
- [Documentation Hub](index.md)
