---
layout: default
title: Linux Desktop Discovery via AT-SPI — Research - Automation Sandbox
---

# Linux Desktop Discovery via AT-SPI2 — Research Findings

> **TR:** Bu bir araştırma notudur (#17), üretim kodu içermez. Bugün masaüstü keşfi (`AutomationSandbox.Discovery`)
> yalnızca Windows'tur (FlaUI/UIA3). Linux'ta karşılığı **AT-SPI2** (GNOME erişilebilirlik yığını, D-Bus üzerinden).
> **Sonuç:** yapısal sinyaller (rol, ad, sınırlayıcı kutu, ebeveyn/kardeş yapısı) AT-SPI'den güvenilir biçimde
> alınabilir ve `AutomationSandbox.LinuxDiscovery` adında ayrı bir paketle `UiModel`'i bağımsız tutarak
> uygulanabilir. **Ama kritik bir boşluk var:** UIA'daki `AutomationId`'nin güvenilir bir Linux karşılığı yok —
> `AccessibleId` spesifikasyonda var ama Chromium/Electron uygulamaları doldurmuyor, GTK'da tutarsız. Healing
> motorunun en güçlü sinyali (birebir ID eşleşmesi) Linux'ta çoğu zaman devre dışı kalır; healing, kalibrasyon
> dokümanının "belirsiz" dediği yapısal sinyallere daha çok yaslanır. İkinci sorun performans: toplu ağaç çekme
> (`Cache` arayüzü) GTK/ATK'da var ama Chromium'da yok, ve Electron uygulamaları modern Linux masaüstünün büyük
> kısmı.

All findings below were verified first-hand against a live GNOME session (`at-spi2-registryd` running,
`org.a11y.Bus` on the user session) by walking the accessibility trees of Chromium, an Electron app, and GTK apps
over D-Bus.

---

## 1. Verdict

| Question | Answer |
| :--- | :--- |
| Can AT-SPI2 feed the signals `SimilarityScorer` uses? | **Mostly yes** — `ControlType`, `Name`, `BoundingRectangle`, parent/sibling structure are all available. `ClassName` is partial. |
| Is there an `AutomationId` equivalent? | **No reliable one.** This is the headline finding — see §3. |
| Can the cross-platform boundary stay clean? | **Yes** — a separate `AutomationSandbox.LinuxDiscovery` package (mirroring `AutomationSandbox.Discovery`), pure managed D-Bus, `UiModel` untouched. |
| Is native interop required? | **No** — the AT-SPI2 API is D-Bus; a managed client (`Tmds.DBus`) is sufficient. `libatspi` P/Invoke is avoidable. |
| Recommended next step | A time-boxed spike that builds the capture path against 2–3 real apps and runs the output through the existing `LocatorAblationHarness`, to measure how much the missing `AutomationId` degrades healing precision on Linux before committing to a shipped backend. |

---

## 2. What AT-SPI2 is, and how you talk to it

**AT-SPI2** (Assistive Technology Service Provider Interface, v2) is the Linux desktop accessibility stack — the
rough equivalent of Windows UI Automation. Toolkits (GTK, Qt, Chromium/Electron, Java) publish an accessibility
tree; assistive tech (screen readers, and here, a test tool) reads it.

The API is **D-Bus**, not a C library call:

1. On the session bus, `org.a11y.Bus.GetAddress()` returns the address of a *separate* accessibility bus
   (e.g. `unix:path=/run/user/1000/at-spi/bus`).
2. On that bus, `org.a11y.atspi.Registry` at `/org/a11y/atspi/accessible/root` — `GetChildren()` returns one
   `(bus_name, object_path)` reference per running accessible application.
3. Each accessible object exposes interfaces under `org.a11y.atspi.*`. The ones that matter here:
   - **`Accessible`** — `Name`, `Description`, `Parent`, `ChildCount`, `AccessibleId` (properties);
     `GetRole()` → `uint` (stable `AtspiRole` enum), `GetRoleName()` → string, `GetChildren()`,
     `GetIndexInParent()`, `GetAttributes()` → `a{ss}`, `GetState()` → `au` bitmask, `GetInterfaces()`.
   - **`Component`** — `GetExtents(coordType)` → `(x,y,w,h)`, `GetPosition`, `GetSize`. Not present on every
     object (a window minimised to the tray had no `Component` interface at all).
   - **`Application`** — `ToolkitName`, `Version` (observed: `"Chromium"/"1.0"`, `"clutter"/"50.1"`, `"gtk"`).

A managed .NET client works fine. There is **no mature managed binding for `libatspi`**; the realistic path is
raw D-Bus via [`Tmds.DBus`](https://github.com/tmds/Tmds.DBus) (MIT, pure managed) with hand-written proxies for
the ~5 interfaces above.

---

## 3. The `AutomationId` gap — the headline finding

`SimilarityScorer`'s strongest signal is an exact identifier match: `benchmark-calibration.md` §3 shows the
`RenamedAutomationId` tier is the *only* one where every scenario scores exactly `1.000`, because when the
structure is otherwise identical a stable id resolves it outright. On Windows, UIA `AutomationId` is commonly set
by frameworks and by developers.

On Linux there is **no equivalent that can be relied on**:

- **`org.a11y.atspi.Accessible.AccessibleId`** exists (AT-SPI ≥ 2.34). In a live walk of Chromium, an Electron
  app, and GTK apps it was **empty (`""`) on every single node**. Chromium/Electron never populate it. GTK 4.10+
  *can* surface `gtk_accessible` / `GtkBuilder` ids, but only if the app was built that way, and GTK 3 apps
  cannot at all.
- **`GetAttributes()["id"]`** — Chromium *does* expose an `id` here (`view_1`, `view_2`, `view_1000`, …), but it
  is a **render-tree ordinal**, not a stable identifier: it renumbers as the view hierarchy changes. Using it as
  a locator key is exactly the `RenamedAutomationId` failure mode the benchmark measures — it looks like an id
  and drifts like a label.
- **Web content inside a browser** does carry the real HTML `id`/`class`/`tag`/`xml-roles` in `GetAttributes()`,
  but that is the *web* automation path (`AutomationSandbox.WebDiscovery` / Playwright), not desktop.

**Consequence.** A Linux desktop backend would feed the healer trees where `AutomationId` is almost always empty.
Healing then rests on the four structural signals (`ControlType`, `ParentControlType`, `SiblingPosition`, `Name`,
`Position`) — which `benchmark-calibration.md` §3–§5 shows are precisely the ambiguous ones whose score
distributions overlap. Expect Linux healing precision to sit in the "no stable id" regime, materially below the
Windows numbers. This should be **measured** (run the capture output through `LocatorAblationHarness`) before a
backend is shipped, and disclosed the way the LLM false-heal rate is.

---

## 4. Signal-by-signal mapping

| `UiElementInfo` field | AT-SPI2 source | Quality |
| :--- | :--- | :--- |
| `ControlType` | `GetRoleName()` (string) or `GetRole()` (`AtspiRole` uint — stable, prefer this) | Good. Vocabulary differs from UIA — a mapping table is needed (§7). |
| `Name` | `Accessible.Name` property | Good. Set on interactive elements, empty on most containers — same as UIA. |
| `AutomationId` | `Accessible.AccessibleId` (→ almost always `""`); fallback `GetAttributes()["id"]` (→ unstable) | **Poor. See §3.** |
| `ClassName` | `GetAttributes()["class"]` | Partial. Chromium gives the C++ `View` class (`ToolbarView`, `FrameCaptionButton`) — coarse but stable-ish. GTK sometimes gives the widget type name. Absent on many nodes. |
| `BoundingRectangle` | `Component.GetExtents(coordType)` → `(x,y,w,h)` | Good on X11. On Wayland, screen-global coordinates may be zeroed for security — use `coordType = 1` (window-relative). Many `(0,0,0,0)` for offscreen/collapsed nodes — the engine already excludes unusable rects. |
| `ParentControlType` | `Accessible.Parent` → resolve → `GetRoleName()` | Good (one extra round trip per node, or derive during the walk). |
| `ParentAutomationId` | parent's `AccessibleId` | Poor (same as §3). |
| `SiblingIndex` | `GetIndexInParent()` | Good. |
| `SiblingCount` | parent's `ChildCount` | Good. |
| filtering (visible / enabled) | `GetState()` bitmask — `SHOWING`, `VISIBLE`, `ENABLED`, `FOCUSABLE` | Good — maps onto `DiscoveryOptions` filtering. |

---

## 5. Performance: the `Cache` interface is toolkit-dependent

The synthetic benchmark expects tree capture in the tens of milliseconds for thousands of controls. A naïve
AT-SPI walk needs ~5–6 D-Bus round trips **per node** (role, name, id, attributes, extents, index) — a
3,000-node tree is 15,000–18,000 round trips, which is seconds, not milliseconds.

AT-SPI2 has a batch primitive for exactly this: **`org.a11y.atspi.Cache.GetItems()`** on `/org/a11y/atspi/cache`,
which returns a whole subtree in one call (`a((so)(so)(so)a(so)assusau)` — ref, app, parent, children, ifaces,
name, role, description, state). GTK/ATK apps implement it.

**Chromium/Electron do not.** `Cache.GetItems` on a Chromium connection returns *method doesn't exist*. That
matters because Electron apps — VS Code, Slack, Spotify, Discord, Postman — are a large share of the Linux
desktop apps a test suite would target. For those, the backend is back to per-node round trips and must lean on
aggressive `DiscoveryOptions` limits (`MaxDepth`, `MaxElements`, `Timeout`) and parallel D-Bus calls.

---

## 6. Toolkit & display-server coverage

| Toolkit | AT-SPI coverage | Notes |
| :--- | :--- | :--- |
| GTK 3 / GTK 4 | Native (ATK / GTK 4 accessibility) | Best case. Implements `Cache`. `AccessibleId` only on GTK 4.10+ and only if the app sets it. |
| Qt 5 / Qt 6 | Good (`QAccessible` bridge) | Generally faithful roles and extents. |
| Chromium / Electron | Good tree, rich `class`, but no `Cache`, no `AccessibleId`, ordinal `id` | The dominant modern case; also the weakest for stable identity. |
| Java / Swing | Via `java-atk-wrapper` | Often not installed; unreliable. |
| Flutter (Linux), raw X11, Electron with a11y disabled | None or opt-in | Needs an accessibility env flag / app flag before start; capture otherwise returns an empty or shallow tree. |

**Display server.** X11: screen-global extents work. Wayland: global coordinates are often unavailable (a
deliberate isolation boundary); the backend must use window-relative coordinates and cannot assume one global
coordinate space across windows.

**Prerequisite.** AT-SPI must be *running* — `at-spi2-registryd` and the a11y bus. On a headless CI box this
means launching `at-spi-bus-launcher` under a D-Bus session and setting the accessibility env
(`ACCESSIBILITY_ENABLED=1`, `GTK_MODULES`, `QT_ACCESSIBILITY=1`) before the target app starts, or its tree never
appears.

---

## 7. Backend sketch

Mirrors `AutomationSandbox.Discovery` (Windows) so the healing engine sees the same `UiElementInfo`:

- **New package `AutomationSandbox.LinuxDiscovery`**, `net8.0` (Linux). `UiModel` stays dependency-free; this
  package takes the `Tmds.DBus` dependency, `Discovery` keeps FlaUI.
- **`AtSpiApplicationConnector`** — resolves the a11y bus (`org.a11y.Bus.GetAddress`), connects, finds the target
  app's root under the `Registry` (by `Application` PID or `Name`).
- **`AtSpiTreeWalker`** — the analogue of `UiTreeWalker`: from a root ref, per node fetch
  `{ GetRole, Name, AccessibleId, GetAttributes, Component.GetExtents, GetIndexInParent, ChildCount }`, honour
  `DiscoveryOptions`, build `UiElementInfo`. Use `Cache.GetItems` when the app implements it; fall back to
  per-node with bounded concurrency when it does not.
- **`AtSpiRoleMap`** — `AtspiRole` (uint) → the ControlType vocabulary the scorer and existing fixtures use.
  Observed pairs from the live walk: `75 application`, `23 frame`, `39 panel`, `43 button`, `63 tool bar`,
  `38 page tab list`, `37 page tab`, `79 entry`, `51 slider`, `50 separator`, `116 static`, `95 document web`,
  `101 notification`, `85 section`. Decide whether to normalise toward the UIA names (`panel`→`Pane`,
  `static`→`Text`, `page tab`→`TabItem`, `entry`→`Edit`) so cross-platform fixtures share a vocabulary, or keep
  AT-SPI names and let the scorer compare like-for-like within a platform.
- **No new public API in `SelfHealing`** — `SelfHealingEngine.ExecuteWithHealingAsync` already takes a
  `Func<UiElementInfo> captureTreeRoot`; the Linux connector just supplies it.

---

## 8. Recommendation

1. Not beta-blocking; stays `P2` / research until there is demand.
2. Before any shipped backend, run a **measurement spike**: capture real trees from a GTK app and an Electron app,
   feed them through `LocatorAblationHarness` / `TreeCalibrator`, and publish the false-heal / precision numbers
   the way `benchmark-calibration.md` does for HandBrake and ShareX. The missing `AutomationId` is the variable
   to quantify.
3. If the numbers hold up, ship `AutomationSandbox.LinuxDiscovery` as an explicitly **experimental** package,
   documenting the identity gap and the Electron performance caveat up front.

## See also

- [Benchmark & Calibration](benchmark-calibration.md) — why the structural-only regime is the hard one
- [Desktop Automation](desktop-automation.md) — the Windows (FlaUI/UIA3) path this would mirror
- [Documentation Hub](index.md)
