# Playwright End-to-End Self-Healing & Safety Sample / Playwright Uçtan Uca Self-Healing ve Güvenlik Örneği

This .NET 8 console sample launches real headless Chromium via the `Microsoft.Playwright` .NET SDK to demonstrate the full self-healing and false-heal avoidance pipeline across two versions of an actual web application (`v1.html` and `v2.html`).

Bu .NET 8 console örneği, `Microsoft.Playwright` .NET SDK kullanarak gerçek bir headless Chromium başlatır ve iki uygulama sürümü (`v1.html` ve `v2.html`) üzerinde uçtan uca self-healing ve false-heal önleme hattını gösterir.

---

## What this scenario demonstrates / Bu senaryo neleri gösterir

1. **Live DOM Capture & Baseline**:
   Explores `v1.html` and captures the baseline DOM structure for two locators:
   - `Checkout.SubmitButton` (`#btn-checkout`, "Proceed to Checkout")
   - `Checkout.ApplyPromoButton` (`#btn-apply-promo`, "Apply Discount")
2. **Web App Refactoring (`v2.html`)**:
   - The checkout button was refactored, moved, and renamed to `#btn-complete-order` ("Complete Order").
   - The manual promo code button `#btn-apply-promo` was **deleted** (coupons are now auto-applied on input).
3. **Safe Healing & False-Heal Prevention**:
   - `Checkout.SubmitButton` is safely healed to `#btn-complete-order` using structural similarity scoring.
   - `Checkout.ApplyPromoButton` is **correctly declined** by the batch ownership reconciliation guard, preventing a false heal against the unrelated submit button and routing the deleted element to manual review.
4. **Report & History Artifacts**:
   - Generates an interactive `healing-report.html` and `healing-report.json`.
   - Persists the updated locator state in `locators.json`.

---

## How to run / Nasıl çalıştırılır

Single command from repository root / Depo kökünden tek bir komutla:

```bash
dotnet run --project samples/PlaywrightEndToEndQuickstart
```

Or via PowerShell verification script / Veya PowerShell doğrulama betiği ile:

```powershell
pwsh ./samples/PlaywrightEndToEndQuickstart/verify.ps1
```
