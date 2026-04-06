using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Web.Models;

/// <summary>
/// HesapKontrol sayfası için strongly-typed ViewModel.
/// ViewBag kullanımı yerine tip güvenli model geçişi sağlar.
/// </summary>
public sealed class HesapKontrolViewModel
{
    public HesapKontrolDashboard? Dashboard { get; set; }
    public List<HesapKontrolKaydi> AcikKayitlar { get; set; } = new();
    public List<HesapKontrolKaydi> TakipteKayitlar { get; set; } = new();
    public List<HesapKontrolKaydi> GecmisKayitlar { get; set; } = new();
    public List<HesapKontrolKaydi> TakipGecmisi { get; set; } = new();
    public TakipOzeti? TakipOzeti { get; set; }

    // ── UI State ──
    public string ActiveTab { get; set; } = "ozet";
    public BankaHesapTuru? FilterHesapTuru { get; set; }
    public KayitDurumu? FilterDurum { get; set; }
    public DateOnly FilterBaslangic { get; set; } = DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
    public DateOnly FilterBitis { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public string Arama { get; set; } = "";
    // P4.3: DB Snapshot bağları koparıldı
    // public DateOnly LastSnapshotDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Kullanıcının seçtiği analiz tarihi (geriye dönük kasa düzeltmeleri için)</summary>
    public DateOnly AnalizTarihi { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    // public List<DateOnly> SnapshotTarihleri { get; set; } = new();

    /// <summary>CrossDay kısmi eşleşmeler — onay bekleyen potansiyel eşleşmeler</summary>
    public List<CrossDayMatch> PotansiyelEslesmeler { get; set; } = new();
}
