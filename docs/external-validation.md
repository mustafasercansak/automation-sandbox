---
layout: default
title: External Validation - Automation Sandbox
---

# External Validation / Dış Doğrulama

## English Guide

### 1. Commercial Goal and Current Evidence

The product goal is a future commercial offering centered on safety-first, explainable FlaUI locator healing for modern .NET Windows tests (#402). The first audience to validate is teams maintaining FlaUI desktop suites. Web is secondary; intent planning, code generation, and new provider features are frozen while existing features receive bug and security fixes. The MIT license remains unchanged. Pricing, packaging of paid capabilities, and service commitments are undecided.

As of September 18, 2026, there are no verified independent integrations, written pilot feedback, or organic drift datasets. Existing HandBrake/ShareX synthetic mutations support calibration only. This document is a collection protocol, not a report of completed pilots. [Issue #401](https://github.com/mustafasercansak/automation-sandbox/issues/401) blocks 1.0 until its evidence criteria are met.

### 2. Pilot Evidence to Collect

Recruit three independently maintained FlaUI test projects outside this repository when access becomes available. Record project ownership, repository or permitted evidence reference, framework, engine commit/package version, application versions, test commands, and the maintainer's written feedback. A fork or sample maintained by this repository does not establish an independent integration.

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

### 3. Next 90 Days and Release Decision

The sequence is partner identification and access, repeatable baseline capture, supervised integrations, then publication and commercial offer assessment. Timing depends on external participation; the 90-day planning window is not a release promise. Prefer improvements demonstrated necessary by pilot feedback over new feature families. Reuse #401 for the validation work; create implementation issues only for concrete findings aligned with the desktop focus.

Before 1.0, verify three independent integrations with written feedback and two organic drift datasets with measured outcomes. Link these artifacts from #401 and the release notes, then re-check every [beta-exit criterion](versioning-and-stability.md). Until then, keep #401 open and describe the product as preview, with commercial viability unvalidated.

## Türkçe Kılavuz

### 1. Ticari Hedef ve Mevcut Kanıtlar

Ürün hedefi, modern .NET Windows testleri için güvenliği önceleyen, açıklanabilir FlaUI locator iyileştirmesine odaklanan gelecekteki bir ticari üründür (#402). İlk doğrulanacak hedef kitle, FlaUI masaüstü testlerini sürdüren ekiplerdir. Web ikincildir; intent planlama, kod üretimi ve yeni sağlayıcı özellikleri dondurulmuştur. Mevcut özelliklerde hata ve güvenlik düzeltmeleri sürer. MIT lisansı değişmez. Fiyatlandırma, ücretli özelliklerin paketlenmesi ve hizmet taahhütleri henüz belirlenmemiştir.

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

### 3. Gelecek 90 Gün ve Sürüm Kararı

Sıralama, pilot ortaklarının belirlenmesi ve erişim, tekrarlanabilir başlangıç ölçümü, gözetimli entegrasyonlar, ardından yayımlama ve ticari teklif değerlendirmesidir. Zamanlama dış katılıma bağlıdır; 90 günlük planlama dönemi sürüm taahhüdü değildir. Pilot geri bildiriminin gerekli gösterdiği iyileştirmelere öncelik verin. Doğrulama için #401'i kullanın; yalnızca masaüstü odağıyla uyumlu somut bulgular için uygulama issue'ları açın.

1.0 öncesinde yazılı geri bildirimli üç bağımsız entegrasyonu ve ölçülmüş sonuçları olan iki doğal değişim veri setini doğrulayın. Kanıtları #401 ve sürüm notlarından bağlayın, ardından tüm [beta çıkış kriterlerini](versioning-and-stability.md) yeniden kontrol edin. O zamana kadar #401 açık kalır; ürün önizleme olarak tanımlanır ve ticari uygulanabilirliği doğrulanmış sayılmaz.
