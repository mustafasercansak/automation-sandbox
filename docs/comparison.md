---
layout: default
title: How Automation Sandbox Compares - Automation Sandbox
---

# How Automation Sandbox Compares / Automation Sandbox Nasıl Kıyaslanır

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

Automation Sandbox is not trying to be Ranorex or Tosca. It solves **one** part of the test-automation problem —
a locator broke, re-resolve it from structural evidence and explain the decision — as an open, auditable .NET
library. This page is for deciding whether that scope, and that approach, fit your situation.

Every claim about another product below is sourced from that product's own public documentation. Where a product
is the better fit, this page says so.

### At a glance

| | **Automation Sandbox** | **Healenium** | **Testim / Mabl / Functionize** | **Ranorex** |
| :--- | :--- | :--- | :--- | :--- |
| Stack | .NET (net48 / netstandard2.0 / net8 / net10) | Java + Selenium (JS SDK community) | SaaS, language-agnostic recorder | Windows IDE, .NET scripting |
| License / cost | MIT, free, self-hosted | Apache-2.0, free; needs a backend service + DB | Commercial subscription | Commercial per-seat licence |
| Healing approach | Deterministic structural heuristic **first**; LLM only as an opt-in, quorum-gated fallback | ML over historical DOM trees stored in a backend | Vendor ML model (black box) | Proprietary "RanoreXPath" weighting |
| Explainability | Per-decision JSON + HTML: every signal's weight, providers' votes, outcome | Healing report of before/after selectors | Limited; heal shown in run history | Limited |
| Data leaving your machine | Nothing, unless you configure an LLM — then a bounded shortlist with PII/secret redaction on by default | Selector data to your self-hosted backend | Full DOM / screenshots to vendor cloud | Local |
| Where locators live | Readable JSON in your repo, versioned, diffable | In the Healenium backend DB | Vendor cloud | Ranorex object repository (`.rxrep`) |
| Desktop support | Yes — FlaUI / UI Automation (Windows) | No (web only) | Mostly web; some desktop (vendor-specific) | Yes — strong desktop |
| What runs it | Your runner: xUnit, NUnit, Playwright, FlaUI, Reqnroll | Selenium tests | Vendor runner / grid | Ranorex Studio + Ranorex agent |
| Recorder / IDE / grid / scheduler | **None** — library only | None (heals existing Selenium tests) | Yes — full platform | Yes — full studio |

### What each approach optimises for

**Automation Sandbox** optimises for *trust in the individual heal*. The heuristic is deterministic, so the same
tree always produces the same decision, and every decision is reconstructable from the report. The LLM is boxed in
deliberately because [multi-provider agreement on a deleted element is unreliable](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97) —
34 of 34 unanimous verdicts in four measured runs were false heals. If you need to explain to an auditor *why* a
locator changed, this is the model built for that question.

**Healenium** optimises for *drop-in healing of an existing Selenium-Java suite*. If that is your stack, Healenium
is mature, widely used, and requires no change to how you write tests. The trade-offs are operational: you run and
maintain a backend service plus a database, healing history lives there rather than in your repo, and it is
web-only.

**Testim / Mabl / Functionize** optimise for *time-to-first-test and low-code authoring*. A recorder builds the
test, the vendor's model heals it, and a non-programmer can maintain it. You are buying a whole platform — grid,
scheduling, analytics, support — and accepting a black-box healer and your test data in the vendor's cloud.

**Ranorex** optimises for *desktop and mixed desktop/web automation with a full studio*. Its object repository and
path weighting are strong for Windows apps. It is a licensed IDE-centric product, not a library you compose into
your own code.

### When *not* to choose Automation Sandbox

- **You need a recorder, an IDE, an execution grid, or a scheduler.** This is a library. It does not run your
  tests. Ranorex Studio, Tosca, or a SaaS platform solve a much wider problem — use one of those.
- **Your suite is Selenium + Java.** Automation Sandbox is .NET. [Healenium](https://healenium.io/) is the
  natural fit and is designed exactly for that drop-in case.
- **You want a non-programmer to author and maintain tests.** The low-code platforms (Testim, Mabl) are built for
  that; this library assumes you write and own C# test code.
- **You require a vendor SLA and support contract.** This is an MIT open-source project maintained in the open.
  Evaluate its [API stability policy](versioning-and-stability.md) and release cadence against your risk tolerance.

### When Automation Sandbox is the right call

- You maintain **.NET** UI tests (desktop, web, or both) and locators breaking on refactors is your main pain.
- You want healing **inside the runner you already use**, with no new service to operate and no test data leaving
  your infrastructure.
- You need every heal to be **auditable** — a readable locator repository in source control and a per-decision
  report — rather than a green check from a model you cannot inspect.
- You are wary of "AI-powered healing" claims and want the AI to be **optional, bounded, and never the sole
  decision maker**.

---

## 🇹🇷 Türkçe Kılavuz

Automation Sandbox, Ranorex ya da Tosca olmaya çalışmıyor. Test otomasyonu probleminin **tek** bir parçasını
çözüyor — bir locator kırıldı, onu yapısal kanıttan yeniden çöz ve kararı açıkla — açık, denetlenebilir bir .NET
kütüphanesi olarak. Bu sayfa, bu kapsamın ve bu yaklaşımın sizin durumunuza uyup uymadığına karar vermeniz
içindir.

Aşağıda başka bir ürün hakkındaki her iddia, o ürünün kendi herkese açık dokümantasyonundan alınmıştır. Bir
ürünün daha iyi bir seçim olduğu yerde, bu sayfa bunu açıkça söyler.

### Bir bakışta

| | **Automation Sandbox** | **Healenium** | **Testim / Mabl / Functionize** | **Ranorex** |
| :--- | :--- | :--- | :--- | :--- |
| Teknoloji | .NET (net48 / netstandard2.0 / net8 / net10) | Java + Selenium (topluluk JS SDK) | SaaS, dilden bağımsız kaydedici | Windows IDE, .NET betikleme |
| Lisans / maliyet | MIT, ücretsiz, kendi sunucunuzda | Apache-2.0, ücretsiz; backend servisi + veritabanı gerekir | Ticari abonelik | Kullanıcı başı ticari lisans |
| İyileştirme yaklaşımı | **Önce** deterministik yapısal sezgisel; LLM yalnızca isteğe bağlı, çoğunluk-onaylı bir yedek | Backend'de saklanan geçmiş DOM ağaçları üzerinde ML | Sağlayıcı ML modeli (kara kutu) | Tescilli "RanoreXPath" ağırlıklandırması |
| Açıklanabilirlik | Karar başına JSON + HTML: her sinyalin ağırlığı, sağlayıcı oyları, sonuç | Öncesi/sonrası seçicilerin iyileştirme raporu | Sınırlı; iyileştirme çalışma geçmişinde gösterilir | Sınırlı |
| Makinenizden çıkan veri | Bir LLM yapılandırmadıkça hiçbir şey — o zaman da PII/gizli bilgi maskelemesi varsayılan açık, sınırlı bir kısa liste | Seçici verisi kendi barındırdığınız backend'e | Tam DOM / ekran görüntüleri sağlayıcı bulutuna | Yerel |
| Locator'ların yaşadığı yer | Deponuzda okunabilir JSON, sürümlü, diff alınabilir | Healenium backend veritabanında | Sağlayıcı bulutu | Ranorex nesne deposu (`.rxrep`) |
| Masaüstü desteği | Evet — FlaUI / UI Automation (Windows) | Hayır (yalnızca web) | Çoğunlukla web; bir miktar masaüstü (sağlayıcıya özel) | Evet — güçlü masaüstü |
| Onu çalıştıran | Kendi koşucunuz: xUnit, NUnit, Playwright, FlaUI, Reqnroll | Selenium testleri | Sağlayıcı koşucusu / grid | Ranorex Studio + Ranorex agent |
| Kaydedici / IDE / grid / zamanlayıcı | **Yok** — yalnızca kütüphane | Yok (mevcut Selenium testlerini iyileştirir) | Evet — tam platform | Evet — tam stüdyo |

### Her yaklaşımın optimize ettiği şey

**Automation Sandbox**, *tek bir iyileştirmeye duyulan güveni* optimize eder. Sezgisel yöntem deterministiktir;
aynı ağaç her zaman aynı kararı üretir ve her karar rapordan yeniden kurulabilir. LLM bilinçli olarak
kutulanmıştır çünkü [silinen bir eleman üzerinde çoklu sağlayıcı uzlaşması güvenilmezdir](benchmark-calibration.md#6-multi-provider-llm-consensus-as-an-absence-detector-97) —
ölçülen dört koşuda 34 oybirliği kararının 34'ü yanlış iyileştirmeydi. Bir denetçiye bir locator'ın *neden*
değiştiğini açıklamanız gerekiyorsa, bu model tam da o soru için kuruldu.

**Healenium**, *mevcut bir Selenium-Java paketinin doğrudan iyileştirilmesini* optimize eder. Teknolojiniz buysa
Healenium olgun, yaygın kullanılan ve testlerinizi yazma şeklinizde değişiklik gerektirmeyen bir çözümdür.
Ödünler operasyoneldir: bir backend servisi ile bir veritabanını çalıştırıp bakımını yaparsınız, iyileştirme
geçmişi deponuzda değil orada yaşar ve yalnızca web içindir.

**Testim / Mabl / Functionize**, *ilk teste ulaşma süresini ve az-kod yazımını* optimize eder. Bir kaydedici
testi kurar, sağlayıcının modeli onu iyileştirir ve programcı olmayan biri bakımını yapabilir. Bütün bir platform
satın alıyorsunuz — grid, zamanlama, analitik, destek — ve karşılığında kara kutu bir iyileştirici ile test
verinizin sağlayıcının bulutunda olmasını kabul ediyorsunuz.

**Ranorex**, *tam bir stüdyo ile masaüstü ve karma masaüstü/web otomasyonunu* optimize eder. Nesne deposu ve yol
ağırlıklandırması Windows uygulamaları için güçlüdür. Kendi kodunuza dahil ettiğiniz bir kütüphane değil,
lisanslı, IDE-merkezli bir üründür.

### Automation Sandbox'ı *ne zaman seçmemeli*

- **Bir kaydediciye, bir IDE'ye, bir çalıştırma grid'ine veya bir zamanlayıcıya ihtiyacınız var.** Bu bir
  kütüphane. Testlerinizi çalıştırmaz. Ranorex Studio, Tosca veya bir SaaS platformu çok daha geniş bir problemi
  çözer — onlardan birini kullanın.
- **Paketiniz Selenium + Java.** Automation Sandbox .NET'tir. [Healenium](https://healenium.io/) doğal seçimdir ve
  tam olarak bu doğrudan-entegrasyon durumu için tasarlanmıştır.
- **Testleri programcı olmayan birinin yazıp bakımını yapmasını istiyorsunuz.** Az-kod platformları (Testim, Mabl)
  bunun için kuruldu; bu kütüphane C# test kodunu sizin yazdığınızı ve sahiplendiğinizi varsayar.
- **Bir sağlayıcı SLA'sı ve destek sözleşmesi gerekiyor.** Bu, açıkça sürdürülen bir MIT açık kaynak projesidir.
  [API kararlılık politikasını](versioning-and-stability.md) ve sürüm sıklığını kendi risk toleransınıza göre
  değerlendirin.

### Automation Sandbox'ın doğru seçim olduğu durumlar

- **.NET** UI testleri (masaüstü, web veya ikisi) sürdürüyorsunuz ve locator'ların refactor'larda kırılması ana
  sıkıntınız.
- İyileştirmeyi **hâlihazırda kullandığınız koşucunun içinde**, işletilecek yeni bir servis olmadan ve test
  veriniz altyapınızdan çıkmadan istiyorsunuz.
- Her iyileştirmenin **denetlenebilir** olmasını istiyorsunuz — kaynak kontrolünde okunabilir bir locator deposu
  ve karar başına bir rapor — inceleyemediğiniz bir modelden gelen yeşil bir onay yerine.
- "Yapay zeka destekli iyileştirme" iddialarına temkinli yaklaşıyorsunuz ve yapay zekanın **isteğe bağlı, sınırlı
  ve asla tek karar verici olmamasını** istiyorsunuz.

---

## See also / Ayrıca bakınız

- [Benchmark & Calibration](benchmark-calibration.md) — measured accuracy on two real applications, and the multi-provider consensus study
- [Scope: what this is / what this isn't](https://github.com/mustafasercansak/automation-sandbox#-scope-what-this-is--what-this-isnt)
- [Documentation Hub](index.md) — including the guide for adding self-healing to an existing suite
