# Desktop App Explorer Quickstart / Masaüstü Uygulama Keşif Hızlı Başlangıcı

This .NET 8 console sample points `Discovery`'s `ApplicationConnector` + `UiTreeWalker` at **any** real Windows desktop application - .NET Framework (WinForms) or modern .NET (WinForms/WPF), your choice, since FlaUI/UIA3 automates through Windows UI Automation regardless of the target app's runtime - and reports its UI tree structure and locator health.

Bu .NET 8 console örneği, `Discovery` paketinin `ApplicationConnector` + `UiTreeWalker`'ını **herhangi bir** gerçek Windows masaüstü uygulamasına yöneltir - .NET Framework (WinForms) veya modern .NET (WinForms/WPF), farketmez, çünkü FlaUI/UIA3 hedef uygulamanın çalışma zamanından bağımsız olarak Windows UI Automation üzerinden otomasyon yapar - ve UI ağacının yapısını ve locator sağlığını raporlar.

---

**Unlike this repository's other samples, this one has not been run end-to-end by the assistant
that wrote it - it was written and compile-verified (`dotnet build`, cross-targeting
`net8.0-windows` from Linux via `EnableWindowsTargeting`) but never executed, because FlaUI/UIA3
requires real Windows UI Automation COM APIs that only exist on Windows. Run it on a Windows
machine and treat the first run as a real test of this sample, not just of your application.**

**Bu depodaki diğer örneklerin aksine, bu örnek onu yazan asistan tarafından uçtan uca
çalıştırılmadı** - yazıldı ve derleme seviyesinde doğrulandı (`dotnet build`, Linux'tan
`EnableWindowsTargeting` ile `net8.0-windows` çapraz hedeflemesi) ama hiç çalıştırılmadı, çünkü
FlaUI/UIA3 yalnızca Windows'ta bulunan gerçek Windows UI Automation COM API'lerini gerektirir.
Windows makinede çalıştırın ve ilk çalıştırmayı sadece uygulamanızın değil, bu örneğin de gerçek
bir testi olarak değerlendirin.

---

## What this reports / Bu neyi raporlar

1. **Control type breakdown** - how many of each `ControlType` (Button, Edit, Pane, ...) the walk found.
2. **Locator health** - how many elements have no `AutomationId` at all (name/position-based matching is the only option for these), and which `AutomationId` values are shared by more than one element (a duplicate ID is a broken locator waiting to happen - see `WinFormsApp`'s own `panel1` case study in the main README).
3. **A full JSON snapshot** (`desktop-app-snapshot.json`, written to the current directory) you can use as a baseline `expected` `UiElementInfo` for `SelfHealingEngine`/`SelfHealingResolver` - see [Getting Started](../../docs/getting-started.md) and [Desktop Automation](../../docs/desktop-automation.md).

---

## How to run / Nasıl çalıştırılır

Requires Windows and a Debug (or Release) build of the target application already on disk, or a
process already running.

Launch a fresh instance and explore it / Yeni bir örnek başlatıp keşfedin:

```powershell
dotnet run --project samples/DesktopAppExplorerQuickstart -- "C:\path\to\YourApp.exe"
```

Attach to an already-running instance by process name (no `.exe`) / Zaten çalışan bir örneğe
işlem adıyla bağlanın (`.exe` uzantısı olmadan):

```powershell
dotnet run --project samples/DesktopAppExplorerQuickstart -- --attach YourApp
```

## If something goes wrong / Bir şeyler ters giderse

This is genuinely unverified against a real app - if it throws, that is useful information, not
a failure to hide. Common things worth checking: the target app is a native Win32/WinForms/WPF
app (not a wrapped web view with no real UIA tree), the process name for `--attach` matches
exactly what Task Manager shows (no `.exe`), and the app's main window is fully loaded before the
15-second discovery timeout.

Bu, gerçek bir uygulamaya karşı gerçekten doğrulanmamış durumda - hata verirse bu saklanacak bir
başarısızlık değil, faydalı bir bilgidir. Kontrol etmeye değer yaygın noktalar: hedef uygulama
gerçek bir Win32/WinForms/WPF uygulaması mı (gerçek bir UIA ağacı olmayan sarmalanmış bir web
görünümü değil), `--attach` için verilen işlem adı Görev Yöneticisi'nde görünenle birebir eşleşiyor
mu (`.exe` olmadan), ve uygulamanın ana penceresi 15 saniyelik keşif zaman aşımından önce tam
olarak yüklendi mi.
