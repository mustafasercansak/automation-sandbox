# 🎯 Intent-Aware Healing Guide / Test Amacı İle İyileştirme

This guide explains **Intent-Aware Healing** (`TestIntent`), helping AI models resolve elements after major UI redesigns.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Guide](#-english-guide)
> - [🇹🇷 Türkçe Kılavuz](#-türkçe-kılavuz)

---

## 🇬🇧 English Guide

### What is Test Intent?
Traditional self-healing tools only ask: *"Where is the element named `btnRegister`?"*
If developers rename `btnRegister` to `"Create Account"`, traditional tools will fail.

By providing **`TestIntent`**, you tell the AI **why** you are clicking the element:
`TestIntent = "Click the button to finalize registration"`

Even if the name, ID, or screen position changes completely, AI models understand your goal and accurately select the new `"Create Account"` button!

### How to Code Intent-Aware Steps

```csharp
await engine.ExecuteWithHealingAsync(
    locatorKey: "Registration.Submit",
    expected: new UiElementInfo { ControlType = "Button", AutomationId = "btnRegister_Old" },
    action: async (healedElement) => await page.ClickAsync(healedElement.AutomationId),
    captureTreeRoot: () => page.CaptureTree(),
    testIntent: "Click the main registration form submission button" // <--- Test Intent Here!
);
```

---

## 🇹🇷 Türkçe Kılavuz

### Test Intent (Test Amacı) Nedir?
Geleneksel iyileştirme araçları sadece şunu sorar: *"Adı `btnRegister` olan buton nerede?"*
Eğer yazılımcılar butonun adını `"Hesap Oluştur"` yaparsa eski araçlar **başarısız olur**.

**`TestIntent`** vererek yapay zekaya bu butona **neden** tıkladığınızı söylersiniz:
`TestIntent = "Kullanıcı kayıt formunu onaylama butonuna tıkla"`

Butonun adı, ID'si veya ekrandaki yeri tamamen değişse bile yapay zeka adımınızın amacını kavrar ve yeni `"Hesap Oluştur"` butonunu **yanılmadan seçer**!

### Intent Kullanarak Kod Yazımı

```csharp
await engine.ExecuteWithHealingAsync(
    locatorKey: "KayitFormu.GonderButonu",
    expected: new UiElementInfo { ControlType = "Button", AutomationId = "btnRegister_Eski" },
    action: async (iyilestirilenEleman) => await page.ClickAsync(iyilestirilenEleman.AutomationId),
    captureTreeRoot: () => CanliEkranYakala(),
    testIntent: "Kullanıcı kayıt formunu onaylama butonuna tıkla" // <--- Test Amacı Burası!
);
```
