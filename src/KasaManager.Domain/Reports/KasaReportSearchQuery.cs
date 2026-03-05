#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Kasa Raporu arama/filtreleme parametreleri.
/// </summary>
public sealed class KasaReportSearchQuery
{
    /// <summary>İsim, açıklama veya notlarda arama</summary>
    public string? SearchText { get; set; }
    
    /// <summary>Kasa türü filtresi (null = hepsi)</summary>
    public KasaRaporTuru? KasaTuru { get; set; }
    
    /// <summary>Başlangıç tarihi</summary>
    public DateOnly? StartDate { get; set; }
    
    /// <summary>Bitiş tarihi</summary>
    public DateOnly? EndDate { get; set; }
    
    /// <summary>Silinmiş kayıtları da dahil et</summary>
    public bool IncludeDeleted { get; set; } = false;
    
    /// <summary>Sıralama alanı (RaporTarihi, Name, KasaTuru, CalculatedAtUtc)</summary>
    public string SortBy { get; set; } = "RaporTarihi";
    
    /// <summary>Azalan sıralama</summary>
    public bool SortDescending { get; set; } = true;
    
    /// <summary>Sayfa numarası (1-based)</summary>
    public int Page { get; set; } = 1;
    
    /// <summary>Sayfa boyutu</summary>
    public int PageSize { get; set; } = 20;
}
