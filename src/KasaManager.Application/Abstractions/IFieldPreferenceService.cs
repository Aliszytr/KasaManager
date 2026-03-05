#nullable enable
using KasaManager.Domain.FormulaEngine;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R17: Field Chooser tercihleri yönetimi.
/// Kullanıcının hangi alanları görmek istediğini saklar/getirir.
/// </summary>
public interface IFieldPreferenceService
{
    /// <summary>
    /// Kullanıcının seçtiği alanları getir.
    /// Önce kullanıcıya özel tercihi arar, bulamazsa global tercihi döner.
    /// O da yoksa FieldCatalog'dan varsayılanları döner.
    /// </summary>
    Task<List<string>> GetSelectedFieldsAsync(string kasaType, string? userName, CancellationToken ct = default);
    
    /// <summary>Seçimleri kaydet (kullanıcıya özel veya global)</summary>
    Task SaveSelectedFieldsAsync(string kasaType, string? userName, List<string> selectedFields, CancellationToken ct = default);
    
    /// <summary>Varsayılana dön (kullanıcı tercihini sil)</summary>
    Task ResetToDefaultsAsync(string kasaType, string? userName, CancellationToken ct = default);
    
    /// <summary>Tüm kategorileri ve alanları UI için gruplanmış getir</summary>
    List<FieldCategoryGroup> GetFieldCatalogGrouped();
    
    /// <summary>R17B: Kaynak bazlı gruplanmış alanları getir (Excel, Kullanıcı Girişi, Hesaplanan)</summary>
    List<FieldSourceGroup> GetFieldCatalogGroupedBySource();
    
    /// <summary>Belirli kasa türü için varsayılan alanları getir</summary>
    List<string> GetDefaultFieldsFor(string kasaType);
}

/// <summary>R17: Kategori bazlı gruplanmış alanlar</summary>
public sealed class FieldCategoryGroup
{
    public string Category { get; init; } = string.Empty;
    public List<FieldCatalogEntry> Fields { get; init; } = new();
    
    /// <summary>Kategorideki alan sayısı</summary>
    public int Count => Fields.Count;
    
    /// <summary>Kategorideki seçili alan sayısı (UI için)</summary>
    public int SelectedCount { get; set; }
}

/// <summary>R17B: Kaynak bazlı gruplanmış alanlar (Excel, Kullanıcı Girişi, Hesaplanan)</summary>
public sealed class FieldSourceGroup
{
    public FieldSource Source { get; init; }
    
    /// <summary>UI'da gösterilecek grup adı</summary>
    public string DisplayName => Source switch
    {
        FieldSource.Excel => "📊 Excel Ham Veri",
        FieldSource.UserInput => "✏️ Kullanıcı Girişleri",
        FieldSource.Calculated => "🧮 Hesaplanan Alanlar",
        _ => Source.ToString()
    };
    
    /// <summary>Grubun ikonları</summary>
    public string Icon => Source switch
    {
        FieldSource.Excel => "bi-file-earmark-excel",
        FieldSource.UserInput => "bi-pencil-square",
        FieldSource.Calculated => "bi-calculator",
        _ => "bi-question-circle"
    };
    
    /// <summary>Grubun renk sınıfı</summary>
    public string ColorClass => Source switch
    {
        FieldSource.Excel => "text-success",
        FieldSource.UserInput => "text-primary",
        FieldSource.Calculated => "text-secondary",
        _ => ""
    };
    
    public List<FieldCatalogEntry> Fields { get; init; } = new();
    public int Count => Fields.Count;
}
