# 📋 KasaManager Projesi — Devam Dokümanı
> **Son Güncelleme:** 26 Şubat 2026  
> **Hazırlayan:** Helpy 🤖  
> **Bu dokümanı yeni oturumda Helpy'ye gösterin — tüm bağlamı hatırlaması için yeterlidir.**

---

## 🏗️ Proje Genel Bilgi

**KasaManager**, bir icra dairesinin nakit akışı (kasa) yönetimini dijitalleştirmek için geliştirilen **.NET 8 + ASP.NET Core MVC** uygulamasıdır.

- **Proje Yolu:** Geliştirildiği PC'de `f:\KasaYonetim`, iş bilgisayarında `D:\Publish` (publish çıktısı)
- **Solution:** `KasaManager.sln`
- **Teknoloji:** .NET 8, ASP.NET Core MVC, EF Core (SQL Server + SQLite), QuestPDF, ExcelDataReader
- **Son Build:** ✅ 0 hata, 0 uyarı

---

## 🧱 4 Katmanlı Clean Architecture

```
src/
├── KasaManager.Domain/          — Entity'ler, FormulaEngine, Guards, Constants
├── KasaManager.Application/     — Servisler, Orchestration, Pipeline, Abstractions
├── KasaManager.Infrastructure/  — EF Core DbContext, Migrations, Excel, PDF, Export
└── KasaManager.Web/             — 20 Controller, 17 View klasörü, Program.cs
tests/
└── KasaManager.Tests/           — Unit testler
```

---

## 🌐 Controller'lar (20 adet)

| Controller | Açıklama |
|-----------|----------|
| `KasaPreviewController.cs` (+Export, Helpers, Snapshot partial'ları) | Ana kasa hesaplama UI |
| `ImportController.cs` | 7 tip Excel import (BankaHarcama, BankaTahsilat, KasaUstRapor, MasrafVeReddiyat, OnlineHarcama, OnlineMasraf, OnlineReddiyat) |
| `KasaUstRaporController.cs` | Kasa üst rapor yönetimi |
| `GenelKasaRaporController.cs` | Dönemsel genel kasa raporları |
| `ComparisonController.cs` | Banka-Online karşılaştırma |
| `HesapKontrolController.cs` | Banka hesap kontrol modülü |
| `FieldChooserController.cs` | Dinamik alan seçici |
| `FormulaDesignerController.cs` | Formül tasarımcısı (template-driven) |
| `KasaSettingsController.cs` | Global kasa ayarları |
| `KasaRaporlarController.cs` | Rapor listeleme/yönetimi |
| `KasaRaporController.cs` | Rapor detay |
| `BackupController.cs` | DB yedekleme (SQL Server .bak / SQLite .db) |
| `LegacyKasaController.cs` | Eski DB'den okuma (read-only) |
| `AccountController.cs` | Login/Logout (Cookie Auth) |
| `UserManagementController.cs` | Kullanıcı yönetimi |
| `DiagnosticsController.cs` | Sistem diagnostik |
| `HomeController.cs` | Ana sayfa |

---

## 💾 Veritabanı (EF Core)

### Provider
- **SQL Server** (production, default)
- **SQLite** (opsiyonel, `Database:Provider=Sqlite` ile)

### DbContext Tabloları
| Tablo | Entity | Açıklama |
|-------|--------|----------|
| `KasaRaporSnapshots` | `KasaRaporSnapshot` | Tarihli kasa rapor snapshot'ları |
| `KasaRaporSnapshotRows` | `KasaRaporSnapshotRow` | Snapshot satırları (veznedar bazlı) |
| `KasaRaporSnapshotInputs` | `KasaRaporSnapshotInputs` | Kullanıcı girdileri (JSON) |
| `KasaRaporSnapshotResults` | `KasaRaporSnapshotResults` | Hesaplama sonuçları (JSON) |
| `CalculatedKasaSnapshots` | `CalculatedKasaSnapshot` | FormulaEngine hesaplama sonuçları |
| `KasaGlobalDefaultsSettings` | `KasaGlobalDefaultsSettings` | Global varsayılanlar (tek satır, Id=1) |
| `FormulaSets` | `PersistedFormulaSet` | Formül şablon setleri |
| `FormulaLines` | `PersistedFormulaLine` | Formül satırları |
| `FormulaRuns` | `PersistedFormulaRun` | Formül çalıştırma geçmişi |
| `UserFieldPreferences` | `UserFieldPreference` | Kullanıcı alan tercihleri |
| `HesapKontrolKayitlari` | `HesapKontrolKaydi` | Banka hesap kontrol kayıtları |
| `DismissedValidations` | `DismissedValidation` | Kullanıcının kapadığı uyarılar |
| `KasaUsers` | `KasaUser` | Kullanıcılar (BCrypt hash) |
| `ComparisonDecisions` | `ComparisonDecision` | Banka-Online eşleştirme kararları |

### Migration Dosyaları (7 adet)
```
Migrations/
├── 20260106190000_InitialCreate.cs
├── 20260110120000_AddFormulaSets.cs
├── 20260130120000_R17_DynamicFieldChooser.cs
├── 20260217120000_AddDismissedValidations.cs
├── 20260219180934_AddKasaUsers.cs (+Designer)
├── 20260220093934_AddComparisonDecisions.cs (+Designer)
└── KasaManagerDbContextModelSnapshot.cs
```

### Migration Stratejisi
- `DbBootstrapExtensions.cs` → SQL Server için `db.Database.Migrate()` (idempotent — `__EFMigrationsHistory` kontrol eder)
- SQLite için `db.Database.EnsureCreated()`
- Migration öncesi pending/applied sayısı loglanıyor
- Mevcut veriler ASLA silinmez

---

## ⚙️ Önemli Servisler

| Servis | Konum | Açıklama |
|--------|-------|----------|
| `KasaDraftService` | Application/Services/ | Ana iş mantığı, draft üretimi, UnifiedPool |
| `FormulaEngineService` | Application/Services/ | Template-driven formül motoru |
| `KasaOrchestrator` | Application/Orchestration/ | İstek koordinasyonu |
| `ImportOrchestrator` | Application/Orchestration/ | Excel import koordinasyonu |
| `ComparisonService` | Application/Services/Comparison/ | Banka-Online karşılaştırma |
| `BankaHesapKontrolService` | Infrastructure/Services/ | Hesap kontrol analizi |
| `VergideBirikenLedgerService` | Infrastructure/Services/ | Vergide biriken akıllı ledger |
| `FieldPreferenceService` | Infrastructure/Services/ | Alan tercihleri |
| `CalculatedKasaSnapshotService` | Infrastructure/Services/ | Hesaplama snapshot CRUD |
| `ExportService` | Infrastructure/Export/ | PDF/Excel export |

---

## 🔑 Temel Kavramlar

### Kasa Türleri
- **Sabah Kasa** (`KasaRaporTuru.Sabah`) — Sabah açılış kasa raporu
- **Akşam Kasa** (`KasaRaporTuru.Aksam`) — Günlük kapanış kasa raporu
- **Genel Kasa** (`KasaRaporTuru.Genel`) — Dönemsel toplam
- **Ortak** (`KasaRaporTuru.Ortak`) — Her iki kasa için ortak alanlar

### FormulaEngine
- **FieldCatalog** (`Domain/FormulaEngine/FieldCatalog.cs`) — 83+ alan tanımı, kategorize
- **FormulaSet** — DB'de kayıtlı formül şablonları (Aksam, Sabah, Genel, Custom)
- **FormulaTemplate** — Formül satırları (TargetKey, Expression, Mode)
- **KasaCanonicalKeys** — 70+ standart alan adı (normal_tahsilat, online_harc, genel_kasa, vb.)

### UnifiedKasaRecord
```csharp
public sealed class UnifiedKasaRecord
{
    public Guid Id { get; set; }
    public DateOnly RaporTarihi { get; set; }
    public KasaRaporTuru RaporTuru { get; set; }
    public Dictionary<string, decimal> DecimalFields { get; set; }
    public Dictionary<string, string?> TextFields { get; set; }
    public Dictionary<string, DateTime> DateFields { get; set; }
    public Dictionary<string, int> IntFields { get; set; }
}
```

---

## 🌐 Ağ & Deploy Yapılandırması

### appsettings.json (Development)
```json
{
  "Database": { "Provider": "SqlServer", "AutoInstanceResolve": true },
  "ConnectionStrings": {
    "SqlConnection": "Server=localhost;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "SeedAdmin": { "Username": "admin", "Password": "Admin123!", "DisplayName": "Sistem Yöneticisi" }
}
```

### appsettings.Production.json (Kurumsal Ağ)
```json
{
  "Database": { "Provider": "SqlServer", "AutoInstanceResolve": true },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5000" } } },
  "LegacyDatabase": { "Enabled": false },
  "Logging": { "LogLevel": { "Default": "Warning", "DbBootstrap": "Information" } }
}
```

### Publish & Çalıştırma
```powershell
dotnet publish -c Release -o D:\Publish
# D:\Publish klasöründe:
set ASPNETCORE_ENVIRONMENT=Production
KasaManager.Web.exe
# Diğer bilgisayarlardan → http://<sunucu-ip>:5000
```

### Önemli Program.cs Detayları
- Kestrel max body: 200 MB (büyük Excel upload)
- Cookie Auth: 8 saat, sliding expiration
- HTTPS redirect: sadece Development'ta (Production'da kapalı — sertifika sorunu)
- Response Compression aktif
- `SqlServerConnectionStringResolver` — local SQL instance otomatik bulucu (Windows Registry)
- `DbBootstrapExtensions.UseDbBootstrapAsync()` — migration + seed (admin user, global defaults, formula templates)

---

## 🔄 Önceki Oturumlarda Yapılanlar (Kronolojik)

### Oturum 1 (30 Ocak 2026)
- ✅ Proje analizi ve mimari dokümantasyonu
- ✅ Alan Seçici (Field Chooser) iyileştirmesi — FieldCatalog'dan 83+ alan gösterim
- ✅ Sol Menü Senkronizasyonu — yeni alanlar dinamik ekleme
- ✅ Şablon Adı Gösterimi — FormulaSet dropdown'dan dinamik etiket

### Oturum 2 (Bu Oturum — 26 Şubat 2026)
- ✅ Migration loglama — pending/applied sayıları loglanıyor
- ✅ `appsettings.Production.json` — Kestrel 0.0.0.0:5000 ağ dinleme
- ✅ `Program.cs` — HTTPS redirect sadece Development'ta
- ✅ Yedekleme butonu fix — cookie-based download detection + 60s failsafe timer
- ✅ `BackupController.cs` — download-complete cookie eklendi

---

## ⏳ Bekleyen / Gelecek Konular

### Yüksek Öncelikli
1. **Eksik/Fazla Veri Akışı** — `gune_ait_eksik_fazla_tahsilat` hesaplama sonrası önceki güne devri, VT'den önceki gün değeri okuma
2. **Sabah Kasa Şablonu** — Formül tanımları, alan görünürlüğü
3. **22 yeni alan** FieldCatalog'a eklenmesi (SabahKasa-specific alanlar — detayları oturum 1 implementation_plan'da)

### Refactoring (İleride)
4. **KasaPreviewController** — Zaten partial'lara bölündü (Export, Helpers, Snapshot) ama hala büyük
5. **KasaDraftService** — Hala büyük, alt servislere bölünebilir
6. **Legacy model temizliği** — SabahKasaNesnesi, AksamKasaNesnesi vb. Unified modele geçiş

### UI İyileştirmeleri
7. Vurgulu alanlar gösterimi
8. Kullanıcı uyarıları (eksik veri bildirimi)

---

## 🔧 Dikkat Edilecek Teknik Noktalar

1. **Turkish Culture** — Decimal ayracı virgül (,) vs nokta (.), DateTime format farkları
2. **QuestPDF** — Community License, `HeaderDescriptor` API değişiklikleri
3. **ExcelDataReader** — `System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` zorunlu
4. **SQL Server Instance** — `SqlServerConnectionStringResolver` otomatik buluyor ama `Database:PreferredInstance` ile override edilebilir
5. **dotnet kilitleme** — Build sırasında `bin/obj` dosyaları kilitlenebilir, `dotnet build-server shutdown` gerekebilir
6. **Migration scripts** — `scripts/` klasöründe 01/02/03 numaralı PS1 scriptleri var

---

## 📁 Önemli Dosya Haritası

```
src/KasaManager.Domain/
  FormulaEngine/FieldCatalog.cs          ← 83+ alan tanımı (merkezi)
  FormulaEngine/FormulaSet.cs            ← Formül şablonları
  Constants/KasaCanonicalKeys.cs         ← 70+ standart alan key
  Constants/KasaFieldMapper.cs           ← Legacy→Canonical mapping
  Models/UnifiedKasaRecord.cs            ← Dictionary-based unified model
  Reports/KasaRaporTuru.cs               ← Enum: Sabah, Aksam, Genel, Ortak

src/KasaManager.Application/
  Services/KasaDraftService.cs           ← Ana iş mantığı
  Services/FormulaEngineService.cs       ← Formül motoru
  Orchestration/KasaOrchestrator.cs      ← İstek koordinasyonu

src/KasaManager.Infrastructure/
  Persistence/KasaManagerDbContext.cs    ← 14 DbSet, EF Core config
  Persistence/PersistenceDependencyInjection.cs ← DI + SQL instance resolver
  Migrations/                            ← 7 migration dosyası

src/KasaManager.Web/
  Program.cs                             ← DI, Kestrel, middleware, auth
  DbBootstrapExtensions.cs              ← Migration + seed (admin, formulas)
  appsettings.json                       ← Development ayarları
  appsettings.Production.json            ← Kurumsal ağ ayarları (YENİ)
  Controllers/                           ← 20 controller
  Views/                                 ← 17 view klasörü
```

---

> 💡 **Yeni oturumda bu dokümanı Helpy'ye gösterin:**  
> _"KasaManager projesi — devam dokümanını oku ve kaldığımız yerden devam edelim."_
> Veya doğrudan yapılacak işi söyleyin, Helpy bu dokümanı proje kökünde bulacaktır.
