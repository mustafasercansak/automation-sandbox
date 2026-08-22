# Heuristic Healing Quickstart / Sezgisel Healing Hızlı Başlangıç

This .NET 8 console app consumes `AutomationSandbox.SelfHealing` **from nuget.org**. It
contains no `ProjectReference` back to this repository. The scenario starts with a stale
checkout-button locator, retries after an `ElementNotFoundException`, selects the renamed
candidate with the deterministic heuristic resolver, and verifies that the successful
retry was persisted.

Bu .NET 8 console uygulaması `AutomationSandbox.SelfHealing` paketini **nuget.org'dan**
tüketir; repository içindeki projelere `ProjectReference` içermez. Senaryo eski bir ödeme
butonu locator'ıyla başlar, `ElementNotFoundException` sonrasında yeniden dener, yeniden
adlandırılmış adayı deterministik sezgisel resolver ile seçer ve başarılı retry'ın kalıcı
olarak kaydedildiğini doğrular.

Run / Çalıştır:

```powershell
pwsh ./samples/HeuristicHealingQuickstart/verify.ps1
```

The verification script copies the project into an isolated temporary consumer directory,
restores into a fresh temporary package directory using only
`https://api.nuget.org/v3/index.json`, then builds and runs the sample. This keeps repository
build settings and cached packages out of the proof. No API key, LLM, browser, or Windows
desktop is required.

Doğrulama betiği projeyi yalıtılmış bir geçici consumer dizinine kopyalar, yalnızca
`https://api.nuget.org/v3/index.json` kaynağını kullanarak temiz bir geçici paket dizinine
restore eder, ardından örneği build edip çalıştırır. Böylece repository build ayarları ve
cache'lenmiş paketler kanıtın dışında kalır. API anahtarı, LLM, tarayıcı veya Windows
masaüstü gerekmez.
