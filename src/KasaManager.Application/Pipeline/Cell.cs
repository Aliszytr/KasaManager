#nullable enable
namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 1: Veri hücresi - Excel hücresi benzeri veri birimi.
/// Tüm veriler (Excel, UserInput, Settings, Derived) bu yapıda tutulur.
/// </summary>
public sealed record Cell
{
    /// <summary>Canonical key (snake_case, küçük harf).</summary>
    public required string Key { get; init; }
    
    /// <summary>Hücre değeri.</summary>
    public required decimal Value { get; init; }
    
    /// <summary>Veri kaynağı tipi.</summary>
    public required CellSource Source { get; init; }
    
    /// <summary>Sol menüde seçili mi?</summary>
    public bool IsSelected { get; set; }
    
    /// <summary>Bu hücreyi kullanan formül ID'leri.</summary>
    public HashSet<Guid> UsedByFormulas { get; } = new();
    
    // === Metadata ===
    
    /// <summary>Görüntüleme adı (UI için).</summary>
    public string? DisplayName { get; init; }
    
    /// <summary>Kategori (Tahsilat, Harç, Banka vb.).</summary>
    public string? Category { get; init; }
    
    /// <summary>Kaynak dosya adı.</summary>
    public string? SourceFile { get; init; }
    
    /// <summary>Ek açıklama/notlar.</summary>
    public string? Notes { get; init; }
    
    /// <summary>Formatlı değer (UI için).</summary>
    public string FormattedValue => Value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Veri kaynağı tipi.
/// </summary>
public enum CellSource
{
    /// <summary>Excel dosyasından okunan ham veri.</summary>
    Excel = 0,
    
    /// <summary>Kullanıcı girişi (UI'dan).</summary>
    UserInput = 1,
    
    /// <summary>Ayarlar/Defaults (veritabanından).</summary>
    Settings = 2,
    
    /// <summary>Önceki günden taşınan değer.</summary>
    Carryover = 3,
    
    /// <summary>Formül sonucu (hesaplanmış).</summary>
    Derived = 4,
    
    /// <summary>Field Catalog'dan eklenen.</summary>
    Catalog = 5
}
