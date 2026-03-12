#nullable enable
namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// Faz 3.1: Financial Exception üzerindeki her durum/karar/tutar değişiminin
/// audit-grade temporal kaydı.
/// </summary>
public sealed class FinansalIstisnaHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>İlgili FinansalIstisna kayıt ID'si.</summary>
    public Guid FinansalIstisnaId { get; set; }

    // ─── Event Bilgisi ───

    public IstisnaHistoryEventType EventType { get; set; }

    public DateTime EventTarihiUtc { get; set; } = DateTime.UtcNow;

    public string? EventKullanici { get; set; }

    public string? Aciklama { get; set; }

    // ─── Önceki Durum (snapshot) ───

    public KararDurumu? OldKararDurumu { get; set; }
    public IstisnaDurumu? OldDurum { get; set; }
    public decimal? OldBeklenenTutar { get; set; }
    public decimal? OldGerceklesenTutar { get; set; }
    public decimal? OldSistemeGirilenTutar { get; set; }

    // ─── Yeni Durum (snapshot) ───

    public KararDurumu? NewKararDurumu { get; set; }
    public IstisnaDurumu? NewDurum { get; set; }
    public decimal? NewBeklenenTutar { get; set; }
    public decimal? NewGerceklesenTutar { get; set; }
    public decimal? NewSistemeGirilenTutar { get; set; }
}

/// <summary>
/// Faz 3.1: History event tipleri.
/// </summary>
public enum IstisnaHistoryEventType
{
    Created = 0,
    KararOnaylandi = 1,
    KararReddedildi = 2,
    DurumDegisti = 3,
    TutarGuncellendi = 4,
    KismiCozuldu = 5,
    Cozuldu = 6,
    ErtesiGuneDevredildi = 7,
    IptalEdildi = 8,
    CreateFromDevredilmis = 9
}
