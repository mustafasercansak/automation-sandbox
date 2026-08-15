# 🎯 Intent-Aware Healing Guide / Test Amacı İle İyileştirme

This guide explains **Intent-Aware Healing** (`TestIntent`), BDD/Gherkin scenario style intents, best practices, and multilingual support.

> 🧭 **Built on this:** Intent-aware healing is the foundation for
> [Intent-Driven Automation](intent-driven-automation.md), where business goals
> become explored, recorded, and generated test flows.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

> 💡 **Zero Token Cost:** If your AI API tokens run out or you have no internet access, the engine's deterministic heuristic scorer (`SimilarityScorer`) still resolves broken locators at **\$0 cost and 0 API tokens** — sub-50ms for 3,000 controls on developer hardware (see `SyntheticTreeBenchmarkTests`).

---

## 🇬🇧 English Guide

### 1. BDD / Gherkin Scenario Style Intent (GIVEN-WHEN-THEN)
Yes! You can write `TestIntent` formatted as a **Gherkin / BDD test scenario step**. LLMs understand structured Gherkin syntax exceptionally well because it provides state preconditions and clear expected outcomes.

#### BDD Scenario Style Examples:
- `testIntent: "GIVEN user is on registration modal WHEN personal details are filled THEN click primary submit button"`
- `testIntent: "GIVEN cart contains items WHEN on checkout page THEN click confirm payment button"`
- `testIntent: "GIVEN user is logged in WHEN in profile settings THEN save changes"`

#### SpecFlow / Reqnroll BDD Integration Example:
```csharp
[When(@"user clicks the submit button with intent ""(.*)""")]
public async Task WhenUserClicksSubmitWithIntent(string intent)
{
    await engine.ExecuteWithHealingAsync(
        locatorKey: "Registration.SubmitButton",
        expected: expectedButton,
        action: async (element) => await page.ClickAsync(element.AutomationId),
        captureTreeRoot: () => page.CaptureTree(),
        testIntent: intent // Passes Gherkin step directly into testIntent!
    );
}
```

---

### 2. How to Write a Good Intent (The 3-Part Formula)
Writing an effective `TestIntent` is simple when using the 3-part formula:

$$\text{TestIntent} = \text{[Action Verb]} + \text{[Business Context]} + \text{[Goal / Outcome]}$$

#### ✅ Good Intent Examples:
- `"Enter corporate email address into the 2FA login form"`
- `"Click the primary checkout confirmation button to complete payment"`

---

### 3. Multilingual Intent Support
Automation Sandbox LLM providers are **natively multilingual**. You can write BDD or natural intent in any language (English, Turkish, German, French, etc.):

- **Turkish BDD:** `testIntent: "GIVEN kullanıcı kayıt sayfasında WHEN bilgiler girildi THEN kaydı tamamla butonuna tıkla"`
- **German BDD:** `testIntent: "GIVEN Benutzer ist im Registrierungsformular WHEN Daten eingegeben THEN Registrierung abschließen"`

### 4. What TestIntent Does Today

`TestIntent` currently supports healing, not full autonomous test generation:

- It is stored with snapshots and locator repository records.
- It is sent to LLM providers as semantic context.
- It is preserved when a locator is healed.
- It appears in reports and audit history.

Full intent-driven automation is planned as M6, starting with a structured
intent scenario model and deterministic planner.

---

## 🇹🇷 Türkçe Kılavuz

### 1. BDD / Gherkin Senaryo Formatında Intent Yazımı (GIVEN-WHEN-THEN)
Evet! `TestIntent` parametresini bir **Gherkin / BDD test senaryosu adımı** biçiminde yazabilirsiniz. Yapay zeka modelleri GIVEN-WHEN-THEN yapısını çok iyi kavrar çünkü bu yapı ön koşulu ve hedeflenen sonucu net şekilde sunar.

#### BDD Formatında Intent Örnekleri:
- `testIntent: "GIVEN kullanıcı kayıt sayfasında WHEN bilgiler girildi THEN kaydı tamamla butonuna tıkla"`
- `testIntent: "GIVEN sepet dolu WHEN ödeme sayfasında THEN ödemeyi onayla butonuna tıkla"`
- `testIntent: "GIVEN oturum açık WHEN profil ayarlarında THEN değişiklikleri kaydet"`

#### SpecFlow / Reqnroll BDD Entegrasyon Örneği:
```csharp
[When(@"kullanıcı ""(.*)"" amacıyla butonuna tıklar")]
public async Task KullaniciTıklarIntentIle(string intent)
{
    await engine.ExecuteWithHealingAsync(
        locatorKey: "Registration.SubmitButton",
        expected: expectedButton,
        action: async (element) => await page.ClickAsync(element.AutomationId),
        captureTreeRoot: () => page.CaptureTree(),
        testIntent: intent // Gherkin adım metnini doğrudan testIntent olarak aktarır!
    );
}
```

---

### 2. Intent Nasıl Yazılır? (3 Adımlı Formül)
$$\text{TestIntent} = \text{[Eylem Fiili]} + \text{[İş Bağlamı]} + \text{[Hedef / Sonuç]}$$

---

### 3. Çok Dilli BDD Intent Desteği
İster Türkçe BDD, ister İngilizce Gherkin yazın; yapay zeka adımı otomatik çözer ve ekrandaki elemanla eşleştirir.

### 4. TestIntent Bugün Ne Yapar?

`TestIntent` bugün tam otomatik test üretmez; self-healing kararına anlam katar:

- Snapshot ve locator repository içinde saklanır.
- LLM prompt'una semantik bağlam olarak eklenir.
- Locator iyileştirildiğinde korunur.
- Raporlarda ve audit history içinde görünür.

Tam intent tabanlı otomasyon M6 kapsamındadır.
