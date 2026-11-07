---
layout: default
title: How to Write Playwright Locators That Survive an Upgrade - Automation Sandbox
---

# How to Write Playwright Locators That Survive an Upgrade

*Five habits. Each one is backed by a measurement on two real releases of Swagger UI.*

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English](#-english)
> - [🇹🇷 Türkçe](#-türkçe)

---

## 🇬🇧 English

### In short

A locator is the way your test finds an element on the page. When the page changes, a locator can stop working. Worse, it can keep working on the wrong element.

We tried ten ways to find one button, before and after an upgrade. Seven worked before. After the upgrade, only **one** was really safe. The five habits below come from that result.

### What we tested

We took two real releases of Swagger UI: 4.19.1 and 5.17.14. They are the unchanged npm packages, pinned by hash. We wanted one button: the one that opens the `GET /pets` operation. We wrote ten locators for it.

A locator is **valid** if, in the old release, it finds exactly that button and nothing else. Seven were valid. Here is what they did in the new release:

| Result in 5.17.14 | Locators |
| :--- | ---: |
| Still right, and not by luck | **1** |
| Still right, but only because the right button comes first on the page | **3** |
| Failed with an error (Playwright refused to act on two matches) | **2** |
| **Picked a different button, with no error** | **1** |

What changed in the page? In 4.19.1 the button had the name `get /pets`. In 5.17.14 that name moved to a **new** button next to it. The old button got a new name: "GET /pets List pets". So any search by name now finds two buttons.

### Habit 1: Ask for exactly one match

If your locator can match more than one element, it is not finished. Check the count. It should be 1, on the old page and on the new page.

*Evidence:* three locators looked fine after the upgrade. Without `.first()` or `.nth(0)` they matched 3, 6 and 3 elements. They only worked because the right element was first.

### Habit 2: Treat `.first()` and `.nth()` as a warning

They turn "I found several" into "I picked one". Playwright's strict mode would have stopped you. These calls switch it off. Use them only when you can say why the first one is the right one, and write that reason in a comment.

*Evidence:* one locator ended in `.first()`. After the upgrade it picked the new look-alike button. No error. The test kept running.

### Habit 3: Narrow the search with something stable

Look for a container that keeps the same id across versions. Then search inside it for the role of the element.

*Evidence:* the one safe locator was `#operations-pets-get_pets .opblock-summary-control`. It found exactly one button in both releases, in any order.

### Habit 4: Find elements the way a user sees them, and let it fail loudly

Playwright's own guidance is to prefer role and name, or test ids that you add on purpose. When the name moves, these locators fail with an error. That is good. A red test is better than a quiet wrong click.

*Evidence:* the two `getByRole` locators failed with a strict-mode error after the upgrade. Someone would have looked. We did not measure test ids, because this page has none. That advice is Playwright's, not ours.

### Habit 5: Check that you touched the right element

A green test does not prove you clicked the right thing. Add one check that only the right element can pass: its own name, its keyboard focus, or the state only it changes.

*Evidence:* the wrong button also opened the operation. A test that only checks "the operation is open" cannot tell the two apart. The wrong button has `tabindex="-1"`, so it is not reachable by keyboard. A focus test would have caught it.

### Before you merge an upgrade

Count the matches of every locator on the old page and on the new page:

```js
for (const [name, make] of Object.entries(locators)) {
  console.log(name, await make(page).count());   // should be 1 on both pages, without .first() or .nth()
}
```

Anything that is not 1 will fail soon, or is already hiding behind `.first()`. Also search your tests for `.first()` and `.nth(`.

### The full result

This is the output of the script, with long lines wrapped for reading:

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

### What we did *not* measure

- **One program, one upgrade.** Swagger UI 4.19.1 to 5.17.14. Other pages behave differently.
- **Ten locators that we chose.** They are plausible, but they are not a sample of real test suites. We do not know how common each one is.
- **"Valid" is our definition:** exactly one match, and it is the right button, found by its class inside the operation block.
- **The click effect was checked on this page only.**
- **Browser and tools:** Chrome 156 through Playwright (declared range `^1.64.0`) on Linux, the same machine and browser as the dataset's `capture-environment.json`. The script itself does not record its environment.

### Run it yourself

```bash
cd examples/js-playwright-capture
npm install
node locator-survival.mjs ../../TestAutomation/ScenarioRunner/Fixtures/organic-drift/swagger-ui/manifest.json --channel chrome
```

A release whose hash does not match the manifest is refused. `--channel chrome` uses an installed Chrome. Without it, run `npx playwright install chromium` first.

---

## 🇹🇷 Türkçe

### Kısaca

Locator, testin sayfada bir elemanı bulma yöntemidir. Sayfa değişince bir locator çalışmayı bırakabilir. Daha kötüsü, yanlış elemanda çalışmaya devam edebilir.

Bir butonu bulmak için on yöntemi, yükseltmeden önce ve sonra denedik. Yedisi önce çalıştı. Yükseltmeden sonra yalnızca **biri** gerçekten sağlamdı. Aşağıdaki beş alışkanlık bu sonuçtan çıkıyor.

### Ne denedik

Swagger UI'nin iki gerçek sürümünü aldık: 4.19.1 ve 5.17.14. Değiştirilmemiş npm paketleri, hash ile sabitlenmiş. `GET /pets` operasyonunu açan butonu istedik ve bunun için on locator yazdık.

Eski sürümde **tam o butonu** bulan locator "geçerli" sayıldı. Yedisi geçerliydi. Yeni sürümde şunları yaptılar:

| 5.17.14'teki sonuç | Locator |
| :--- | ---: |
| Hâlâ doğru, şansa bağlı değil | **1** |
| Hâlâ doğru, ama yalnızca doğru buton sayfada önce geldiği için | **3** |
| Hata verdi (Playwright iki eşleşmede işlem yapmayı reddetti) | **2** |
| **Hata vermeden başka bir buton seçti** | **1** |

Sayfada ne değişti? 4.19.1'de butonun adı `get /pets` idi. 5.17.14'te bu ad, yanındaki **yeni** bir butona taşındı. Eski butonun adı "GET /pets List pets" oldu. Yani isimle arayan herkes artık iki buton buluyor.

### Alışkanlık 1: Tam bir eşleşme isteyin

Locator'ınız birden fazla elemana uyabiliyorsa bitmemiştir. Sayıyı kontrol edin. Eski sayfada da yeni sayfada da 1 olmalı.

*Kanıt:* üç locator yükseltmeden sonra iyi görünüyordu. `.first()` ya da `.nth(0)` olmadan 3, 6 ve 3 elemanla eşleşiyorlardı. Yalnızca doğru eleman ilk sırada olduğu için çalıştılar.

### Alışkanlık 2: `.first()` ve `.nth()` bir uyarıdır

"Birkaç tane buldum" cümlesini "birini seçtim" yapar. Playwright'ın strict modu sizi durdururdu. Bu çağrılar onu kapatır. Yalnızca ilkinin neden doğru olduğunu söyleyebiliyorsanız kullanın ve nedenini yoruma yazın.

*Kanıt:* bir locator `.first()` ile bitiyordu. Yükseltmeden sonra yeni benzer butonu seçti. Hata yok. Test çalışmaya devam etti.

### Alışkanlık 3: Aramayı sabit bir şeyle daraltın

Sürümler arasında aynı kalan bir kapsayıcı id'si bulun. Sonra elemanın rolünü onun içinde arayın.

*Kanıt:* tek sağlam locator `#operations-pets-get_pets .opblock-summary-control` idi. İki sürümde de, sıradan bağımsız, tam bir buton buldu.

### Alışkanlık 4: Elemanı kullanıcının gördüğü gibi bulun ve gürültüyle başarısız olmasına izin verin

Playwright'ın kendi tavsiyesi rol ve ad, ya da bilerek eklediğiniz test id'leridir. Ad taşınınca bu locator'lar hata verir. Bu iyidir. Kırmızı bir test, sessiz bir yanlış tıklamadan iyidir.

*Kanıt:* iki `getByRole` locator yükseltmeden sonra strict-mode hatası verdi. Biri bakardı. Test id'lerini ölçmedik, çünkü bu sayfada yok. Bu tavsiye Playwright'ın, bizim değil.

### Alışkanlık 5: Doğru elemana dokunduğunuzu kontrol edin

Yeşil test, doğru şeye tıkladığınızı kanıtlamaz. Yalnızca doğru elemanın geçebileceği bir kontrol ekleyin: kendi adı, klavye odağı ya da yalnızca onun değiştirdiği durum.

*Kanıt:* yanlış buton da operasyonu açtı. Yalnızca "operasyon açık" diye bakan bir test ikisini ayıramaz. Yanlış butonda `tabindex="-1"` var, klavyeyle ulaşılamıyor. Bir odak testi bunu yakalardı.

### Bir yükseltmeyi birleştirmeden önce

Her locator'ın eşleşme sayısını eski sayfada ve yeni sayfada sayın (yukarıdaki kod). 1 olmayan her şey yakında hata verecek ya da zaten `.first()` arkasına saklanıyor. Testlerinizde `.first()` ve `.nth(` de arayın.

### Ölçmediklerimiz

Tek program, tek yükseltme (Swagger UI 4.19.1'den 5.17.14'e). On locator'ı biz seçtik, gerçek test paketlerinin örneği değil, hangisinin ne kadar yaygın olduğunu bilmiyoruz. "Geçerli" tanımı bizim. Tıklama etkisi yalnızca bu sayfada denendi. Yeniden üretmek için İngilizce bölümdeki komutu çalıştırın.

---

## See also

- [External Validation](../external-validation.md) · the organic drift dataset behind the two releases
- [Can You Trust an LLM to Fix a Broken Locator?](llm-false-heal-study.md) · the measured limits of LLM-based healing
- [CLI & CI Integration](../cli-and-ci-integration.md) · `automation-sandbox-heal`, which can propose a replacement for a broken locator and declines when two candidates are too close. It is our tool, and this measurement is not an independent evaluation of it.
- [Documentation Hub](../index.md)
