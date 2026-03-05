namespace KasaManager.Domain.Reports;

public sealed class ImportedTable
{
    public required string SourceFileName { get; init; }
    public required ImportFileKind Kind { get; init; }

    // 🔴 ESKİ (kalsın ama kullanılmayacak)
    public List<string> Columns { get; init; } = new();

    // ✅ YENİ: Kolon metadatası
    public List<ImportedColumnMeta> ColumnMetas { get; init; } = new();

    public List<Dictionary<string, string?>> Rows { get; init; } = new();

    public int RowCount => Rows.Count;

    /// <summary>
    /// R1 uyumluluğu için: tuple deconstruction kullanan eski kodları kırmamak adına.
    /// Örn: var (columns, rows) = importedTable;
    /// </summary>
    public void Deconstruct(out List<string> columns, out List<Dictionary<string, string?>> rows)
    {
        columns = Columns;
        rows = Rows;
    }
}

public sealed class ImportedColumnMeta
{
    public required int Index { get; init; }
    public required string CanonicalName { get; init; }
    public required string OriginalHeader { get; init; }
}
