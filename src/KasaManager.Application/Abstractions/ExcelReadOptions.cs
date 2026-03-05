namespace KasaManager.Application.Abstractions;

public sealed class ExcelReadOptions
{
    /// <summary>Header satırı otomatik bulunamazsa zorla.</summary>
    public int? HeaderRowIndex1Based { get; set; }

    /// <summary>Boş satırları atla.</summary>
    public bool SkipEmptyRows { get; set; } = true;

    /// <summary>Maks. satır (UI test için limit). null: limitsiz.</summary>
    public int? MaxRows { get; set; }

    /// <summary>Normalize edilmiş kolon adları (zorunlu). Boşsa: serbest.</summary>
    public List<string> RequiredColumns { get; set; } = new();

    /// <summary>Kolon adı eş anlamları: canonical -> alternatif başlıklar.</summary>
    public Dictionary<string, string[]> ColumnAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
