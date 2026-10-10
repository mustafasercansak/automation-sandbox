# Desktop App Explorer Quickstart / Masaüstü Uygulama Keşif Hızlı Başlangıcı

This .NET 8 console sample points `Discovery`'s `ApplicationConnector` + `UiTreeWalker` at **any** real Windows desktop application - .NET Framework (WinForms) or modern .NET (WinForms/WPF), your choice, since FlaUI/UIA3 automates through Windows UI Automation regardless of the target app's runtime - and reports its UI tree structure and locator health.

Bu .NET 8 console örneği, `Discovery` paketinin `ApplicationConnector` + `UiTreeWalker`'ını **herhangi bir** gerçek Windows masaüstü uygulamasına yöneltir - .NET Framework (WinForms) veya modern .NET (WinForms/WPF), farketmez, çünkü FlaUI/UIA3 hedef uygulamanın çalışma zamanından bağımsız olarak Windows UI Automation üzerinden otomasyon yapar - ve UI ağacının yapısını ve locator sağlığını raporlar.

---

**CI runtime-verifies this sample on every PR** (`Desktop App Explorer Sample` in `ci.yml`,
#494/#501): it runs the built sample against four genuinely external, third-party-authored
applications, not this repository's own `WinFormsApp`/`WpfApp` demos. Two ship with every
`windows-latest` runner, no download needed - Character Map (`charmap.exe`) and Registry Editor
(`regedit.exe`). Two more are downloaded fresh each run from their own GitHub Releases, pinned to
a specific version - Notepad++ and PeaZip, both real native Win32 apps with richer UI trees
(tabbed documents, toolbars, tree/list views) than the two built-in tools. That's real evidence
`ApplicationConnector` and `UiTreeWalker` work against arbitrary external UIA applications across
a varied pool, not just against apps this repository built and controls. What CI still does
**not** cover: an arbitrary third-party application at your own workplace, with its own quirks (a
wrapped web view, an unusually deep tree, a slow-loading main window) - that verification is
tracked separately by [#489](https://github.com/mustafasercansak/automation-sandbox/issues/489)
and stays open until someone runs it there and reports back.

**Bu örnek her PR'da CI tarafından çalışma zamanında doğrulanır** (`ci.yml`'deki
`Desktop App Explorer Sample`, #494/#501): derlenen örneği, bu deponun kendi
`WinFormsApp`/`WpfApp` demoları olmayan dört gerçek, üçüncü parti uygulama üzerinde çalıştırır.
İkisi her `windows-latest` runner'ında hazır gelir, indirme gerektirmez - Character Map
(`charmap.exe`) ve Registry Editor (`regedit.exe`). Diğer ikisi her çalıştırmada kendi GitHub
Releases sayfalarından, belirli bir sürüme sabitlenmiş olarak indirilir - Notepad++ ve PeaZip,
ikisi de her iki dahili araçtan daha zengin UI ağaçlarına (sekmeli belgeler, araç çubukları,
ağaç/liste görünümleri) sahip gerçek native Win32 uygulamaları. Bu, `ApplicationConnector` ve
`UiTreeWalker`'ın yalnızca bu deponun inşa edip kontrol ettiği uygulamalara karşı değil, çeşitli
bir havuzda gerçekten rastgele dış UIA uygulamalarına karşı da çalıştığını kanıtlar. CI'nin hâlâ
kapsamadığı şey: kendi işyerindeki, kendine has tuhaflıkları olan (sarmalanmış bir web görünümü,
alışılmadık derinlikte bir ağaç, yavaş yüklenen bir ana pencere) rastgele bir üçüncü parti
uygulama - bu doğrulama ayrıca
[#489](https://github.com/mustafasercansak/automation-sandbox/issues/489) tarafından takip edilir
ve biri orada çalıştırıp sonucu bildirene kadar açık kalır.

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

CI proves this sample works against two real built-in Windows apps, but your own target
application is still untested territory - if it throws, that is useful information, not a failure
to hide. Common things worth checking: the target app is a native Win32/WinForms/WPF app (not a
wrapped web view with no real UIA tree), the process name for `--attach` matches exactly what
Task Manager shows (no `.exe`), and the app's main window is fully loaded before the 15-second
discovery timeout.

CI, bu örneğin dahili gelen iki gerçek Windows uygulamasına karşı çalıştığını kanıtlıyor, ama kendi
hedef uygulamanız hâlâ test edilmemiş bir alan - hata verirse bu saklanacak bir başarısızlık değil,
faydalı bir bilgidir. Kontrol etmeye değer yaygın noktalar: hedef uygulama gerçek bir
Win32/WinForms/WPF uygulaması mı (gerçek bir UIA ağacı olmayan sarmalanmış bir web görünümü
değil), `--attach` için verilen işlem adı Görev Yöneticisi'nde görünenle birebir eşleşiyor mu
(`.exe` olmadan), ve uygulamanın ana penceresi 15 saniyelik keşif zaman aşımından önce tam olarak
yüklendi mi.
