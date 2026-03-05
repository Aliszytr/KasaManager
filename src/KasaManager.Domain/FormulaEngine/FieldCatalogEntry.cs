#nullable enable
namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// R17: Bir alanın metadata tanımı.
/// Field Chooser panelinde bu bilgiler kullanılır.
/// Yeni alan eklemek için sadece FieldCatalog'a entry eklenir - kod değişikliği gerekmez.
/// </summary>
public sealed class FieldCatalogEntry
{
    /// <summary>Canonical key (örn: dunden_eksik_fazla_tahsilat)</summary>
    public string Key { get; init; } = string.Empty;
    
    /// <summary>UI'da görünecek isim (örn: "Dünden Eksik/Fazla Tahsilat")</summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>Kısa açıklama/tooltip</summary>
    public string? Description { get; init; }
    
    /// <summary>Kategori (Tahsilat, Reddiyat, Banka, Kasa, Stopaj, vb.)</summary>
    public string Category { get; init; } = "Genel";
    
    /// <summary>Varsayılan olarak hangi kasa türlerinde görünür (boş = hiçbirinde varsayılan değil)</summary>
    public List<string> DefaultVisibleIn { get; init; } = new() { "Aksam", "Sabah", "Genel" };
    
    /// <summary>UI sıralaması (düşük=üstte)</summary>
    public int SortOrder { get; init; } = 100;
    
    /// <summary>Gizli mi? (Sadece admin görebilir)</summary>
    public bool IsHidden { get; init; } = false;
    
    /// <summary>Read-only mu? (Hesaplanmış alan, düzenlenemez)</summary>
    public bool IsReadOnly { get; init; } = false;
    
    /// <summary>Veri tipi (decimal, string, date, bool)</summary>
    public string DataType { get; init; } = "decimal";
    
    /// <summary>Format string (örn: "N2" veya "#,##0.00 ₺")</summary>
    public string? Format { get; init; }
    
    /// <summary>İkon (Bootstrap Icons, örn: "bi-cash-coin")</summary>
    public string? Icon { get; init; }
    
    /// <summary>Renk grubu (CSS class, örn: "text-success")</summary>
    public string? ColorClass { get; init; }
    
    /// <summary>Bu alanın bağlı olduğu model property adı (reflection için)</summary>
    public string? ModelPropertyName { get; init; }
    
    /// <summary>Ek notlar/kullanım bilgisi</summary>
    public string? Notes { get; init; }
    
    /// <summary>R17B: Alanın veri kaynağı (Excel, Kullanıcı Girişi, Hesaplanan)</summary>
    public FieldSource Source { get; init; } = FieldSource.Excel;
}
