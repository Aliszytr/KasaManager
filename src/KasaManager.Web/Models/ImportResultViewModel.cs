using KasaManager.Domain.Reports;

namespace KasaManager.Web.Models;

public sealed class ImportResultViewModel
{
    public string SourceFileName { get; set; } = string.Empty;
    public ImportFileKind Kind { get; set; } = ImportFileKind.Unknown;

    // ✅ R2: Kolon metadatası
    public List<ImportedColumnMeta> ColumnMetas { get; set; } = new();

    // Satırlar: key = CanonicalName
    public List<Dictionary<string, string?>> Rows { get; set; } = new();

    // ✅ R3D: Önizleme için zenginleştirilmiş satırlar (ilk N satır)
    public List<NarrativePreviewRow> NarrativePreviewRows { get; set; } = new();

    public int RowCount => Rows?.Count ?? 0;
}

public sealed class NarrativePreviewRow
{
    public int RowNo { get; set; }

    public string? Tarih { get; set; }
    public string? Tutar { get; set; }

    public string? AciklamaHam { get; set; }
    public string? AciklamaSecilen { get; set; }
    public string? AciklamaNormalize { get; set; }

    public string? NormalizedCourtUnit { get; set; }
    public string? FileNo { get; set; }

    public int IssueCount { get; set; }
    public string Issues { get; set; } = string.Empty;
}
