#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FormulaEngine;

namespace KasaManager.Web.Models;

/// <summary>
/// R17B: Field Chooser panel için ViewModel.
/// Kaynak bazlı (Excel, Kullanıcı Girişi, Hesaplanan) ve kategori bazlı gruplanmış alanları tutar.
/// </summary>
public sealed class FieldChooserViewModel
{
    /// <summary>Kategori bazlı gruplanmış alanlar (eski yapı, geriye uyumluluk)</summary>
    public List<FieldCategoryGroup> Categories { get; init; } = new();
    
    /// <summary>R17B: Kaynak bazlı gruplanmış alanlar (yeni yapı)</summary>
    public List<FieldSourceGroup> SourceGroups { get; init; } = new();
    
    /// <summary>Seçili alan key'leri</summary>
    public HashSet<string> SelectedFields { get; init; } = new();
    
    /// <summary>Hangi kasa türü için (Aksam, Sabah, Genel, Ortak)</summary>
    public string KasaType { get; init; } = "Aksam";
    
    /// <summary>R18: Yüklenen şablon adı (DB'den yüklenmiş FormulaSet adı, yoksa null)</summary>
    public string? LoadedTemplateName { get; init; }
    
    /// <summary>Belirti bir alanın seçili olup olmadığını kontrol et</summary>
    public bool IsSelected(string key) => SelectedFields.Contains(key);
    
    /// <summary>Toplam seçili alan sayısı</summary>
    public int SelectedCount => SelectedFields.Count;
    
    /// <summary>Toplam alan sayısı</summary>
    public int TotalCount => SourceGroups.Sum(g => g.Fields.Count);
    
    /// <summary>R17B: Pool verilerinin yüklenip yüklenmediğini gösterir</summary>
    public bool HasPoolData { get; init; } = false;
    
    /// <summary>Factory method - service'ten veri alarak oluşturur</summary>
    public static async Task<FieldChooserViewModel> CreateAsync(
        IFieldPreferenceService service,
        string kasaType,
        string? userName,
        CancellationToken ct = default)
    {
        var sourceGroups = service.GetFieldCatalogGroupedBySource();
        var selectedFields = await service.GetSelectedFieldsAsync(kasaType, userName, ct);
        
        return new FieldChooserViewModel
        {
            SourceGroups = sourceGroups,
            Categories = service.GetFieldCatalogGrouped(), // geriye uyumluluk
            SelectedFields = new HashSet<string>(selectedFields, StringComparer.OrdinalIgnoreCase),
            KasaType = kasaType
        };
    }
    
    /// <summary>R17B: Sol menüden gelen seçili alanlarla oluşturur</summary>
    public static Task<FieldChooserViewModel> CreateWithSelectedAsync(
        IFieldPreferenceService service,
        string kasaType,
        HashSet<string> currentlySelected,
        CancellationToken ct = default)
    {
        var sourceGroups = service.GetFieldCatalogGroupedBySource();
        
        return Task.FromResult(new FieldChooserViewModel
        {
            SourceGroups = sourceGroups,
            Categories = service.GetFieldCatalogGrouped(), // geriye uyumluluk
            SelectedFields = currentlySelected,
            KasaType = kasaType
        });
    }
    
    /// <summary>R18: Sol menüden gelen seçili alanlarla oluşturur - Catalog'dan TÜM alanları gösterir</summary>
    /// <remarks>
    /// Pool verisi artık kullanılmıyor. Alan Seçici her zaman FieldCatalog'dan
    /// tüm alanları kaynak bazlı (Excel, UserInput, Calculated) olarak gösterir.
    /// Kullanıcı istediği alanı seçerek formüllerde kullanabilir.
    /// </remarks>
    public static Task<FieldChooserViewModel> CreateWithSelectedAndPoolAsync(
        IFieldPreferenceService service,
        string kasaType,
        HashSet<string> currentlySelected,
        HashSet<string> poolKeys, // Artık kullanılmıyor, geriye uyumluluk için parametrede kaldı
        string? loadedTemplateName = null, // R18: Yüklenen şablon adı
        CancellationToken ct = default)
    {
        // R18: Her zaman Catalog'dan tüm alanları göster (kaynak bazlı gruplama ile)
        var sourceGroups = service.GetFieldCatalogGroupedBySource();
        
        return Task.FromResult(new FieldChooserViewModel
        {
            SourceGroups = sourceGroups,
            Categories = service.GetFieldCatalogGrouped(), // geriye uyumluluk
            SelectedFields = currentlySelected,
            KasaType = kasaType,
            LoadedTemplateName = loadedTemplateName, // R18
            HasPoolData = false // Artık pool kullanılmıyor
        });
    }
    
    /// <summary>Pool key'ini okunabilir bir display name'e çevir</summary>
    private static string FormatPoolKeyDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        
        // Alt çizgileri boşluğa çevir ve ilk harfleri büyük yap
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => 
            char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : "")));
    }
    
    /// <summary>Pool key'ine göre kategori belirle</summary>
    private static string DeterminePoolKeyCategory(string key)
    {
        var k = key.ToLowerInvariant();
        
        if (k.Contains("tahsilat")) return "Tahsilat";
        if (k.Contains("harc") || k.Contains("harç")) return "Harç";
        if (k.Contains("reddiyat")) return "Reddiyat";
        if (k.Contains("stopaj")) return "Stopaj";
        if (k.Contains("banka")) return "Banka";
        if (k.Contains("kasa")) return "Kasa";
        if (k.Contains("vergi")) return "Vergi";
        if (k.Contains("eksik") || k.Contains("fazla")) return "Eksik/Fazla";
        if (k.Contains("masraf")) return "Masraf";
        
        return "Diğer";
    }
}

