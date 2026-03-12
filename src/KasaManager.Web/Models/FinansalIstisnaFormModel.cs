using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Web.Models;

/// <summary>
/// İstisna oluşturma form modeli.
/// DB-1 FIX: Controller dosyasından Web/Models altına taşındı.
/// </summary>
public sealed class FinansalIstisnaFormModel
{
    public DateOnly IslemTarihi { get; set; }
    public IstisnaTuru Tur { get; set; }
    public IstisnaKategorisi Kategori { get; set; }
    public BankaHesapTuru HesapTuru { get; set; }
    public decimal BeklenenTutar { get; set; }
    public decimal GerceklesenTutar { get; set; }
    public decimal SistemeGirilenTutar { get; set; }
    public KasaEtkiYonu EtkiYonu { get; set; }
    public string? Neden { get; set; }
    public string? Aciklama { get; set; }
    public string? HedefHesapAciklama { get; set; }
    public string? RedirectKasaType { get; set; }
}
