using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Validation;

namespace KasaManager.Web.Models;

public sealed class KasaPreviewViewModel
{
    public DateOnly? SelectedDate { get; set; }

    /// <summary>
    /// Intent-First: Hangi kasa türüyle çalışıldığı (Sabah/Aksam/Genel/Ortak/Custom).
    /// Dashboard'dan gelen kasaType parametresinden belirlenir.
    /// </summary>
    public string KasaType { get; set; } = "";

    /// <summary>
    /// Admin modu: R16 Designer bölümünün görünürlüğünü kontrol eder.
    /// Normal kullanıcılar bu alanı görmez.
    /// </summary>
    public bool IsAdminMode { get; set; }

    /// <summary>
    /// Akşam Kasa: Mesai Sonu modunda sadece 4 dosya okunur (OnlineMasraf, OnlineHarc, MasrafveReddiyat atlanır).
    /// Tam Gün modunda (false) tüm 7 dosya okunur (geriye dönük analiz için).
    /// </summary>
    public bool AksamMesaiSonuModu { get; set; } = true;

    /// <summary>
    /// Hesapla butonu sonrası sonuçların gösterilip gösterilmediği.
    /// Progressive Disclosure: true ise ADIM 3 (Sonuçlar) görünür.
    /// </summary>
    public bool HasResults { get; set; }

    // ===== R10: Genel Kasa Tarih Aralığı (metadata) =====
    // NOT: Tarihler formül input'u değildir; checkbox/pool içine girmez.
    // R16 Preview'da rapor aralığını şeffaf göstermek için ViewModel üzerinde taşınır.
    public DateOnly? GenelKasaStartDate { get; set; }
    public DateOnly? GenelKasaEndDate { get; set; }
    public string? GenelKasaStartDateSource { get; set; }
    public string? GenelKasaEndDateSource { get; set; }

    // ===== R16: Designer/Studio opsiyonel test araligi (sol menu scope) =====
    // Bos birakilirsa: Tam dosya toplami (FULL EXCEL TOTAL)
    public DateOnly? DesignerStartDate { get; set; }
    public DateOnly? DesignerEndDate { get; set; }
    public string? InputCatalogScopeLabel { get; set; }


    /// <summary>
    /// R9: "Verileri Getir" başarılı olduysa true.
    /// Snapshot yüklenmeden ve otomatik alanlar doldurulmadan "Hesapla" yapılmaz.
    /// </summary>
    public bool IsDataLoaded { get; set; }

    /// <summary>
    /// R9: Seçilen gün için snapshot bulunup bulunmadığı.
    /// </summary>
    public bool HasSnapshot { get; set; }

    // Dropdown options (KasaUstRapor snapshot'ından)
    public List<string> VeznedarOptions { get; set; } = new();

    /// <summary>
    /// Vergi Kasa: Bu günün Genel snapshot'ında işaretlenmiş (IsSelected=true) veznedarlar.
    /// Kaynak: KasaÜstRapor ekranında seçilen checkbox'lar.
    /// </summary>
    public List<string> VergiKasaVeznedarlar { get; set; } = new();

    /// <summary>
    /// Vergi Kasa toplamı (snapshot'taki seçime göre).
    /// UI hesap yapmaz; burada sadece gösterim/taşıma amaçlıdır.
    /// </summary>
    public decimal VergiKasaBakiyeToplam { get; set; }

    /// <summary>
    /// R9.1 (Vizyon): Ayarlardan gelen varsayılanlar.
    /// Kullanıcı isterse override edebilir; bu fazda DB'ye yazılmaz.
    /// </summary>
    public decimal? DefaultBozukPara { get; set; }

    public decimal? DefaultNakitPara { get; set; }

    // ===== R10/R11: Genel Kasa & Carryover (Ayarlar'dan gelen varsayılanlar) =====
    // NOTE: Bunlar UnifiedPool (HAM) içine yazılmaz. UI/Engine context override olarak kullanılır.
    public decimal? DefaultGenelKasaDevredenSeed { get; set; }
    public DateOnly? DefaultGenelKasaBaslangicTarihiSeed { get; set; }
    public decimal? DefaultKaydenTahsilat { get; set; }
    public decimal? DefaultDundenDevredenKasaNakit { get; set; }

    /// <summary>
    /// Vergiden Gelen (manuel). Excel'den gelmez; hesaplamayı etkiler.
    /// </summary>
    public decimal? VergidenGelen { get; set; }

    /// <summary>
    /// Gelmeyen D. (manuel). Excel'den gelmez; mutabakatı etkiler.
    /// Not: Devreden ile karıştırılmamalı.
    /// </summary>
    public decimal? GelmeyenD { get; set; }

    // ===== R13: Akşam/Sabah kasa için kullanıcı girişleri =====
    public decimal? KasadaKalacakHedef { get; set; }
    public decimal? KaydenTahsilat { get; set; }
    public decimal? KaydenHarc { get; set; }
    public decimal? BankadanCekilen { get; set; }
    public decimal? CesitliNedenlerleBankadanCikamayanTahsilat { get; set; }
    public decimal? BankayaGonderilmisDeger { get; set; }
    public decimal? BankayaYatirilacakHarciDegistir { get; set; }
    public decimal? BankayaYatirilacakTahsilatiDegistir { get; set; }

    // ===== Eksik/Fazla Kullanıcı Girişleri (Sabah Kasa) =====
    public decimal? GuneAitEksikFazlaTahsilat { get; set; }
    public decimal? GuneAitEksikFazlaHarc { get; set; }
    public decimal? DundenEksikFazlaTahsilat { get; set; }
    public decimal? DundenEksikFazlaHarc { get; set; }
    public decimal? DundenEksikFazlaGelenTahsilat { get; set; }
    public decimal? DundenEksikFazlaGelenHarc { get; set; }

    /// <summary>
    /// HesapKontrol modülünden gelen auto-fill bilgi mesajı.
    /// Null ise mesaj gösterilmez.
    /// </summary>
    public string? HesapKontrolAutoFillMessage { get; set; }

    // ===== Takipte Kayıt Özeti (HesapKontrol) =====
    public decimal TakipteEksikTahsilat { get; set; }
    public decimal TakipteEksikHarc { get; set; }
    public decimal TakipteFazlaTahsilat { get; set; }
    public decimal TakipteFazlaHarc { get; set; }
    public int TakipteSayisi { get; set; }

    // ===== Faz 1: Detaylı Kırılım (Breakdown) — Sabah Kasa Kartı =====
    /// <summary>Tüm aktif kayıtların toplam farkı (Açık+Onaylanmış+Takipte+Çözüldü)</summary>
    public decimal ToplamFarkTahsilat { get; set; }
    /// <summary>Tüm aktif kayıtların toplam farkı (Açık+Onaylanmış+Takipte+Çözüldü)</summary>
    public decimal ToplamFarkHarc { get; set; }
    /// <summary>Beklenen (EFT İade, Havale vb.) toplamı</summary>
    public decimal BeklenenTahsilat { get; set; }
    public decimal BeklenenHarc { get; set; }
    /// <summary>Olağan Dışı (Bilinmeyen sınıfı) toplamı</summary>
    public decimal OlaganDisiTahsilat { get; set; }
    public decimal OlaganDisiHarc { get; set; }
    /// <summary>Kırılım mesajı — ör: "EFT iade 1.317,50 ₺, Olağan dışı 300 ₺"</summary>
    public string? BreakdownMesajTahsilat { get; set; }
    public string? BreakdownMesajHarc { get; set; }

    // ===== CrossDay Otomatik Eşleştirme Sonuçları =====
    /// <summary>CrossDay eşleşme sayısı (kasa hesaplama sırasında tetiklenir)</summary>
    public int CrossDayEslesmeSayisi { get; set; }
    /// <summary>CrossDay eşleşen toplam tutar</summary>
    public decimal CrossDayToplamTutar { get; set; }
    /// <summary>CrossDay bildirim mesajı (null ise eşleşme yok)</summary>
    public string? CrossDayBildirim { get; set; }
    /// <summary>Kısmi güvenli eşleşme sayısı (kullanıcı onayı bekleyen)</summary>
    public int CrossDayPotansiyelSayisi { get; set; }

    // ── Loaded Snapshot (In-Context CRUD) ──
    /// <summary>Yüklü rapor ID'si (null = yeni/boş)</summary>
    public Guid? LoadedSnapshotId { get; set; }
    /// <summary>Yüklü rapor adı (UI gösterimi)</summary>
    public string? LoadedSnapshotName { get; set; }
    /// <summary>Yüklü rapor versiyonu</summary>
    public int? LoadedSnapshotVersion { get; set; }

    // User inputs (Finalize fazında DB'ye yazılacak; burada sadece doğrulama için alınır)
    public string? KasayiYapan { get; set; }
    public string? Aciklama { get; set; }

    /// <summary>
    /// Günlük Kasa Notu: Özel durumlar, uyarılar ve notlar.
    /// Export (PDF/Excel/CSV) ve DB snapshot'a dahil edilir.
    /// </summary>
    public string? GunlukKasaNotu { get; set; }
    public decimal? BozukPara { get; set; }
    public decimal? NakitPara { get; set; }
    public decimal? VergideBirikenKasa { get; set; }

    // Backend output
    public KasaDraftBundle? Drafts { get; set; }

    /// <summary>
    /// R15B: Ortak Veri Havuzu (HAM) görünümü.
    /// </summary>
    public List<UnifiedPoolEntry> PoolEntries { get; set; } = new();

    /// <summary>
    /// R16: Sol panel "Input Catalog".
    /// UnifiedPool (HAM) değişmez çekirdek olarak kalır.
    /// Bu katalog; hem UnifiedPool'daki gerçek input'ları hem de modelde tanımlı
    /// fakat Pool'a girmemesi gereken "virtual" alanları (çıktı/taşıma/UI alanları)
    /// kullanıcıya görünür kılmak için kullanılır.
    /// </summary>
    public List<KasaInputCatalogEntry> InputCatalog { get; set; } = new();

    // ===== R16-UI: Tasarım (Input seçimi + Mapping Builder) =====

    /// <summary>
    /// Bu rapor taslağında kullanılacak HAM input canonical key listesi.
    /// UI'da checkbox olarak seçilir.
    /// </summary>
    public List<string> SelectedInputKeys { get; set; } = new();

    /// <summary>
    /// Mapping satırları: "Hangi çıktı alanı nasıl üretilecek?" (Map/Formula)
    /// </summary>
    public List<KasaPreviewMappingRow> Mappings { get; set; } = new();

    /// <summary>
    /// UI'da TargetKey input'u için öneriler (datalist).
    /// value = canonical target key.
    /// </summary>
    public List<string> TargetKeySuggestions { get; set; } = new();

    // ===== R17C: FormulaSet CRUD (DB) =====
    public string? DbFormulaSetId { get; set; }
    public string? DbFormulaSetName { get; set; }
    public string DbScopeType { get; set; } = "Custom";
    public List<FormulaSetListItem> DbFormulaSets { get; set; } = new();
    public string? DbInfoMessage { get; set; }

    // ===== R16: Formula Engine Preview =====
    public string? SelectedFormulaSetId { get; set; }
    public List<FormulaSet> AvailableFormulaSets { get; set; } = new();
    public CalculationRun? FormulaRun { get; set; }

    // ===== R4: Parity & DiffMap =====
    // Legacy Draft (Genel/Sabah/Akşam) ile FormulaEngine çıktıları arasındaki farkları görünür kılar.
    // "Önce doğruluk, sonra genişleme" için temel ölçüm katmanı.
    public List<ParityDiffItem> ParityDiffs { get; set; } = new();

    // Non-blocking uyarılar
    public List<string> Warnings { get; set; } = new();

    public List<string> Errors { get; set; } = new();

    // ===== Validation Uyarı Sistemi =====
    /// <summary>
    /// Hesaplama sonrası kural motoru tarafından tespit edilen uyarılar.
    /// </summary>
    public List<KasaValidationResult> ValidationResults { get; set; } = new();

    /// <summary>
    /// Bu gün + kasa tipi için kullanıcı tarafından dismiss edilen kural kodları.
    /// UI'da dismiss edilmiş uyarıları filtrelemek için kullanılır.
    /// </summary>
    public HashSet<string> DismissedRuleCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ===== KasaÜstRapor Panel (tüm kasa tipleri için) =====
    public KasaUstRaporPanelViewModel? UstRaporPanel { get; set; }

    // ===== Karşılama kartı verileri =====
    public DateOnly? LastSnapshotDate { get; set; }
    public bool HasUploadedFiles { get; set; }

    // ===== IBAN Bilgileri (Bankaya Götürülecek Kartlarda Gösterim) =====
    public string? HesapAdiStopaj { get; set; }
    public string? IbanStopaj { get; set; }
    public string? HesapAdiMasraf { get; set; }
    public string? IbanMasraf { get; set; }
    public string? HesapAdiHarc { get; set; }
    public string? IbanHarc { get; set; }
    public string? IbanPostaPulu { get; set; }

    // ===== Financial Exceptions (vNext Faz 1) =====
    /// <summary>Seçili tarihteki tüm finansal istisnalar.</summary>
    public List<Domain.FinancialExceptions.FinansalIstisna> FinansalIstisnalar { get; set; } = new();

    /// <summary>Faz 3: Anomali önerileri.</summary>
    public List<Application.Abstractions.AnomaliOnerisi> AnomaliOnerileri { get; set; } = new();
}
