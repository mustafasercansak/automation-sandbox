---
layout: default
title: Which Playwright Locators Survive a Release Upgrade? - Automation Sandbox
---

# Which Playwright Locators Survive a Release Upgrade?

*A measurement on two real releases of Swagger UI: how ten common locator styles behave when the page changes, and which ones fail without telling you.*

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English](#-english)
> - [🇹🇷 Türkçe Özet](#-türkçe-özet)

---

## 🇬🇧 English

### TL;DR

We aimed ten common locator styles at one control in Swagger UI 4.19.1: the button that expands the `GET /pets` operation. Seven found exactly that control. After upgrading the page to Swagger UI 5.17.14:

| Result in 5.17.14 | Locators |
| :--- | ---: |
| Still right, for a reason that does not depend on luck | **1** |
| Still right, only because the right element comes first in the DOM | **3** |
| Fail visibly (Playwright's strict mode refuses to act on two matches) | **2** |
| **Silently bound to a different element** | **1** |

The silent one kept working: clicking the wrong element still opened the operation, so a test that asserts "the operation is open" would have stayed green. Seven locators that worked, one that was robust.

### 1. What changed in the page

In 4.19.1 the button that expands an operation carries `aria-label="get /pets"`. In 5.17.14 that `aria-label` moved to a **new** button (class `opblock-control-arrow`, `tabindex="-1"`), and the original button now takes its accessible name from its content: "GET /pets List pets". Two buttons now answer to a name-based query, and the new one is a look-alike of the old one, not the same control.

Nothing was edited to create this. The releases are the unmodified `swagger-ui-dist` npm packages, pinned by SHA-256 and npm integrity in the manifest of [the organic drift dataset](../external-validation.md), and the page that mounts them is a fixed probe with the OpenAPI document inline.

### 2. Method

The script [`locator-survival.mjs`](https://github.com/mustafasercansak/automation-sandbox/blob/main/examples/js-playwright-capture/locator-survival.mjs) downloads both releases, refuses them unless the hashes match, and runs ten locator styles against each. A locator is **valid** when, in the old release, it matches exactly one element and that element is the control. For each valid locator it then reports what it matches in the new release: the same control, no or several elements, or exactly one *different* element. For a locator that ends in `.first()` or `.nth(0)` it also reports how many elements the same selector matches without it, because such a locator never fails on "several matches".

### 3. Results

The output of the script, with long lines wrapped for reading:

```text
getByRole button, name "get /pets"          works in 4.19.1; in 5.17.14: fails visibly (strict mode: 2 matches)
getByRole button, name /get.*pets/i         works in 4.19.1; in 5.17.14: fails visibly (strict mode: 2 matches)
getByRole button, name /^GET \/pets/        not valid in 4.19.1 (matches 0)
getByText "List pets"                       not valid in 4.19.1 (matches 1, the wrong element)
CSS #operations-pets-get_pets .opblock-summary-control
                                            works in 4.19.1; in 5.17.14: still right
CSS #operations-pets-get_pets button  (.first())
                                            works in 4.19.1; in 5.17.14: still right (only because it comes first: the same selector without it matches 3)
CSS .opblock-summary-get button  (.first())
                                            works in 4.19.1; in 5.17.14: still right (only because it comes first: the same selector without it matches 6)
CSS button.opblock-summary-control  (.nth(0))
                                            works in 4.19.1; in 5.17.14: still right (only because it comes first: the same selector without it matches 3)
XPath //button[contains(@aria-label,"get")] not valid in 4.19.1 (matches 2)
CSS button[aria-label*="pets"]  (.first())  works in 4.19.1; in 5.17.14: SILENTLY WRONG (opblock-control-arrow; the same selector without .first() matches 2); clicking it still opens the operation

10 locator styles tried, 7 valid in 4.19.1. In 5.17.14: 4 still right (3 of them only because the right element comes first in the DOM), 2 fail visibly, 1 silently wrong.
```

The script prints each of these on one line. Two runs in a row printed the same text.

### 4. Three things to take from it

**1. Strict mode protects you; `.first()` and `.nth()` switch it off.** The two `getByRole` locators failed with a strict-mode error, which is the good outcome: the suite went red and someone looked. The same page change, seen through `button[aria-label*="pets"]` plus `.first()`, produced no error and a different element. Three more locators survived because the right element happens to come first, which is a fact about the DOM order of this release, not about the locator. Search your suite for `.first()` and `.nth(`, and for each ask why one match is not enough.

**2. A green test does not prove it touched the same control.** Clicking the look-alike button opened the operation, so an assertion on "the operation is open" cannot tell the two apart. The look-alike has `tabindex="-1"`, so a test of keyboard focus would behave differently. What the test checks decides whether the substitution is visible.

**3. The one robust locator combined a role-like class with a stable container.** `#operations-pets-get_pets .opblock-summary-control` matched exactly one element in both releases, whatever the order. The other locators were either too loose or leaned on position. Playwright's own guidance prefers user-facing locators such as role and name; here they failed loudly rather than silently, which is the property you want from a locator you cannot make unique.

### 5. Check your own suite

Compare how many elements each locator matches on the old and the new version of the page before you merge an upgrade:

```js
for (const [name, make] of Object.entries(locators)) {
  console.log(name, await make(page).count());   // 1 on both versions, without .first() or .nth()
}
```

Anything that is not 1 is either about to fail loudly or already hiding behind `.first()`. [`automation-sandbox-heal`](../cli-and-ci-integration.md), the engine this site documents, can propose a replacement for a broken locator and declines when two candidates are too close. That tool is ours, and this measurement is not an independent evaluation of it.

### 6. What this does *not* establish

- **One application pair.** Swagger UI 4.19.1 and 5.17.14, a major version apart. Other pages and other upgrades behave differently.
- **Ten locator styles chosen by the author.** They are plausible, not a sample of real suites. Nothing here says how common each style is, or how often a real upgrade moves a name the way this one did.
- **"Valid" is our definition:** exactly one match, and it is the control as identified by its class inside the operation block.
- **The click effect was checked on this page only.** That the look-alike opens the operation is a fact about this page, not a rule.
- **Browser and tools:** Chrome 156 through Playwright (declared range `^1.64.0`) on Linux, the same machine and browser as the dataset's `capture-environment.json`; the script itself does not record its environment.

### 7. Reproduce this yourself

```bash
cd examples/js-playwright-capture
npm install
node locator-survival.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/manifest.json --channel chrome
```

A release whose hash does not match the manifest is refused. `--channel chrome` uses an installed Chrome; without it, run `npx playwright install chromium` first.

---

## 🇹🇷 Türkçe Özet

### Kısaca

Swagger UI 4.19.1'de tek bir kontrole (`GET /pets` operasyonunu açan düğme) on yaygın locator stilini yönelttik. Yedisi tam olarak o kontrolü buldu. Sayfa Swagger UI 5.17.14'e geçince:

| 5.17.14'teki sonuç | Locator |
| :--- | ---: |
| Hâlâ doğru, şansa bağlı olmayan bir nedenle | **1** |
| Hâlâ doğru, ama yalnızca doğru eleman DOM'da önce geldiği için | **3** |
| Açık hata verdi (Playwright'ın strict modu iki eşleşmede işlem yapmayı reddeder) | **2** |
| **Sessizce başka bir elemana bağlandı** | **1** |

Sessiz olan çalışmaya devam etti: yanlış elemana tıklamak yine operasyonu açtı, yani "operasyon açık" diye doğrulayan bir test yeşil kalırdı. Çalışan yedi locator, sağlam olan bir tane.

### Ne değişti

4.19.1'de operasyonu açan düğmede `aria-label="get /pets"` var. 5.17.14'te bu etiket **yeni** bir düğmeye (`opblock-control-arrow`, `tabindex="-1"`) taşınmış, asıl düğme adını artık içeriğinden alıyor: "GET /pets List pets". İsimle sorgulayınca iki düğme eşleşiyor ve yenisi eskisinin aynısı değil, ona benzeyen başka bir kontrol. Hiçbir şey düzenlenmedi: sürümler değiştirilmemiş `swagger-ui-dist` npm paketleri, SHA-256 ile sabitlenmiş.

### Üç ders

1. **Strict mode sizi korur, `.first()` ve `.nth()` onu kapatır.** İki `getByRole` locator strict-mode hatasıyla düştü, bu iyi sonuç: test kırmızı oldu ve biri baktı. Aynı değişiklik `button[aria-label*="pets"]` ile `.first()` üzerinden hata vermeden başka bir eleman üretti. Üç locator daha, doğru eleman önce geldiği için hayatta kaldı, bu bir locator özelliği değil, bu sürümün DOM sırası. Testlerinizde `.first()` ve `.nth(` arayın ve her biri için "tek eşleşme neden yetmiyor" diye sorun.
2. **Yeşil test, aynı kontrole dokunduğunuzu kanıtlamaz.** Benzer düğmeye tıklamak operasyonu açtı, "operasyon açık" doğrulaması ikisini ayıramaz. Benzer düğmede `tabindex="-1"` var, klavye odağını test eden bir senaryo farklı davranırdı.
3. **Tek sağlam locator, rol gibi bir sınıfı sabit bir kapsayıcıyla birleştiriyordu:** `#operations-pets-get_pets .opblock-summary-control`, sıradan bağımsız olarak iki sürümde de tam bir eleman buldu.

### Kendi paketinizde kontrol edin

Bir yükseltmeyi birleştirmeden önce, her locator'ın eski ve yeni sayfada kaç eleman eşleştirdiğini karşılaştırın (`.first()` ya da `.nth()` olmadan). 1 olmayan her şey ya açık hata verecek ya da zaten `.first()` arkasına saklanıyor. Bu ölçüm bizim aracımızın bağımsız bir değerlendirmesi değil.

### Sınırlar

Tek uygulama çifti (bir ana sürüm arayla), yazarın seçtiği on stil (gerçek paketlerin örneği değil, hangisinin ne kadar yaygın olduğunu söylemez), "geçerli" tanımı bizim, tıklama etkisi yalnızca bu sayfada denendi. Yeniden üretmek için: `examples/js-playwright-capture/locator-survival.mjs`.

---

## See also

- [External Validation](../external-validation.md) · the organic drift dataset behind the two releases
- [Can You Trust an LLM to Fix a Broken Locator?](llm-false-heal-study.md) · the measured limits of LLM-based healing
- [CLI & CI Integration](../cli-and-ci-integration.md) · `automation-sandbox-heal`
- [Documentation Hub](../index.md)
