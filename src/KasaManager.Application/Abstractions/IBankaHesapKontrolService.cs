#nullable enable
using KasaManager.Domain.Reports;
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
        int actorUserId,
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
    Task<bool> ConfirmMatchAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default);

    /// <summary>
    /// Kaydı iptal eder.
    /// </summary>
    Task<bool> CancelAsync(Guid kayitId, int actorUserId, string? actorUsername, string? sebep, CancellationToken ct = default);

    /// <summary>
    /// Kaydı takibe alır: Açık → Takipte.
    /// Kullanıcı kaydın gerçek eksik/fazla olduğunu onaylar, sistem izlemeye başlar.
    /// </summary>
    Task<bool> StartTrackingAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default);

    /// <summary>
    /// Takipteki kaydı el ile çözüldü olarak işaretler: Takipte → Onaylandı.
    /// </summary>
    Task<bool> ResolveTrackedAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default);

    /// <summary>
    /// Herhangi bir kapalı/takipte kaydı geri alır.
    /// Takipte/Onaylandı/Çözüldü → Açık, İptal → Açık.
    /// </summary>
    Task<bool> RevertAsync(Guid kayitId, int actorUserId, string? actorUsername, string? sebep, CancellationToken ct = default);

    /// <summary>
    /// Kısmi güvenli CrossDay potansiyel eşleşmeyi kullanıcı onaylar.
    /// İki kayıt da Çözüldü olarak işaretlenir.
    /// </summary>
    Task<bool> ApprovePotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, int actorUserId, string? actorUsername, CancellationToken ct = default);

    /// <summary>
    /// Kısmi güvenli CrossDay potansiyel eşleşmeyi kullanıcı reddeder.
    /// Eksik kayıt Takipte kalır, potansiyel eşleşme notu eklenir.
    /// </summary>
    Task<bool> RejectPotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, int actorUserId, string? actorUsername, CancellationToken ct = default);

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
    /// Takipteki kayitlari secili analiz tarihine kadar olan kayit evreninden getirir.
    /// Entity'nin mevcut durumu kullanilir; tarihsel durum gecmisi uretilmez.
    /// </summary>
    Task<List<HesapKontrolKaydi>> GetTrackedItemsAsync(
        BankaHesapTuru? hesapTuru,
        DateOnly analizTarihi,
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
    /// Takip ozetini secili analiz tarihine kadar olan kayit evreniyle sinirlar.
    /// Entity'nin mevcut durumu kullanilir; tarihsel durum gecmisi uretilmez.
    /// </summary>
    Task<TakipOzeti> GetTrackingSummaryAsync(DateOnly analizTarihi, CancellationToken ct = default);

    /// <summary>
    /// Dashboard özet bilgileri.
    /// BUG-SYNC-1 FIX: hesapTuru parametresi eklendi — OpenItems ile aynı filtreleme semantiği.
    /// </summary>
    Task<HesapKontrolDashboard> GetDashboardAsync(DateOnly? analizTarihi = null, BankaHesapTuru? hesapTuru = null, CancellationToken ct = default);

    /// <summary>
    /// Faz 3: Belirli bir tarih için tam dashboard verisini yeniden oluşturur.
    /// Bugün ne görülüyorsa, geçmiş bir tarih için de aynı veri yapısı döner.
    /// </summary>
    Task<HesapKontrolDateSnapshot> GetDashboardForDateAsync(
        DateOnly tarih,
        CancellationToken ct = default);

    /// <summary>
    /// Karşılaştırma satırlarını son 90 günlük Hesap Kontrol karar hafızası ile
    /// salt okunur biçimde zenginleştirir.
    /// </summary>
    Task EnrichComparisonDecisionMemoryAsync(
        ComparisonReport report,
        BankaHesapTuru hesapTuru,
        DateOnly asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Sabah Kasa textbox'ları için otomatik doldurma verilerini döndürür.
    /// HesapKontrol çalıştırılmamışsa HasData=false döner.
    /// </summary>
    Task<EksikFazlaAutoFill> GetAutoFillDataAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default);

    /// <summary>
    /// Snapshot save icin scalar ozet ve bu ozeti ureten minimum kayit
    /// ayrintilarini ayni materialize edilmis canonical kaynaklardan dondurur.
    /// </summary>
    Task<HesapKontrolImmutableAuditSnapshot> GetImmutableAuditSnapshotAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default);

    /// <summary>
    /// Aktif HesapKontrol SSOT toplamlarini dondurur.
    /// Acik/Takipte kayitlar dahil; Iptal/Cozuldu/Onaylandi haric tutulur.
    /// </summary>
    Task<ActiveFollowTotals> GetActiveFollowTotalsAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default);

    /// <summary>
    /// Secili rapor gununde aktif olan Takipte kayitlarin etkisi ile o gun
    /// cozulmus cross-day takip kayitlarinin ters telafisini tek canonical
    /// kasa gateway degeri olarak dondurur.
    /// </summary>
    Task<ActiveFollowTotals> GetDailyFollowTotalsAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────
// DTO'lar
// ─────────────────────────────────────────────────────────────

public sealed record ActiveFollowTotals(
    DateOnly AnalizTarihi,
    decimal TahsilatNet,
    decimal HarcNet,
    decimal TahsilatEksik,
    decimal HarcEksik,
    decimal TahsilatFazla,
    decimal HarcFazla,
    int KayitSayisi);
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
    string Mesaj,
    StopajStatus Status = StopajStatus.Ok);

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
    HesapKontrolSnapshotSummary Summary,
    EksikFazlaAutoFill AutoFill,
    string? OzetMesaj);

/// <summary>Belirli bir güne ait salt okunur Hesap Kontrol kayıt özeti.</summary>
public sealed record HesapKontrolSnapshotSummary(
    int TotalCount,
    int AcikCount,
    int TakipteCount,
    int IptalCount,
    int CozulduCount,
    int OnaylandiCount,
    int ProcessedCount,
    int BeklenenCount,
    int BilinmeyenCount);

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
    /// <summary>Sabah Kasa genel_kasa için HK reconciliation gateway tahsilat etkisi.</summary>
    decimal TakipKasaEtkisiTahsilat = 0,
    /// <summary>Sabah Kasa genel_kasa için HK reconciliation gateway harç etkisi.</summary>
    decimal TakipKasaEtkisiHarc = 0,
    /// <summary>Sabah Kasa genel_kasa için HK reconciliation gateway net etkisi.</summary>
    decimal TakipKasaEtkisiNet = 0,
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

/// <summary>
/// Immutable snapshot JSON'una alinabilecek minimum Hesap Kontrol kayit gorunumu.
/// EF entity, actor, aciklama/not veya kaynak dosya metadata'si tasimaz.
/// </summary>
public sealed record HesapKontrolImmutableAuditRecord(
    Guid KayitId,
    DateOnly AnalizTarihi,
    BankaHesapTuru HesapTuru,
    KayitYonu Yon,
    decimal Tutar,
    KayitDurumu KaydetmeAnindakiDurum,
    FarkSinifi Sinif,
    string? DosyaNo,
    string? BirimAdi,
    string? TespitEdilenTip,
    DateOnly? TakipBaslangicTarihi,
    DateOnly? CozulmeTarihi,
    DateTime? OnayTarihi);

public sealed record HesapKontrolImmutableAuditDetails(
    IReadOnlyList<HesapKontrolImmutableAuditRecord> Records,
    HesapKontrolImmutableAuditGroups Groups);

public sealed record HesapKontrolImmutableAuditGroups(
    IReadOnlyList<Guid> AktifKayitlar,
    IReadOnlyList<Guid> OncekiAciklar,
    IReadOnlyList<Guid> BugunCozulenler,
    IReadOnlyList<Guid> ReconciliationKayitlar,
    IReadOnlyList<Guid> TakipteKayitlar,
    IReadOnlyList<Guid> BugunTakipCozulenler);

public sealed record HesapKontrolImmutableAuditSnapshot(
    EksikFazlaAutoFill Summary,
    HesapKontrolImmutableAuditDetails Details);

/// <summary>
/// Save ve LoadSnapshot tarafinin paylastigi version-2 structural validation.
/// Validation exception firlatmaz; kullaniciya tasinmayacak kisa hata kodu verir.
/// </summary>
public static class HesapKontrolImmutableAuditDetailsValidator
{
    public static bool TryValidate(
        HesapKontrolImmutableAuditDetails? details,
        out string? error)
    {
        error = null;
        if (details is null)
            return Fail("details-null", out error);
        if (details.Records is null)
            return Fail("records-null", out error);
        if (details.Groups is null)
            return Fail("groups-null", out error);

        var groups = EnumerateGroups(details.Groups).ToArray();
        if (groups.Any(group => group.Ids is null))
            return Fail("group-null", out error);

        var recordsById = new Dictionary<Guid, HesapKontrolImmutableAuditRecord>();
        foreach (var record in details.Records)
        {
            if (record is null)
                return Fail("record-null", out error);
            if (record.KayitId == Guid.Empty || record.AnalizTarihi == default || record.Tutar < 0m)
                return Fail("record-required-field", out error);
            if (!Enum.IsDefined(record.HesapTuru)
                || !Enum.IsDefined(record.Yon)
                || !Enum.IsDefined(record.KaydetmeAnindakiDurum)
                || !Enum.IsDefined(record.Sinif))
                return Fail("record-enum", out error);
            if (record.KaydetmeAnindakiDurum == KayitDurumu.Onaylandi
                && record.OnayTarihi is null)
                return Fail("approved-date-null", out error);
            if (!recordsById.TryAdd(record.KayitId, record))
                return Fail("record-duplicate", out error);
        }

        if (!details.Records.SequenceEqual(OrderRecords(details.Records)))
            return Fail("record-order", out error);

        var referenced = new HashSet<Guid>();
        foreach (var group in groups)
        {
            var ids = group.Ids!;
            if (ids.Count != ids.Distinct().Count())
                return Fail($"group-duplicate:{group.Name}", out error);
            if (!ids.SequenceEqual(ids.OrderBy(id => id)))
                return Fail($"group-order:{group.Name}", out error);

            foreach (var id in ids)
            {
                if (!recordsById.TryGetValue(id, out var record))
                    return Fail($"group-reference:{group.Name}", out error);
                if (!IsValidGroupMember(group.Name, record))
                    return Fail($"group-predicate:{group.Name}", out error);
                referenced.Add(id);
            }
        }

        if (recordsById.Keys.Any(id => !referenced.Contains(id)))
            return Fail("record-unreferenced", out error);

        return true;
    }

    public static IReadOnlyList<HesapKontrolImmutableAuditRecord> OrderRecords(
        IEnumerable<HesapKontrolImmutableAuditRecord> records) => records
        .OrderBy(record => record.AnalizTarihi)
        .ThenBy(record => record.HesapTuru)
        .ThenBy(record => record.Yon)
        .ThenBy(record => record.Sinif)
        .ThenBy(record => record.Tutar)
        .ThenBy(record => record.KayitId)
        .ToArray();

    private static IEnumerable<(string Name, IReadOnlyList<Guid>? Ids)> EnumerateGroups(
        HesapKontrolImmutableAuditGroups groups)
    {
        yield return (nameof(groups.AktifKayitlar), groups.AktifKayitlar);
        yield return (nameof(groups.OncekiAciklar), groups.OncekiAciklar);
        yield return (nameof(groups.BugunCozulenler), groups.BugunCozulenler);
        yield return (nameof(groups.ReconciliationKayitlar), groups.ReconciliationKayitlar);
        yield return (nameof(groups.TakipteKayitlar), groups.TakipteKayitlar);
        yield return (nameof(groups.BugunTakipCozulenler), groups.BugunTakipCozulenler);
    }

    private static bool IsValidGroupMember(
        string groupName,
        HesapKontrolImmutableAuditRecord record) => groupName switch
        {
            nameof(HesapKontrolImmutableAuditGroups.AktifKayitlar) =>
                record.HesapTuru != BankaHesapTuru.Stopaj
                && record.KaydetmeAnindakiDurum is KayitDurumu.Acik or KayitDurumu.Takipte,
            nameof(HesapKontrolImmutableAuditGroups.OncekiAciklar) =>
                record.HesapTuru != BankaHesapTuru.Stopaj
                && record.Sinif != FarkSinifi.Beklenen
                && record.KaydetmeAnindakiDurum is KayitDurumu.Acik or KayitDurumu.Takipte,
            nameof(HesapKontrolImmutableAuditGroups.BugunCozulenler) =>
                record.CozulmeTarihi.HasValue
                && record.Sinif != FarkSinifi.Beklenen
                && record.KaydetmeAnindakiDurum == KayitDurumu.Cozuldu,
            nameof(HesapKontrolImmutableAuditGroups.ReconciliationKayitlar) =>
                record.Sinif == FarkSinifi.Askida
                && record.KaydetmeAnindakiDurum is KayitDurumu.Cozuldu or KayitDurumu.Onaylandi,
            nameof(HesapKontrolImmutableAuditGroups.TakipteKayitlar) =>
                record.HesapTuru != BankaHesapTuru.Stopaj
                && record.KaydetmeAnindakiDurum == KayitDurumu.Takipte,
            nameof(HesapKontrolImmutableAuditGroups.BugunTakipCozulenler) =>
                record.HesapTuru != BankaHesapTuru.Stopaj
                && record.TakipBaslangicTarihi.HasValue
                && record.CozulmeTarihi.HasValue
                && record.KaydetmeAnindakiDurum is KayitDurumu.Cozuldu or KayitDurumu.Onaylandi,
            _ => false
        };

    private static bool Fail(string value, out string? error)
    {
        error = value;
        return false;
    }
}
