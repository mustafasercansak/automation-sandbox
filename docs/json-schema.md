---
layout: default
title: JSON Schema Guide - Automation Sandbox
---

# 📄 JSON Schema Guide / JSON Şema Rehberi

This guide explains the `.locator.json` repository file structure used to store element snapshots and healing audit history.

> 💡 **Select Language / Dil Seçin:**
> - [🇬🇧 English Reference](#-english-reference)
> - [🇹🇷 Türkçe Referans](#-türkçe-referans)

---

## 🇬🇧 English Reference

### Sample `.locator.json` File
```json
{
  "SchemaVersion": 1,
  "ApplicationName": "WinFormsApp",
  "Platform": "windows-uia",
  "Locators": [
    {
      "LocatorKey": "MainForm.txtEmail",
      "Description": "Customer email input field",
      "TestIntent": "Enter user account email address for authentication",
      "Snapshot": {
        "ControlType": "Edit",
        "Name": "Email Address",
        "AutomationId": "txtEmailAddress",
        "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
      },
      "CreatedAt": "2026-08-02T10:00:00Z",
      "UpdatedAt": "2026-08-02T12:00:00Z",
      "HealingHistory": [
        {
          "HealedAt": "2026-08-02T12:00:00Z",
          "Source": "heuristic",
          "Score": 0.87,
          "ConfidenceThreshold": 0.5,
          "LlmConfidence": null,
          "LlmProviderName": null,
          "PreviousSnapshot": {
            "ControlType": "Edit",
            "Name": "Email Address",
            "AutomationId": "txtEmail",
            "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
          },
          "AcceptedSnapshot": {
            "ControlType": "Edit",
            "Name": "Email Address",
            "AutomationId": "txtEmailAddress",
            "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
          },
          "ScoreBreakdown": {
            "ControlTypeScore": 1.0,
            "ParentControlTypeScore": 1.0,
            "SiblingPositionScore": 1.0,
            "NameScore": 1.0,
            "PositionScore": 1.0
          },
          "DivergedFromHeuristic": null
        }
      ]
    }
  ]
}
```

### Key Field Descriptions
- **`LocatorKey`**: The unique identifier used in test code (e.g., `MainForm.txtEmail`).
- **`TestIntent`**: Description of *why* this step is performed (helps AI during refactors).
- **`Snapshot`**: The structural attributes of the element (ID, Name, Position, Parent).
- **`HealingHistory`**: The audit trail of accepted healing events for this locator. Each entry records:
  - **`HealedAt`**: UTC timestamp of the healing event.
  - **`Source`**: Who produced the accepted match — `"heuristic"`, or the LLM provider name for an LLM-consensus heal.
  - **`Score`**: The total similarity score of the accepted candidate.
  - **`ConfidenceThreshold`**: For a heuristic heal, the `MinimumConfidence` score gate in effect when the heal was accepted. For an LLM-consensus heal this is `0.0` — a deliberate placeholder, because acceptance is decided by the provider consensus quorum, not by a score threshold.
  - **`LlmConfidence`**: Mean self-reported confidence of the agreeing LLM providers (`null` for heuristic heals). Recorded for audit only; it is never compared or thresholded.
  - **`LlmProviderName`**: The provider that produced the LLM pick (`null` for heuristic heals).
  - **`PreviousSnapshot`**: The stored snapshot before the heal (`null` if not recorded).
  - **`AcceptedSnapshot`**: The newly accepted snapshot after the heal (`null` if not recorded).
  - **`ScoreBreakdown`**: The per-signal `ScoreComponents` of the accepted candidate (`ControlTypeScore`, `ParentControlTypeScore`, `SiblingPositionScore`, `NameScore`, `PositionScore`; each is `null` when that signal was missing on both sides).
  - **`DivergedFromHeuristic`**: `true` when an LLM heal picked a different element than the heuristic top candidate, `false` when it agreed. `null` for heuristic heals (the comparison only applies to LLM picks) and for entries saved before this field existed ("unknown / not recorded").

---

## 🇹🇷 Türkçe Referans

### Örnek `.locator.json` Dosyası
```json
{
  "SchemaVersion": 1,
  "ApplicationName": "WinFormsApp",
  "Platform": "windows-uia",
  "Locators": [
    {
      "LocatorKey": "AnaForm.txtEposta",
      "Description": "Müşteri e-posta girdisi",
      "TestIntent": "Oturum açmak için kullanıcı e-posta adresini girer",
      "Snapshot": {
        "ControlType": "Edit",
        "Name": "E-posta Adresi",
        "AutomationId": "txtEmailAddress",
        "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
      },
      "CreatedAt": "2026-08-02T10:00:00Z",
      "UpdatedAt": "2026-08-02T12:00:00Z",
      "HealingHistory": [
        {
          "HealedAt": "2026-08-02T12:00:00Z",
          "Source": "heuristic",
          "Score": 0.87,
          "ConfidenceThreshold": 0.5,
          "LlmConfidence": null,
          "LlmProviderName": null,
          "PreviousSnapshot": {
            "ControlType": "Edit",
            "Name": "E-posta Adresi",
            "AutomationId": "txtEmail",
            "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
          },
          "AcceptedSnapshot": {
            "ControlType": "Edit",
            "Name": "E-posta Adresi",
            "AutomationId": "txtEmailAddress",
            "BoundingRectangle": { "X": 120, "Y": 80, "Width": 200, "Height": 24 }
          },
          "ScoreBreakdown": {
            "ControlTypeScore": 1.0,
            "ParentControlTypeScore": 1.0,
            "SiblingPositionScore": 1.0,
            "NameScore": 1.0,
            "PositionScore": 1.0
          },
          "DivergedFromHeuristic": null
        }
      ]
    }
  ]
}
```

### Temel Alan Açıklamaları
- **`LocatorKey`**: Test kodunda kullanılan sabit benzersiz anahtar (örn. `AnaForm.txtEposta`).
- **`TestIntent`**: Bu test adımının *neden* yapıldığının açıklaması (Yapay zekaya rehberlik eder).
- **`Snapshot`**: Elemanın ekrandaki bilinen son yapısal özellikleri (ID, Ad, Konum, Ebeveyn).
- **`HealingHistory`**: Bu locator için kabul edilmiş iyileştirme olaylarının denetim kaydı. Her kayıt şunları içerir:
  - **`HealedAt`**: İyileştirme olayının UTC zaman damgası.
  - **`Source`**: Kabul edilen eşleşmeyi üreten kaynak — `"heuristic"` ya da LLM uzlaşmasıyla iyileştirmede LLM sağlayıcısının adı.
  - **`Score`**: Kabul edilen adayın toplam benzerlik skoru.
  - **`ConfidenceThreshold`**: Sezgisel iyileştirmede, iyileştirme kabul edildiğinde geçerli olan `MinimumConfidence` skor eşiği. LLM uzlaşmasıyla iyileştirmede bu değer `0.0`'dır — kabul kararını bir skor eşiği değil sağlayıcı uzlaşma çoğunluğu verdiği için kasıtlı bir yer tutucudur.
  - **`LlmConfidence`**: Uzlaşan LLM sağlayıcılarının ortalama öz-bildirim güven değeri (sezgisel iyileştirmelerde `null`). Yalnızca denetim amaçlı kaydedilir; hiçbir zaman karşılaştırılmaz veya eşiklenmez.
  - **`LlmProviderName`**: LLM seçimini üreten sağlayıcı (sezgisel iyileştirmelerde `null`).
  - **`PreviousSnapshot`**: İyileştirmeden önceki kayıtlı snapshot (kaydedilmediyse `null`).
  - **`AcceptedSnapshot`**: İyileştirmeden sonra kabul edilen yeni snapshot (kaydedilmediyse `null`).
  - **`ScoreBreakdown`**: Kabul edilen adayın sinyal bazlı `ScoreComponents` dökümü (`ControlTypeScore`, `ParentControlTypeScore`, `SiblingPositionScore`, `NameScore`, `PositionScore`; ilgili sinyal her iki tarafta da eksikse değer `null` olur).
  - **`DivergedFromHeuristic`**: LLM iyileştirmesi sezgisel en iyi adaydan farklı bir eleman seçtiyse `true`, aynı fikirdeyse `false`. Sezgisel iyileştirmelerde `null` (bu karşılaştırma yalnızca LLM seçimleri için anlamlıdır); ayrıca bu alan eklenmeden önce kaydedilmiş girdilerde de `null` ("bilinmiyor / kaydedilmedi").
