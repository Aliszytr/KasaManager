#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Banka Hesap Kontrol modülü — ana servis arayüzü.
/// Karşılaştırma raporlarından otomatik analiz, gün arası eşleştirme,
/// kullanıcı onayı ve sorgu işlemlerini sağlar.
/// </summary>
public interface IBankaHesapKontrolService
{
    // ─── Analiz ───

    /// <summary>
    /// Karşılaştırma raporlarından HesapKontrolKaydi'leri otomatik oluşturur.
    /// Fazla kayıtlar (SurplusBankaRecords) ve Eksik kayıtlar (MissingBankaRecords)
    /// HesapKontrolKaydi olarak DB'ye yazılır.
    /// </summary>
    Task<HesapKontrolRapor> AnalyzeFromComparisonAsync(
        DateOnly analizTarihi,
        string uploadFolder,
        CancellationToken ct = default);

    /// <summary>
    /// Gün arası otomatik eşleştirme (güven seviyeli).
    /// Tam güven (DosyaNo doğrulanmış) → otomatik çözüldü.
    /// Kısmi güven (sadece tutar eşleşmesi) → potansiyel eşleşme, kullanıcı onayı beklenir.
    /// </summary>
    Task<CrossDayResult> CrossDayReconcileAsync(
        DateOnly bugunTarihi,
        CancellationToken ct = default);

    /// <summary>
    /// Stopaj virman kontrolü.
    /// ToplamStopaj değerini BankaTahsilat'taki virman kayıtlarıyla karşılaştırır.
    /// </summary>
    Task<StopajVirmanDurum> CheckStopajVirmanAsync(
        DateOnly tarihi,
        decimal toplamStopaj,
        string uploadFolder,
        CancellationToken ct = default);

    // ─── Kullanıcı Etkileşimi ───

    /// <summary>
    /// Kullanıcı onayı ile kaydı kapatır.
    /// </summary>
    Task<bool> ConfirmMatchAsync(Guid kayitId, string kullanici, string? not, CancellationToken ct = default);

    /// <summary>
    /// Kaydı iptal eder.
    /// </summary>
    Task<bool> CancelAsync(Guid kayitId, string kullanici, string? sebep, CancellationToken ct = default);

    /// <summary>
    /// Kaydı takibe alır: Açık → Takipte.
    /// Kullanıcı kaydın gerçek eksik/fazla olduğunu onaylar, sistem izlemeye başlar.
    /// </summary>
    Task<bool> StartTrackingAsync(Guid kayitId, string kullanici, string? not, CancellationToken ct = default);

    /// <summary>
    /// Takipteki kaydı el ile çözüldü olarak işaretler: Takipte → Onaylandı.
    /// </summary>
    Task<bool> ResolveTrackedAsync(Guid kayitId, string kullanici, string? not, CancellationToken ct = default);

    /// <summary>
    /// Herhangi bir kapalı/takipte kaydı geri alır.
    /// Takipte/Onaylandı/Çözüldü → Açık, İptal → Açık.
    /// </summary>
    Task<bool> RevertAsync(Guid kayitId, string kullanici, string? sebep, CancellationToken ct = default);

    /// <summary>
    /// Kısmi güvenli CrossDay potansiyel eşleşmeyi kullanıcı onaylar.
    /// İki kayıt da Çözüldü olarak işaretlenir.
    /// </summary>
    Task<bool> ApprovePotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, string kullanici, CancellationToken ct = default);

    /// <summary>
    /// Kısmi güvenli CrossDay potansiyel eşleşmeyi kullanıcı reddeder.
    /// Eksik kayıt Takipte kalır, potansiyel eşleşme notu eklenir.
    /// </summary>
    Task<bool> RejectPotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, string kullanici, CancellationToken ct = default);

    // ─── Sorgulama ───

    /// <summary>
    /// Açık kayıtları getirir (dashboard + HesapKontrol sayfası).
    /// </summary>
    Task<List<HesapKontrolKaydi>> GetOpenItemsAsync(
        BankaHesapTuru? hesapTuru = null,
        DateOnly? baslangic = null,
        DateOnly? bitis = null,
        CancellationToken ct = default);

    /// <summary>
    /// Takipteki kayıtları getirir.
    /// </summary>
    Task<List<HesapKontrolKaydi>> GetTrackedItemsAsync(
        BankaHesapTuru? hesapTuru = null,
        CancellationToken ct = default);

    /// <summary>
    /// Tüm kayıtları getirir (filtrelenebilir).
    /// </summary>
    Task<List<HesapKontrolKaydi>> GetHistoryAsync(
        DateOnly baslangic,
        DateOnly bitis,
        BankaHesapTuru? hesapTuru = null,
        KayitDurumu? durum = null,
        CancellationToken ct = default);

    /// <summary>
    /// Takip yaşam döngüsü sorgusu.
    /// Sadece TakipBaslangicTarihi set edilmiş (gerçekten takibe alınmış) kayıtları getirir.
    /// Durumları ne olursa olsun (Takipte/Çözüldü/Onaylandı) — tam yaşam döngüsü.
    /// </summary>
    Task<List<HesapKontrolKaydi>> GetTrackingLifecycleAsync(
        DateOnly baslangic,
        DateOnly bitis,
        BankaHesapTuru? hesapTuru = null,
        KayitDurumu? durum = null,
        CancellationToken ct = default);

    /// <summary>
    /// Takip özet bilgisini döndürür:
    /// - Aktif takipteki kümülatif toplamlar (tüm günlerin birikimleri)
    /// - Son çözülen kayıtlar (bugün CrossDay ile gelmiş olanlar)
    /// - Gün bazlı kırılım
    /// </summary>
    Task<TakipOzeti> GetTrackingSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Dashboard özet bilgileri.
    /// </summary>
    Task<HesapKontrolDashboard> GetDashboardAsync(DateOnly? analizTarihi = null, CancellationToken ct = default);

    /// <summary>
    /// Faz 3: Belirli bir tarih için tam dashboard verisini yeniden oluşturur.
    /// Bugün ne görülüyorsa, geçmiş bir tarih için de aynı veri yapısı döner.
    /// </summary>
    Task<HesapKontrolDateSnapshot> GetDashboardForDateAsync(
        DateOnly tarih,
        CancellationToken ct = default);

    /// <summary>
    /// Sabah Kasa textbox'ları için otomatik doldurma verilerini döndürür.
    /// HesapKontrol çalıştırılmamışsa HasData=false döner.
    /// </summary>
    Task<EksikFazlaAutoFill> GetAutoFillDataAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────
// DTO'lar
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Analiz sonucu özeti.
/// </summary>
public sealed record HesapKontrolRapor(
    DateOnly AnalizTarihi,
    int YeniKayitSayisi,
    int FazlaKayitSayisi,
    int EksikKayitSayisi,
    decimal NetTahsilatFark,
    decimal NetHarcFark,
    StopajVirmanDurum StopajDurum,
    List<CrossDayMatch> CrossDayEslesmeler,
    List<CrossDayMatch> PotansiyelEslesmeler,
    string? OzetMesaj);

/// <summary>
/// CrossDay eşleştirme güven seviyesi.
/// </summary>
public enum CrossDayGuven
{
    /// <summary>DosyaNo doğrulanmış — kesin eşleşme → otomatik çözüldü</summary>
    Tam = 0,
    /// <summary>Sadece tutar eşleşmesi — tesadüf riski → kullanıcı onayı gerekir</summary>
    Kismi = 1
}

/// <summary>
/// Gün arası eşleşme sonucu.
/// </summary>
public sealed record CrossDayMatch(
    Guid EksikKayitId,
    Guid FazlaKayitId,
    decimal Tutar,
    BankaHesapTuru HesapTuru,
    DateOnly EksikTarihi,
    DateOnly FazlaTarihi,
    CrossDayGuven Guven = CrossDayGuven.Tam,
    string? EksikDosyaNo = null,
    string? EksikBirimAdi = null,
    string? FazlaAciklama = null);

/// <summary>
/// CrossDay eşleştirme sonuçları: kesin eşleşmeler + potansiyel (onay bekleyen) eşleşmeler.
/// </summary>
public sealed record CrossDayResult(
    List<CrossDayMatch> KesirEslesmeler,
    List<CrossDayMatch> PotansiyelEslesmeler);

/// <summary>
/// Stopaj virman kontrolü sonucu.
/// </summary>
public sealed record StopajVirmanDurum(
    bool VirmanYapildiMi,
    decimal BeklenenTutar,
    decimal? BulunanVirmanTutar,
    string Mesaj);

/// <summary>
/// Dashboard özet kartı.
/// </summary>
public sealed record HesapKontrolDashboard(
    int AcikKayitSayisi,
    int BeklenenSayisi,
    int BilinmeyenSayisi,
    decimal AcikEksikToplam,
    decimal AcikFazlaToplam,
    int BugunCozulenSayisi,
    int TakipteSayisi,
    decimal TakipteEksikToplam,
    decimal TakipteFazlaToplam,
    StopajVirmanDurum? LastStopajDurum = null);

/// <summary>
/// Faz 3: Belirli bir tarih için tam HesapKontrol ekranı verisi.
/// Bugünkü ekranda ne görülüyorsa, geçmiş tarih için de aynı yapıda veri döner.
/// </summary>
public sealed record HesapKontrolDateSnapshot(
    DateOnly Tarih,
    HesapKontrolDashboard Dashboard,
    List<HesapKontrolKaydi> AcikKayitlar,
    List<HesapKontrolKaydi> TakipteKayitlar,
    List<HesapKontrolKaydi> OnaylananKayitlar,
    List<HesapKontrolKaydi> CozulenKayitlar,
    List<HesapKontrolKaydi> IptalKayitlar,
    EksikFazlaAutoFill AutoFill,
    string? OzetMesaj);

/// <summary>
/// Sabah Kasa textbox otomatik doldurma verisi.
/// HesapKontrol çalıştırılmamışsa HasData=false döner,
/// textbox'lar boş kalır.
/// </summary>
public sealed record EksikFazlaAutoFill(
    decimal GuneAitEksikFazlaTahsilat,
    decimal GuneAitEksikFazlaHarc,
    decimal OncekiGunAcikTahsilat,
    decimal OncekiGunAcikHarc,
    decimal BugunCozulenTahsilat,
    decimal BugunCozulenHarc,
    bool HasData,
    string? InfoMessage,
    // Takipte kayıt toplamları
    decimal TakipteEksikTahsilat = 0,
    decimal TakipteEksikHarc = 0,
    decimal TakipteFazlaTahsilat = 0,
    decimal TakipteFazlaHarc = 0,
    int TakipteSayisi = 0,
    // ─── Faz 1: Detaylı Kırılım (Breakdown) ───
    /// <summary>Beklenen (EFT İade + Havale + Mevduat vb.) toplam — Tahsilat</summary>
    decimal BeklenenTahsilat = 0,
    /// <summary>Olağan Dışı (Bilinmeyen sınıfı) toplam — Tahsilat</summary>
    decimal OlaganDisiTahsilat = 0,
    /// <summary>Beklenen (EFT İade + Havale + Mevduat vb.) toplam — Harç</summary>
    decimal BeklenenHarc = 0,
    /// <summary>Olağan Dışı (Bilinmeyen sınıfı) toplam — Harç</summary>
    decimal OlaganDisiHarc = 0,
    /// <summary>Tüm Açık+Onaylanmış kayıtların net toplamı — Tahsilat (karar beklenmeden)</summary>
    decimal ToplamFarkTahsilat = 0,
    /// <summary>Tüm Açık+Onaylanmış kayıtların net toplamı — Harç (karar beklenmeden)</summary>
    decimal ToplamFarkHarc = 0,
    /// <summary>Kırılım açıklaması — Tahsilat. Ör: "EFT iade 1.317,50 ₺, Olağan dışı 300 ₺"</summary>
    string? BreakdownMesajTahsilat = null,
    /// <summary>Kırılım açıklaması — Harç</summary>
    string? BreakdownMesajHarc = null,
    // ─── Akıllı Takip Korelasyonu ───
    /// <summary>Bugün çözülen + hâlâ takipte olan kayıtların detayları (KasaPreview UI durum etiketleri için)</summary>
    List<TakipCozumDetay>? TakipCozumleri = null,
    /// <summary>Proaktif bildirim mesajı: hangi takip kayıtları çözüldü</summary>
    string? TakipCozumBildirim = null);

/// <summary>
/// Kasa hesaplama sırasında çözülen/mevcut takip kaydının detayı.
/// KasaPreview UI'da "Geldi ✅" / "Takipte 📌" etiketi göstermek için kullanılır.
/// </summary>
public sealed record TakipCozumDetay(
    BankaHesapTuru HesapTuru,
    decimal Tutar,
    /// <summary>"Geldi" = bugün CrossDay ile çözüldü, "TakipteDevam" = hâlâ takipte</summary>
    string Durum,
    DateOnly AnalizTarihi,
    string? DosyaNo,
    string? Aciklama);

/// <summary>
/// Takip motoru kümülatif özet bilgisi.
/// Dashboard ve Takipte sekmesinde toplam takip açığını göstermek için kullanılır.
/// </summary>
public sealed record TakipOzeti(
    /// <summary>Şu an Takipte durumda olan kayıt sayısı</summary>
    int AktifTakipSayisi,
    /// <summary>Aktif takipteki toplam eksik tutarı (kasaya gelmesi beklenen)</summary>
    decimal ToplamEksik,
    /// <summary>Aktif takipteki toplam fazla tutarı</summary>
    decimal ToplamFazla,
    /// <summary>Ortalama takip süresi (gün)</summary>
    double OrtalamaGun,
    /// <summary>En eski takipteki kaydın gün sayısı</summary>
    int EnEskiGun,
    /// <summary>Bugün CrossDay ile otomatik çözülen kayıtlar</summary>
    List<HesapKontrolKaydi> BugunCozulenler,
    /// <summary>Bugün çözülenlerin toplam tutarı</summary>
    decimal BugunCozulenToplam,
    /// <summary>Gün bazlı kırılım: kaç kayıt kaç gündür takipte</summary>
    List<GunBazliTakip> GunBazliKirilim);

/// <summary>
/// Gün bazlı takip kırılımı — "2 gün: 3 kayıt, 15.000₺" gibi.
/// </summary>
public sealed record GunBazliTakip(
    int GunSayisi,
    int KayitSayisi,
    decimal ToplamTutar,
    string Seviye);  // "normal", "uyari", "kritik"
