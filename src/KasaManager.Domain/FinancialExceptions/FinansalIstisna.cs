#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// Operasyonel finansal istisna kaydı.
/// Başarısız virman, sisteme girilmeyen EFT iade, kısmi işlem vb.
/// durumları profesyonel şekilde temsil eder.
/// 
/// ARCHITECTURE LOCK:
/// - HesapKontrolKaydi'den bağımsızdır.
/// - FormulaEngine'i değiştirmez.
/// - Hesaplamaya yalnızca KararDurumu=Onaylandi ve Durum IN (Acik, KismiCozuldu) ise katılır.
/// - ErtesiGuneDevredildi kayıtları hiçbir gün hesaplamaya katılmaz.
/// </summary>
public sealed class FinansalIstisna
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Zaman ───

    /// <summary>Finansal olayın gerçekleştiği gün</summary>
    public DateOnly IslemTarihi { get; set; }

    /// <summary>Sisteme girildiği gün (null = henüz girilmedi)</summary>
    public DateOnly? SistemGirisTarihi { get; set; }

    // ─── Sınıflandırma ───

    /// <summary>İstisna türü — köken tanımı</summary>
    public IstisnaTuru Tur { get; set; }

    /// <summary>Fonksiyonel gruplama</summary>
    public IstisnaKategorisi Kategori { get; set; }

    /// <summary>İlgili banka hesabı türü (Tahsilat, Harç, Stopaj)</summary>
    public BankaHesapTuru HesapTuru { get; set; }

    /// <summary>Hedef hesap açıklama metni (opsiyonel)</summary>
    public string? HedefHesapAciklama { get; set; }

    // ─── Tutarlar ───

    /// <summary>İşlemin planlandığı/beklenen tutarı</summary>
    public decimal BeklenenTutar { get; set; }

    /// <summary>Bankada gerçekleşen tutar</summary>
    public decimal GerceklesenTutar { get; set; }

    /// <summary>Operasyonel sisteme girilen tutar</summary>
    public decimal SistemeGirilenTutar { get; set; }

    // ─── Etki ───

    /// <summary>
    /// Operasyonel nakit etkisi (mutabakat farkı DEĞİL).
    /// Artiran = kasada beklenenden fazla nakit, Azaltan = beklenenden az.
    /// </summary>
    public KasaEtkiYonu EtkiYonu { get; set; }

    // ─── Operatör Karar Durumu ───

    /// <summary>
    /// Yalnızca Onaylandi durumundaki kayıtlar hesaplamaya katılır.
    /// </summary>
    public KararDurumu KararDurumu { get; set; } = KararDurumu.IncelemeBekliyor;

    // ─── Yaşam Döngüsü ───

    /// <summary>Yaşam döngüsü durumu</summary>
    public IstisnaDurumu Durum { get; set; } = IstisnaDurumu.Acik;

    /// <summary>İstisnanın nedeni (serbest metin)</summary>
    public string? Neden { get; set; }

    /// <summary>Ek açıklama</summary>
    public string? Aciklama { get; set; }

    // ─── Audit ───

    public DateTime OlusturmaTarihiUtc { get; set; } = DateTime.UtcNow;
    public string? OlusturanKullanici { get; set; }
    public DateTime? GuncellemeTarihiUtc { get; set; }
    public string? GuncelleyenKullanici { get; set; }
    public DateOnly? CozulmeTarihi { get; set; }

    // ─── Karar Audit ───

    /// <summary>Onay/red kararını veren kullanıcı</summary>
    public string? KararVerenKullanici { get; set; }

    /// <summary>Karar zamanı (UTC)</summary>
    public DateTime? KararTarihiUtc { get; set; }

    // ─── Faz 2: Devretme İlişkisi ───

    /// <summary>Ertesi güne devredildiğinde orijinal kayıt referansı (opsiyonel).</summary>
    public Guid? ParentIstisnaId { get; set; }
}
