#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Web.Models;

public sealed class GenelKasaRaporViewModel
{
    // Tarihler
    [Display(Name = "D.Baş.Tarihi")]
    [DataType(DataType.Date)]
    public DateOnly BaslangicTarihi { get; set; }

    [Display(Name = "D.Bit.Tarihi")]
    [DataType(DataType.Date)]
    public DateOnly BitisTarihi { get; set; }

    [Display(Name = "Dev.Son.Tarihi")]
    [DataType(DataType.Date)]
    public DateOnly DevredenSonTarihi { get; set; }

    [Display(Name = "R.Sn.Tarih")]
    [DataType(DataType.Date)]
    public DateOnly RaporSonTarihi { get; set; }

    [Display(Name = "Tah.Tarihi")]
    [DataType(DataType.Date)]
    public DateOnly TahsilatTarihi { get; set; }

    // Parasal Alanlar
    [Display(Name = "Devreden")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? Devreden { get; set; }

    [Display(Name = "Banka Bakiye")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? BankaBakiye { get; set; }

    [Display(Name = "Toplam Tahsilat")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? ToplamTahsilat { get; set; }

    [Display(Name = "Toplam Reddiyat")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? ToplamReddiyat { get; set; }

    [Display(Name = "Tah.Red Fark")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? TahsilatReddiyatFark { get; set; }

    [Display(Name = "Kayden Tah.")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? KaydenTahsilat { get; set; }

    [Display(Name = "Sn.Dn.Devredecek")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? SonrayaDevredecek { get; set; }

    [Display(Name = "Gelmeyen D.")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? Gelmeyen { get; set; }

    [Display(Name = "K.Nakit")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? KasaNakit { get; set; }

    [Display(Name = "T.Ek.Faz.Mk.")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? EksikYadaFazla { get; set; }

    [Display(Name = "Genel Kasa")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? GenelKasa { get; set; }

    
    [Display(Name = "Mutabakat Farkı (Banka - Beklenen)")]
    [DisplayFormat(DataFormatString = "{0:#,##0.00}")]
    public decimal? MutabakatFarki { get; set; }
// UI/Debug
    public List<string> Issues { get; set; } = new();
    public string? RawJson { get; set; }

    // Form input
    [DataType(DataType.Date)]
    public DateOnly? SelectedBitisTarihi { get; set; }

    // ===== IBAN Bilgileri =====
    public string? HesapAdiStopaj { get; set; }
    public string? IbanStopaj { get; set; }
    public string? HesapAdiMasraf { get; set; }
    public string? IbanMasraf { get; set; }
    public string? HesapAdiHarc { get; set; }
    public string? IbanHarc { get; set; }
    public string? IbanPostaPulu { get; set; }

    // ===== CRUD Snapshot Properties =====
    public Guid? LoadedSnapshotId { get; set; }
    public string? LoadedSnapshotName { get; set; }
    public int LoadedSnapshotVersion { get; set; }
}
