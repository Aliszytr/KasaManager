#nullable enable
namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// R17: Kullanıcının Field Chooser'da seçtiği alanların kaydı.
/// Kasa türüne göre farklı seçimler yapılabilir.
/// Kullanıcı bazlı veya global (UserName=null) olabilir.
/// </summary>
public sealed class UserFieldPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Hangi kasa türü için (Aksam, Sabah, Genel, Ortak)</summary>
    public string KasaType { get; set; } = string.Empty;
    
    /// <summary>Kullanıcı adı (null = global/tüm kullanıcılar için varsayılan)</summary>
    public string? UserName { get; set; }
    
    /// <summary>Seçilen alan key'leri (JSON array)</summary>
    public string SelectedFieldsJson { get; set; } = "[]";
    
    /// <summary>Oluşturulma zamanı</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>Güncellenme zamanı</summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    // ══════════════════════════════════════════════════════
    // Helper Methods
    // ══════════════════════════════════════════════════════
    
    /// <summary>Seçilen alanları liste olarak getir</summary>
    public List<string> GetSelectedFields()
    {
        if (string.IsNullOrWhiteSpace(SelectedFieldsJson) || SelectedFieldsJson == "[]")
            return new List<string>();
            
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(SelectedFieldsJson) 
                ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
    
    /// <summary>Seçilen alanları JSON olarak kaydet</summary>
    public void SetSelectedFields(IEnumerable<string> fields)
    {
        SelectedFieldsJson = System.Text.Json.JsonSerializer.Serialize(fields.ToList());
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
