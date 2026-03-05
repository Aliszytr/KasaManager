#nullable enable
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Web.Models;

/// <summary>
/// Kasa Raporları CRUD liste sayfası ViewModel.
/// </summary>
public sealed class KasaRaporlarViewModel
{
    // Sonuçlar
    public PagedResult<CalculatedKasaSnapshot> Results { get; set; } = new();
    
    // İstatistikler
    public int ToplamRapor { get; set; }
    public int SabahCount { get; set; }
    public int AksamCount { get; set; }
    public int GenelCount { get; set; }
    
    // Aktif filtre değerleri (form state)
    public string? SearchText { get; set; }
    public KasaRaporTuru? KasaTuru { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IncludeDeleted { get; set; }
    public string SortBy { get; set; } = "RaporTarihi";
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Kasa Raporları detay sayfası ViewModel.
/// </summary>
public sealed class KasaRaporDetayViewModel
{
    public CalculatedKasaSnapshot Snapshot { get; set; } = null!;
    
    // Re-hydrated veriler
    public Dictionary<string, decimal> Inputs { get; set; } = new();
    public Dictionary<string, decimal> Outputs { get; set; } = new();
    
    // KasaRaporData (varsa)
    public KasaRaporData? RaporData { get; set; }
}
