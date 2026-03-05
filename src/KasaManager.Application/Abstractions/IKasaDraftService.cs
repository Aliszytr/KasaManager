using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
// NOTE: FAZ-2/Adım-1 bundle types are kept in Abstractions to avoid cross-layer namespace drift.

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R8: Kasa Draft üretimi.
/// Draft = DB'ye yazılmayan, sadece doğrulama amaçlı hesap çıktısı.
/// </summary>
public interface IKasaDraftService
{
    /// <summary>
    /// Seçilen tarihe göre 3 kasa (Genel/Sabah/Akşam) draft çıktısını üretir.
    /// </summary>
    Task<Result<KasaDraftBundle>> BuildAsync(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sadece Genel Kasa kartını True Source v2 mantığıyla (limitsiz MasrafveReddiyat okuma) yeniden üretir.
    /// UI'de 'Yeniden Doldur' butonu için kullanılır.
    /// </summary>
    Task<Result<KasaDraftResult>> BuildGenelKasaTrueSourceV2Async(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        CancellationToken ct = default);

    /// <summary>
    /// FAZ-2 / Adım-1: GenelKasaRapor ekranı "UI-only" olacak.
    /// Bu method, controller'ın hiç hesap yapmadan (sadece input alıp service çağırarak)
    /// FormulaEngine hattını çalıştırabilmesi için gerekli UnifiedPool girişlerini üretir.
    /// </summary>
    Task<Result<GenelKasaR10EngineInputBundle>> BuildGenelKasaR10EngineInputsAsync(
        DateOnly? selectedBitisTarihi,
        decimal? gelmeyenD,
        string uploadFolderAbsolute,
        CancellationToken ct = default);

    /// <summary>
    /// R15B: Ortak Veri Havuzu (HAM).
    /// 
    /// Amaç:
    /// - Raw ham kalır (Excel/Bank/Online okunan değerler)
    /// - Override ham kalır (kullanıcı inputları / ayarlar)
    /// - Derived bu aşamada üretilmez; yalnızca parity için kilit 2 alan (NormalTahsilat/NormalReddiyat)
    /// 
    /// Not: Bu method DB'ye yazmaz ve snapshot üretmez. Sadece UI'de "ham veri"yi göstermek içindir.
    /// </summary>
    Task<Result<IReadOnlyList<UnifiedPoolEntry>>> BuildUnifiedPoolAsync(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        // GenelKasa scope'unda bazı kaynaklar (örn: MasrafveReddiyat) tek gün değil, tarih aralığı toplamı ister.
        // Bu parametreler verilirse ilgili kaynaklar range üzerinden okunur.
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false,
        // R25: SlimPool için kasa scope filtresi
        // Verilirse sadece o kasaya ait alanlar döndürülür (Excel ham verileri her zaman dahil)
        string? kasaScope = null,
        // Akşam Kasa Mesai Sonu modu: true ise OnlineMasraf, OnlineHarc, MasrafveReddiyat atlanır
        bool mesaiSonuModu = false,
        CancellationToken ct = default);

}

/// <summary>
/// R8: Preview ekranındaki kullanıcı girişleri.
/// Bu fazda DB'ye yazılmaz; sadece draft çıktısında BPH/Nakit gibi alanları tamamlamak için kullanılır.
/// </summary>
public sealed class KasaDraftFinalizeInputs
{
    public string? KasayiYapan { get; init; }
    public string? Aciklama { get; init; }
    public decimal? BozukPara { get; init; }
    public decimal? NakitPara { get; init; }

    /// <summary>
    /// Vergi Kasa bakiyesi (checkbox seçimine göre backend'te hesaplanır).
    /// </summary>
    public decimal? VergiKasaBakiyeToplam { get; init; }

    /// <summary>
    /// Vergiden Gelen (manuel).
    /// </summary>
    public decimal? VergidenGelen { get; init; }

    /// <summary>
    /// Genel Kasa için "Gelmeyen D." (manuel). Bu fazda DB'ye yazılmaz; sadece draft hesaplarında kullanılır.
    /// </summary>
    public decimal? GelmeyenD { get; init; }

    // ===== R13: Akşam/Sabah kasa için kullanıcı girişleri (Legacy parity) =====

    /// <summary>
    /// Kasada kalmasını istediğin hedef toplam (bozuk para dahil).
    /// Girilirse BankayaYatirilacakTahsilatiDegistir otomatik önerilir (hedefe oturtmak için).
    /// </summary>
    public decimal? KasadaKalacakHedef { get; init; }

    /// <summary>Vergiden Gelen (manuel) zaten mevcut.</summary>

    /// <summary>Kayden Tahsilat (manuel).</summary>
    public decimal? KaydenTahsilat { get; init; }

    /// <summary>Kayden Harç (manuel).</summary>
    public decimal? KaydenHarc { get; init; }

    /// <summary>Bankadan çekilen nakit (manuel).</summary>
    public decimal? BankadanCekilen { get; init; }

    /// <summary>Çeşitli nedenlerle bankadan çıkamayan tahsilat (manuel).</summary>
    public decimal? CesitliNedenlerleBankadanCikamayanTahsilat { get; init; }

    /// <summary>Bankaya gönderilmiş değer (manuel). (Örn: gün içinde ayrı kanaldan bankaya aktarılan)</summary>
    public decimal? BankayaGonderilmisDeger { get; init; }

    /// <summary>
    /// Bankaya yatırılacak Harcı değiştir (+/-). Harç için manuel düzeltme.
    /// </summary>
    public decimal? BankayaYatirilacakHarciDegistir { get; init; }

    /// <summary>
    /// Bankaya yatırılacak Tahsilatı değiştir (+/-). Tahsilat için manuel düzeltme.
    /// Not: KasadaKalacakHedef girilirse sistem bunun için otomatik bir "öneri" hesaplar; bu alan onun üstüne eklenir.
    /// </summary>
    public decimal? BankayaYatirilacakTahsilatiDegistir { get; init; }

    // ===== Eksik/Fazla Kullanıcı Girişleri (Sabah Kasa) =====
    /// <summary>Güne ait eksik/fazla tahsilat (manuel).</summary>
    public decimal? GuneAitEksikFazlaTahsilat { get; init; }
    /// <summary>Güne ait eksik/fazla harç (manuel).</summary>
    public decimal? GuneAitEksikFazlaHarc { get; init; }
    /// <summary>Dünden kalan eksik/fazla tahsilat (manuel).</summary>
    public decimal? DundenEksikFazlaTahsilat { get; init; }
    /// <summary>Dünden kalan eksik/fazla harç (manuel).</summary>
    public decimal? DundenEksikFazlaHarc { get; init; }
    /// <summary>Dünden gelen eksik/fazla tahsilat (manuel).</summary>
    public decimal? DundenEksikFazlaGelenTahsilat { get; init; }
    /// <summary>Dünden gelen eksik/fazla harç (manuel).</summary>
    public decimal? DundenEksikFazlaGelenHarc { get; init; }
}

public sealed class KasaDraftBundle
{
    public DateOnly RaporTarihi { get; init; }

    public KasaDraftResult Genel { get; init; } = new();
    public KasaDraftResult Sabah { get; init; } = new();
    public KasaDraftResult Aksam { get; init; } = new();

    /// <summary>
    /// Draft üretiminde tespit edilen sorunlar/uyarılar.
    /// </summary>
    public List<string> Issues { get; init; } = new();
}

public sealed class KasaDraftResult
{
    /// <summary>
    /// UI kart başlığı için.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Alanlar: "FieldName" => "Value" (string render)
    /// </summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UI'de alanın hemen altında gösterilecek inline formül/açıklama metinleri.
    /// Key = Fields anahtarı. Value = çok satırlı düz metin (UI tarafı satır kırılımını <br> yapar).
    /// </summary>
    public Dictionary<string, string> InlineFormulas { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// İsteğe bağlı ham JSON (yan panelde göstermek için)
    /// </summary>
    public string? RawJson { get; init; }
}

/// <summary>
/// R15B: Ortak Veri Havuzu satırı.
/// Ham veri = Raw/Override; Derived bu fazda sadece parity için üretilir.
/// </summary>
public sealed class UnifiedPoolEntry
{
    public string CanonicalKey { get; init; } = string.Empty;

    /// <summary>
    /// UI gösterimi için string tutulur. (decimal formatlama vb. burada yapılır)
    /// </summary>
    public string Value { get; init; } = string.Empty;

    public UnifiedPoolValueType Type { get; init; }

    /// <summary>
    /// Hesap motoru aşamasında: 0 ise hesaplamaya dahil edilmez.
    /// Şimdilik UI'de sadece bilgi amaçlı gösterilir.
    /// </summary>
    public bool IncludeInCalculations { get; init; }

    public string SourceName { get; init; } = string.Empty;
    public string? SourceFile { get; init; }
    public string? SourceDetails { get; init; }

    public string? Notes { get; init; }
}

public enum UnifiedPoolValueType
{
    Raw = 0,
    Override = 1,
    Derived = 2
}