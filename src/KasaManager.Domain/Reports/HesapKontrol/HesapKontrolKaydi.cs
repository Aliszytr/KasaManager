#nullable enable
namespace KasaManager.Domain.Reports.HesapKontrol;

// ─────────────────────────────────────────────────────────────
// Enum Tanımları
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Hangi banka hesabına ait analiz.
/// </summary>
public enum BankaHesapTuru
{
    /// <summary>BankaTahsilat (+) hesabı</summary>
    Tahsilat = 0,
    /// <summary>BankaHarc hesabı</summary>
    Harc = 1,
    /// <summary>Stopaj/Virman hesabı</summary>
    Stopaj = 2
}

/// <summary>
/// Tespit edilen farkın yönü.
/// </summary>
public enum KayitYonu
{
    /// <summary>Online'da var → Bankada yok (bedeli gelmemiş)</summary>
    Eksik = 0,
    /// <summary>Bankada var → Online'da yok (EFT iade, havale, mevduat vb.)</summary>
    Fazla = 1
}

/// <summary>
/// Farkın sınıfı — otomatik veya manuel atanır.
/// </summary>
public enum FarkSinifi
{
    /// <summary>EFT İade, Havale, Mevduat → olağan banka etkisi</summary>
    Beklenen = 0,
    /// <summary>EFT takılması, bloke hesap → gecikmeli</summary>
    Askida = 1,
    /// <summary>Hiçbir kalıba uymayan → MANUEL KONTROL</summary>
    Bilinmeyen = 2
}

/// <summary>
/// Kaydın yaşam döngüsü durumu.
/// </summary>
public enum KayitDurumu
{
    /// <summary>Yeni tespit — henüz çözülmedi</summary>
    Acik = 0,
    /// <summary>CrossDay: Ertesi gün geldi (otomatik)</summary>
    Cozuldu = 1,
    /// <summary>Kullanıcı el ile çözüldü dedi</summary>
    Onaylandi = 2,
    /// <summary>Yanlış tespit — yok sayıldı</summary>
    Iptal = 3,
    /// <summary>Kullanıcı onayladı → takibe alındı, sistem izliyor</summary>
    Takipte = 4
}

// ─────────────────────────────────────────────────────────────
// Entity
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Banka Hesap Kontrol modülü — tekil eksik/fazla kaydı.
/// Her bir tutar farkı bu entity olarak veritabanına yazılır,
/// yaşam döngüsü takip edilir (Açık → Çözüldü/Onaylandı → Kapatıldı).
/// </summary>
public sealed class HesapKontrolKaydi
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Ne Zaman ───

    /// <summary>Hangi güne ait Sabah Kasa analizi</summary>
    public DateOnly AnalizTarihi { get; set; }

    /// <summary>Kaydın oluşturulma zamanı (UTC)</summary>
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    // ─── Hangi Hesap ───

    /// <summary>Tahsilat, Harç veya Stopaj</summary>
    public BankaHesapTuru HesapTuru { get; set; }

    // ─── Ne Tespit Edildi ───

    /// <summary>Eksik veya Fazla</summary>
    public KayitYonu Yon { get; set; }

    /// <summary>Fark tutarı (BİREBİR — tolerans yok)</summary>
    public decimal Tutar { get; set; }

    /// <summary>Banka açıklama metni (fazla kayıtlar için)</summary>
    public string? Aciklama { get; set; }

    /// <summary>Online dosya no (eksik kayıtlar için)</summary>
    public string? DosyaNo { get; set; }

    /// <summary>Online birim adı (eksik kayıtlar için)</summary>
    public string? BirimAdi { get; set; }

    // ─── Sınıflandırma ───

    /// <summary>Beklenen, Askıda veya Bilinmeyen</summary>
    public FarkSinifi Sinif { get; set; }

    /// <summary>DetectRecordType++ sonucu: "EFT_OTOMATIK_IADE", "GELEN_HAVALE" vb.</summary>
    public string? TespitEdilenTip { get; set; }

    // ─── Karşılaştırma Bağlantısı ───

    /// <summary>ComparisonReport'taki satır indexi</summary>
    public int? KarsilastirmaSatirIndex { get; set; }

    /// <summary>"TahsilatMasraf" / "HarcamaHarc" / "ReddiyatCikis"</summary>
    public string? KarsilastirmaTuru { get; set; }

    // ─── Yaşam Döngüsü ───

    /// <summary>Açık, Çözüldü, Onaylandı veya İptal</summary>
    public KayitDurumu Durum { get; set; } = KayitDurumu.Acik;

    /// <summary>Çözülme/Kapanma tarihi</summary>
    public DateOnly? CozulmeTarihi { get; set; }

    /// <summary>CrossDay eşleşme ile çözen kaydın ID'si</summary>
    public Guid? CozulmeKaynakId { get; set; }

    // ─── Kullanıcı Etkileşimi ───

    /// <summary>Kullanıcı bu kaydı el ile onayladı mı?</summary>
    public bool KullaniciOnay { get; set; }

    /// <summary>Onaylayan kullanıcı adı</summary>
    public string? OnaylayanKullanici { get; set; }

    /// <summary>Onay zamanı (UTC)</summary>
    public DateTime? OnayTarihi { get; set; }

    /// <summary>Kullanıcı ve sistem notları — arama ve gelecek referans için</summary>
    public string? Notlar { get; set; }

    // ─── Audit ───

    /// <summary>Kaydı oluşturan kullanıcı</summary>
    public string? CreatedBy { get; set; }

    // ─── Geri Alma ───

    /// <summary>Geri alma işlemini yapan kullanıcı (null ise geri alınmamış)</summary>
    public string? GeriAlanKullanici { get; set; }

    /// <summary>Geri alma zamanı (UTC)</summary>
    public DateTime? GeriAlmaTarihi { get; set; }

    // ─── Akıllı Takip Motoru (Faz 2) ───

    /// <summary>Takibe alınma tarihi (DateOnly — gün bazlı)</summary>
    public DateOnly? TakipBaslangicTarihi { get; set; }

    /// <summary>Son otomatik bildirim gönderilme zamanı (UTC)</summary>
    public DateTime? SonBildirimTarihi { get; set; }

    /// <summary>
    /// Takipte geçen gün sayısı (computed — sadece okunur).
    /// Takipte olmayan kayıtlar için null döner.
    /// </summary>
    public int? TakipteGunSayisi =>
        TakipBaslangicTarihi.HasValue && Durum == KayitDurumu.Takipte
            ? DateOnly.FromDateTime(DateTime.Now).DayNumber - TakipBaslangicTarihi.Value.DayNumber
            : null;
}
