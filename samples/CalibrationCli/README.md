# Calibration CLI / Kalibrasyon CLI'ı

A single command that runs `TreeCalibrator` against a captured UI tree and
recommends a `ThresholdProfile` for that application, without writing any
C# code. / Yakalanmış bir UI ağacına karşı `TreeCalibrator`'ı çalıştırıp o
uygulama için bir `ThresholdProfile` öneren, C# kodu yazmayı gerektirmeyen
tek bir komut.

## How to run / Nasıl çalıştırılır

```bash
dotnet run --project samples/CalibrationCli -- <tree.json> [--app <name>] [--max-probed <n>] [--out <path>]
```

- `<tree.json>` — a captured `UiElementInfo` tree, serialized with
  `UiModel.UiTreeSerializer` (the same format `Discovery`/`WebDiscovery`
  snapshots and the repository's own test fixtures use). / `UiModel.UiTreeSerializer`
  ile serileştirilmiş, yakalanmış bir `UiElementInfo` ağacı (`Discovery`/`WebDiscovery`
  anlık görüntülerinin ve reponun kendi test fixture'larının kullandığı formatın aynısı).
- `--app <name>` — application name shown in the report (default: the file
  name). / raporda gösterilecek uygulama adı (varsayılan: dosya adı).
- `--max-probed <n>` — max number of elements to probe (default: `50`). /
  problanacak maksimum eleman sayısı (varsayılan: `50`).
- `--out <path>` — where to write the markdown report (default:
  `<app>-calibration-report.md` in the current directory). / markdown
  raporunun yazılacağı yol (varsayılan: geçerli dizinde `<app>-calibration-report.md`).

## Example / Örnek

```bash
dotnet run --project samples/CalibrationCli -- TestAutomation/ScenarioRunner/Fixtures/HandBrake_1.8.2.tree.json --app HandBrake
```

Prints the markdown report to the console and writes it to
`HandBrake-calibration-report.md`. The report is self-contained: a
recommended profile, the reasoning behind it, and a precision/recall/
false-heal comparison table across `Aggressive`/`Balanced`/`Conservative` -
no need to read `docs/benchmark-calibration.md` first. / Markdown raporunu
konsola basar ve `HandBrake-calibration-report.md` dosyasına yazar. Rapor
kendi başına yeterlidir: önerilen bir profil, bunun gerekçesi, ve
`Aggressive`/`Balanced`/`Conservative` arasında bir precision/recall/
false-heal karşılaştırma tablosu - önce `docs/benchmark-calibration.md`'ı
okumaya gerek yoktur.

See [docs/benchmark-calibration.md](../../docs/benchmark-calibration.md#preset-profiles--application-tree-calibration)
for what the profiles mean and how the calibrator's synthetic probes work.
