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
      "UpdatedAt": "2026-08-02T12:00:00Z"
    }
  ]
}
```

### Key Field Descriptions
- **`LocatorKey`**: The unique identifier used in test code (e.g., `MainForm.txtEmail`).
- **`TestIntent`**: Description of *why* this step is performed (helps AI during refactors).
- **`Snapshot`**: The structural attributes of the element (ID, Name, Position, Parent).

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
      "UpdatedAt": "2026-08-02T12:00:00Z"
    }
  ]
}
```

### Temel Alan Açıklamaları
- **`LocatorKey`**: Test kodunda kullanılan sabit benzersiz anahtar (örn. `AnaForm.txtEposta`).
- **`TestIntent`**: Bu test adımının *neden* yapıldığının açıklaması (Yapay zekaya rehberlik eder).
- **`Snapshot`**: Elemanın ekrandaki bilinen son yapısal özellikleri (ID, Ad, Konum, Ebeveyn).
