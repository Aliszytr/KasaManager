namespace KasaManager.Domain.Reports;

/// <summary>
/// Banka Yatırma Fişi verileri — QuestPDF A5 belge için düz DTO.
/// </summary>
public sealed class BankaFisiData
{
    public DateOnly Tarih { get; set; }
    public string KasaTuru { get; set; } = "Akşam";
    public string? Hazirlayan { get; set; }

    // ── Stopaj ──
    public string? HesapAdiStopaj { get; set; }
    public string? IbanStopaj { get; set; }
    public decimal TutarStopaj { get; set; }

    // ── Tahsilat (Masraf) ──
    public string? HesapAdiMasraf { get; set; }
    public string? IbanMasraf { get; set; }
    public decimal TutarMasraf { get; set; }

    // ── Harç ──
    public string? HesapAdiHarc { get; set; }
    public string? IbanHarc { get; set; }
    public decimal TutarHarc { get; set; }

    // ── Toplam ──
    public decimal BankayaToplam => TutarStopaj + TutarMasraf + TutarHarc;

    // ── Kasa & Banka Devir Bilgileri ──
    public decimal KasadakiNakit { get; set; }
    public decimal DundenDevredenBanka { get; set; }
    public decimal YarinaDevredecekBanka { get; set; }
}
