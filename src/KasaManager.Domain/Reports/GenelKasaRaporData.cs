#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Genel Kasa Raporu export DTO.
/// İki tarih arası kasa durumu — Sabah/Akşam kasası ile alakası yok.
/// UI kartlarının birebir yansıması.
/// </summary>
public sealed class GenelKasaRaporData
{
    // ══════════════════════════════════════════════════════
    // TARİH BİLGİLERİ
    // ══════════════════════════════════════════════════════
    public DateOnly BaslangicTarihi { get; set; }
    public DateOnly BitisTarihi { get; set; }
    public DateOnly DevredenSonTarihi { get; set; }

    // ══════════════════════════════════════════════════════
    // ANA METRİKLER
    // ══════════════════════════════════════════════════════
    public decimal Devreden { get; set; }
    public decimal ToplamTahsilat { get; set; }
    public decimal ToplamReddiyat { get; set; }
    public decimal TahsilatReddiyatFark { get; set; }

    // ══════════════════════════════════════════════════════
    // BANKA & DEVİR BİLGİLERİ
    // ══════════════════════════════════════════════════════
    public decimal BankaBakiye { get; set; }
    public decimal KaydenTahsilat { get; set; }
    public decimal SonrayaDevredecek { get; set; }

    // ══════════════════════════════════════════════════════
    // KASA DETAYLARI
    // ══════════════════════════════════════════════════════
    public decimal KasaNakit { get; set; }
    public decimal EksikYadaFazla { get; set; }
    public decimal Gelmeyen { get; set; }

    // ══════════════════════════════════════════════════════
    // SONUÇLAR
    // ══════════════════════════════════════════════════════
    public decimal GenelKasa { get; set; }
    public decimal MutabakatFarki { get; set; }

    // ══════════════════════════════════════════════════════
    // META
    // ══════════════════════════════════════════════════════
    public string? Hazirlayan { get; set; }
    public List<string> Issues { get; set; } = new();

    // ══════════════════════════════════════════════════════
    // UYARI BİLGİLERİ (DATE MISMATCH & DIAGNOSTICS)
    // ══════════════════════════════════════════════════════
    public BankaBakiyeDiagnosticInfo? BankaBakiyeDiagnostic { get; set; }
    public BankaMismatchType BankaMismatchType { get; set; }
}
