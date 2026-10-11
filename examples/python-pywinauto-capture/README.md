# Python/pywinauto Capture Example / Python/pywinauto Yakalama Örneği

A reference Python + [pywinauto](https://github.com/pywinauto/pywinauto) script that captures a Windows desktop application's UI tree as `UiElementInfo`-shaped JSON - the same input `automation-sandbox-heal` (`AutomationSandbox.Cli`) expects, for desktop locators instead of web ones. Uses pywinauto's `uia` backend, which wraps the same Windows UI Automation COM API `FlaUI`/`UIA3` uses - so this produces genuinely equivalent data, not just a matching JSON shape.

Gerçek bir Python + [pywinauto](https://github.com/pywinauto/pywinauto) betiği, bir Windows masaüstü uygulamasının UI ağacını `automation-sandbox-heal`'in (`AutomationSandbox.Cli`) beklediği aynı JSON şeklinde (`UiElementInfo`) yakalar - web locator'ları yerine masaüstü locator'ları için. pywinauto'nun `uia` backend'ini kullanır, bu da `FlaUI`/`UIA3`'ün kullandığı aynı Windows UI Automation COM API'sini sarmalar - yani burada üretilen veri sadece eşleşen bir JSON şekli değil, gerçekten eşdeğer veridir.

---

**Windows-only, and explicitly not locally verified by the assistant that wrote it** - pywinauto's `uia` backend needs real Windows UI Automation COM APIs, which this sandbox doesn't have (the same reason `samples/DesktopAppExplorerQuickstart` carries the same caveat). The pywinauto API used here was confirmed against its actual source code, not guessed, but this script's first real execution was in CI (`ci.yml`'s `Desktop Capture (Python)` job on `windows-latest`), not on this machine.

**Yalnızca Windows'ta çalışır, ve bunu yazan asistan tarafından yerel olarak doğrulanmamıştır** - pywinauto'nun `uia` backend'i, bu sandbox'ta bulunmayan gerçek Windows UI Automation COM API'lerine ihtiyaç duyar (`samples/DesktopAppExplorerQuickstart`'ın taşıdığı aynı uyarı, aynı sebeple). Burada kullanılan pywinauto API'si gerçek kaynak koduna karşı doğrulandı, tahmin edilmedi, ama bu betiğin ilk gerçek çalıştırılması bu makinede değil, CI'da oldu (`ci.yml`'deki `Desktop Capture (Python)` job'ı, `windows-latest` üzerinde).

---

## Install / Kurulum

```bash
cd examples/python-pywinauto-capture
pip install -r requirements.txt
```

## Use / Kullanım

Capture a full tree (for `--tree`) by launching a fresh instance / Taze bir örnek başlatıp tam bir ağaç yakalayın (`--tree` için):

```bash
python capture.py "C:\path\to\YourApp.exe" tree.json
```

Capture a single element (for `--expected`) by its exact `Name` / Tam `Name`'ine göre tek bir eleman yakalayın (`--expected` için):

```bash
python capture.py "C:\path\to\YourApp.exe" expected.json --name "Submit Order"
```

Attach to an already-running process instead of launching / Yeni bir örnek başlatmak yerine zaten çalışan bir işleme bağlanın:

```bash
python capture.py YourApp.exe tree.json --attach
```

Then heal with the CLI / Sonra CLI ile healing yapın:

```bash
automation-sandbox-heal --expected expected.json --tree tree.json --profile balanced
```

## How this was verified / Bunun nasıl doğrulandığı

CI builds the repository's own `WinFormsApp` demo, launches it fresh, captures its real UI tree with this exact script, then constructs a synthetic stale `--expected` from one real captured node (only its `AutomationId` changed - everything else, including `BoundingRectangle` and `ParentControlType`, stays as genuinely captured) and confirms `automation-sandbox-heal` heals it back with `IsConfident: true`. The same real-data technique the CLI's own desktop fixture (`txtEmail` → `textBox1`) uses, just captured live instead of hand-authored.

CI, deponun kendi `WinFormsApp` demo uygulamasını derler, taze bir örnek başlatır, bu tam betikle gerçek UI ağacını yakalar, sonra gerçekten yakalanan bir düğümden sentetik bir eski (`stale`) `--expected` oluşturur (sadece `AutomationId`'si değiştirilir - `BoundingRectangle` ve `ParentControlType` dahil geri kalan her şey gerçekten yakalanmış haliyle kalır) ve `automation-sandbox-heal`'in bunu `IsConfident: true` ile düzgünce iyileştirdiğini doğrular. CLI'nin kendi masaüstü fixture'ının (`txtEmail` → `textBox1`) kullandığı aynı gerçek-veri tekniği, sadece elle yazılmak yerine canlı yakalanmış hali.

## Why this one Python dependency exists / Bu tek Python bağımlılığı neden var

Same reasoning as `examples/js-playwright-capture`'s one `package.json`: the main repository is deliberately all-C#, with no JavaScript/Python/Rust tooling for the product itself (see `AGENTS.md`). This example is a second narrow, explicit exception - it exists specifically to serve non-.NET callers on the desktop side, so it necessarily needs its own Python tooling. It does not change anything about the product's own runtime dependencies.

`examples/js-playwright-capture`'ın tek `package.json`'ıyla aynı gerekçe: ana depo bilinçli olarak tamamen C#'tır, ürünün kendisi için JavaScript/Python/Rust aracı yoktur (bkz. `AGENTS.md`). Bu örnek, ikinci dar ve açıkça belirtilmiş bir istisnadır - özellikle masaüstü tarafında .NET dışı çağıranlara hizmet etmek için var, bu yüzden kendi Python araçlarına ihtiyaç duyuyor. Ürünün kendi çalışma zamanı bağımlılıkları konusunda hiçbir şeyi değiştirmiyor.
