---
layout: default
title: External Validation - Automation Sandbox
---

# External Validation / Dış Doğrulama

## English Guide

### 1. Commercial Goal and Current Evidence

The product goal is a future commercial offering centered on safety-first, explainable locator healing across **both** desktop (FlaUI) and web (Playwright) tests, as equal-priority platforms (#469, superseding #402). The audience to validate spans teams maintaining FlaUI desktop suites and teams maintaining Playwright/web suites. Intent planning, code generation, and new provider features are active development areas, not frozen. The MIT license remains unchanged. Pricing, packaging of paid capabilities, and service commitments are undecided.

As of September 18, 2026, there are no verified independent integrations, written pilot feedback, or organic drift datasets. Existing HandBrake/ShareX synthetic mutations support calibration only. This document is a collection protocol, not a report of completed pilots. [Issue #401](https://github.com/mustafasercansak/automation-sandbox/issues/401) blocks 1.0 until its evidence criteria are met.

### 2. Pilot Evidence to Collect

Recruit three independently maintained test projects (FlaUI desktop or Playwright web) outside this repository when access becomes available. Record project ownership, repository or permitted evidence reference, framework, engine commit/package version, application versions, test commands, and the maintainer's written feedback. A fork or sample maintained by this repository does not establish an independent integration.

For at least two real applications, capture UI trees and stored locators from two genuine released versions. Record release URLs, artifact hashes, capture environment, repeatable navigation steps, and locator differences. Preserve the original snapshots; do not manufacture drift by renaming or deleting nodes. Obtain permission to publish data and remove secrets or personal data before committing it.

Establish expected target identity or removal through manual review before evaluating the resolver. Freeze the dataset and record every selected locator, including unchanged, missing, and ambiguous targets. Record engine settings and compare the same test workload with and without healing. Start pilots in Observe or Review mode; record subsequent operator decisions separately from proposals.

For each integration publish:

| Measure | Required evidence |
| :--- | :--- |
| Actually broken locators | Count and reproducible lookup failures on the newer version |
| Correct heals | Count and independent confirmation against the expected target |
| Review referrals | Count, proposed candidates, and operator disposition |
| False heals | Count of accepted wrong targets, including deleted targets |
| Unresolved or unverified results | Counts and reasons; never classify unverified acceptance as correct |
| Maintenance minutes saved | Baseline repair time minus assisted repair and review time, with measurement method; report unavailable when unmeasured |
| Written feedback | Maintainer observations, operational friction, value assessment, and interest in a paid pilot without treating interest as a sale |

Keep raw reports, ground truth, commands, and feedback references beside each dataset under `docs/`. Report all outcomes with denominators and limitations. Do not retune against the evaluation cases and then present the result as unseen validation.

### Pilot Quickstart for Volunteers

This is the short path for someone who maintains a FlaUI desktop or Playwright web suite, is willing to try the engine, and does not want to read the whole protocol first. The engine is pre-1.0 preview software. Nothing here has been validated by an independent pilot yet, and trying it commits you to nothing.

**What it will and will not do to your tests.** Both paths below are read-only. They report what the engine would pick for a broken locator, with the score breakdown. They do not edit your tests, do not make a failing test pass, and do not change your repository. Healing that retries and persists (`AutoHeal`) is deliberately not part of a first pilot.

**Path A: any language, no .NET project needed.** Use the `automation-sandbox-heal` CLI on two JSON snapshots of the same element, one from the old app version and one tree from the new version.

1. Install the CLI (needs the .NET 8 runtime on the machine): `dotnet tool install -g AutomationSandbox.Cli --prerelease`
2. Capture snapshots with a reference script, or produce the same JSON shape from your own tooling (see the [JSON Schema](schemas/ui-element-info.schema.json)):
   - Web: [`examples/js-playwright-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/js-playwright-capture), run `npm install` and `npx playwright install chromium` once in that folder, then `node capture.mjs <url> expected.json --selector "<css>"` on the old version and `node capture.mjs <url> tree.json` on the new one.
   - Windows desktop: [`examples/python-pywinauto-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/python-pywinauto-capture), run `pip install -r requirements.txt` once in that folder, then `python capture.py <app.exe> expected.json --name "<Name>"` on the old version and `python capture.py <app.exe> tree.json` on the new one. Add `--attach` to connect to an already running process by name instead of launching it.
3. Run `automation-sandbox-heal --expected expected.json --tree tree.json --profile balanced`. Exit code `0` means the engine is confident, `1` means it is not, `2` means bad input. See the [CLI guide](cli-and-ci-integration.md).
4. Compare the proposed element with the one you would pick by hand.

**Path B: an existing .NET suite.** Follow the [first-week rollout](integration-existing-suite.md) and stop at its `Observe` step: wire one test that breaks often, set `SELF_HEALING_REPORT_PATH`, run the suite, and read the healing report (JSON plus an HTML dashboard, see [Healing Reports](healing-reports.md)). Your tests still fail exactly as before.

**What to send back.** For each locator that actually broke between two real releases, whatever you can share of this: how many locators broke, which target the engine proposed, whether that was the right one by your own review, how many it declined or could not decide, any proposal that was wrong (a wrong proposal is the most valuable finding), roughly how long the manual repair took if you measured it, and your written impressions, including friction and whether it would be worth paying for. Post it as a comment on [#401](https://github.com/mustafasercansak/automation-sandbox/issues/401), or open an issue. Remove secrets and personal data first, and say whether the data may be published. Partial answers are fine.

### 3. Next 90 Days and Release Decision

The sequence is partner identification and access, repeatable baseline capture, supervised integrations, then publication and commercial offer assessment. Timing depends on external participation; the 90-day planning window is not a release promise. Prefer improvements demonstrated necessary by pilot feedback over new feature families. Reuse #401 for the validation work; create implementation issues only for concrete findings aligned with either platform's pilot feedback.

Before 1.0, verify three independent integrations with written feedback and two organic drift datasets with measured outcomes. Link these artifacts from #401 and the release notes, then re-check every [beta-exit criterion](versioning-and-stability.md). Until then, keep #401 open and describe the product as preview, with commercial viability unvalidated.

## Türkçe Kılavuz

### 1. Ticari Hedef ve Mevcut Kanıtlar

Ürün hedefi, hem masaüstü (FlaUI) hem web (Playwright) testleri için güvenliği önceleyen, açıklanabilir locator iyileştirmesini kapsayan, ikisi de eşit öncelikli gelecekteki bir ticari üründür (#402'nin yerine geçen #469). Doğrulanacak hedef kitle hem FlaUI masaüstü testlerini hem de Playwright/web testlerini sürdüren ekipleri kapsar. İntent planlama, kod üretimi ve yeni sağlayıcı özellikleri aktif geliştirme alanlarıdır, dondurulmuş değildir. MIT lisansı değişmez. Fiyatlandırma, ücretli özelliklerin paketlenmesi ve hizmet taahhütleri henüz belirlenmemiştir.

18 Eylül 2026 itibarıyla doğrulanmış bağımsız entegrasyon, yazılı pilot geri bildirimi veya doğal değişim veri seti yoktur. Mevcut HandBrake/ShareX sentetik değişimleri yalnızca kalibrasyon kanıtıdır. Bu belge tamamlanmış pilot raporu değil, veri toplama protokolüdür. [Issue #401](https://github.com/mustafasercansak/automation-sandbox/issues/401), kanıt kriterleri sağlanana kadar 1.0 sürümünü engeller.

### 2. Toplanacak Pilot Kanıtları

Erişim sağlandığında bu depo dışında bağımsız sürdürülen üç FlaUI test projesiyle pilot gerçekleştirin. Proje sahipliğini, depo veya izin verilen kanıt bağlantısını, framework'ü, motor commit/paket sürümünü, uygulama sürümlerini, test komutlarını ve bakımcının yazılı geri bildirimini kaydedin. Bu deponun bakımcılarının sürdürdüğü bir fork veya örnek, bağımsız entegrasyon sayılmaz.

En az iki gerçek uygulamanın gerçekten yayımlanmış iki sürümünden UI ağaçlarını ve kayıtlı locator'ları yakalayın. Sürüm bağlantılarını, artifact hash'lerini, yakalama ortamını, tekrarlanabilir gezinme adımlarını ve locator farklarını kaydedin. Özgün snapshot'ları koruyun; düğümleri yeniden adlandırarak veya silerek değişim üretmeyin. Yayımlama izni alın ve verileri commit etmeden önce sırları veya kişisel verileri temizleyin.

Çözümleyiciyi değerlendirmeden önce beklenen hedefin kimliğini veya kaldırılmış olduğunu elle doğrulayın. Veri setini sabitleyin; değişmemiş, bulunamayan ve belirsiz hedefler dahil seçilen her locator'ı kaydedin. Motor ayarlarını kaydedin ve aynı test iş yükünü iyileştirme açık ve kapalı olarak karşılaştırın. Pilotlara Observe veya Review modunda başlayın; sonraki operatör kararlarını önerilerden ayrı kaydedin.

Her entegrasyon için yayımlayın:

| Ölçüm | Gerekli kanıt |
| :--- | :--- |
| Gerçekten bozulan locator'lar | Yeni sürümde sayı ve tekrarlanabilir arama hataları |
| Doğru iyileştirmeler | Sayı ve beklenen hedefe göre bağımsız doğrulama |
| İncelemeye yönlendirmeler | Sayı, önerilen adaylar ve operatör kararı |
| Yanlış iyileştirmeler | Silinen hedefler dahil kabul edilmiş yanlış hedef sayısı |
| Çözülemeyen veya doğrulanmayan sonuçlar | Sayılar ve nedenler; doğrulanmamış kabulü doğru saymayın |
| Kazanılan bakım dakikası | Ölçüm yöntemiyle birlikte normal onarım süresinden destekli onarım ve inceleme süresinin çıkarılması; ölçülmediyse belirtilmeli |
| Yazılı geri bildirim | Bakımcı gözlemleri, kullanım güçlükleri, değer değerlendirmesi ve ücretli pilota ilgi; ilgi satış sayılmaz |

Ham raporları, doğru hedef kayıtlarını, komutları ve geri bildirim bağlantılarını her veri setinin yanında `docs/` altında tutun. Tüm sonuçları paydaları ve sınırlamalarıyla raporlayın. Değerlendirme vakalarına göre ayar yapıp sonucu görülmemiş veri üzerindeki doğrulama olarak sunmayın.

### Gönüllüler İçin Pilot Hızlı Başlangıç

Bu, bir FlaUI masaüstü veya Playwright web test paketi sürdüren, motoru denemeye istekli olan ve önce protokolün tamamını okumak istemeyen kişi için kısa yoldur. Motor 1.0 öncesi önizleme yazılımıdır. Burada anlatılan hiçbir şey henüz bağımsız bir pilot tarafından doğrulanmadı ve denemek sizi hiçbir şeye bağlamaz.

**Testlerinize ne yapar, ne yapmaz.** Aşağıdaki iki yol da salt okunurdur. Kırık bir locator için motorun neyi seçeceğini, skor dökümüyle birlikte raporlar. Testlerinizi düzenlemez, başarısız bir testi geçirmez ve deponuzu değiştirmez. Yeniden deneyip kalıcı kaydeden iyileştirme (`AutoHeal`) ilk bir pilotun parçası değildir.

**A Yolu: herhangi bir dil, .NET projesi gerekmez.** Aynı elemanın iki JSON anlık görüntüsü üzerinde `automation-sandbox-heal` CLI'sini kullanın: biri eski uygulama sürümünden, biri yeni sürümden alınan ağaç.

1. CLI'yi kurun (makinede .NET 8 runtime gerekir): `dotnet tool install -g AutomationSandbox.Cli --prerelease`
2. Bir referans betikle anlık görüntüleri yakalayın veya kendi araçlarınızla aynı JSON şeklini üretin ([JSON Schema](schemas/ui-element-info.schema.json)):
   - Web: [`examples/js-playwright-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/js-playwright-capture), o klasörde bir kez `npm install` ve `npx playwright install chromium` çalıştırın, sonra eski sürümde `node capture.mjs <url> expected.json --selector "<css>"`, yeni sürümde `node capture.mjs <url> tree.json`.
   - Windows masaüstü: [`examples/python-pywinauto-capture`](https://github.com/mustafasercansak/automation-sandbox/tree/main/examples/python-pywinauto-capture), o klasörde bir kez `pip install -r requirements.txt` çalıştırın, sonra eski sürümde `python capture.py <app.exe> expected.json --name "<Name>"`, yeni sürümde `python capture.py <app.exe> tree.json`. Uygulamayı başlatmak yerine zaten çalışan bir işleme ada göre bağlanmak için `--attach` ekleyin.
3. `automation-sandbox-heal --expected expected.json --tree tree.json --profile balanced` komutunu çalıştırın. Çıkış kodu `0` motorun emin olduğunu, `1` emin olmadığını, `2` hatalı girdiyi gösterir. Bkz. [CLI rehberi](cli-and-ci-integration.md).
4. Önerilen elemanı elle seçeceğiniz elemanla karşılaştırın.

**B Yolu: mevcut bir .NET test paketi.** [İlk hafta geçiş planını](integration-existing-suite.md) izleyin ve `Observe` adımında durun: sık kırılan bir testi bağlayın, `SELF_HEALING_REPORT_PATH` değişkenini ayarlayın, paketi çalıştırın ve healing raporunu okuyun (JSON ve HTML panel, bkz. [Healing Raporları](healing-reports.md)). Testleriniz öncekiyle aynı şekilde başarısız olmaya devam eder.

**Geri ne göndermelisiniz.** İki gerçek sürüm arasında gerçekten kırılan her locator için paylaşabildiğiniz kadarını: kaç locator kırıldı, motor hangi hedefi önerdi, kendi incelemenize göre doğru muydu, kaçını reddetti veya karar veremedi, yanlış olan bir öneri var mı (yanlış öneri en değerli bulgudur), ölçtüyseniz elle onarımın yaklaşık ne kadar sürdüğü, ve sürtünme ile ücretli kullanıma değip değmeyeceği dahil yazılı izlenimleriniz. [#401](https://github.com/mustafasercansak/automation-sandbox/issues/401) üzerine yorum olarak gönderin veya bir issue açın. Önce sırları ve kişisel verileri temizleyin ve verinin yayımlanıp yayımlanamayacağını belirtin. Kısmi yanıtlar da yeterlidir.

### 3. Gelecek 90 Gün ve Sürüm Kararı

Sıralama, pilot ortaklarının belirlenmesi ve erişim, tekrarlanabilir başlangıç ölçümü, gözetimli entegrasyonlar, ardından yayımlama ve ticari teklif değerlendirmesidir. Zamanlama dış katılıma bağlıdır; 90 günlük planlama dönemi sürüm taahhüdü değildir. Pilot geri bildiriminin gerekli gösterdiği iyileştirmelere öncelik verin. Doğrulama için #401'i kullanın; yalnızca masaüstü odağıyla uyumlu somut bulgular için uygulama issue'ları açın.

1.0 öncesinde yazılı geri bildirimli üç bağımsız entegrasyonu ve ölçülmüş sonuçları olan iki doğal değişim veri setini doğrulayın. Kanıtları #401 ve sürüm notlarından bağlayın, ardından tüm [beta çıkış kriterlerini](versioning-and-stability.md) yeniden kontrol edin. O zamana kadar #401 açık kalır; ürün önizleme olarak tanımlanır ve ticari uygulanabilirliği doğrulanmış sayılmaz.
