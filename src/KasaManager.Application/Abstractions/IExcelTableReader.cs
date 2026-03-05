using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

public interface IExcelTableReader
{
    /// <summary>
    /// Excel'den tablo okur.
    /// Header satırını otomatik bulur.
    /// R2: ColumnMeta + Rows içeren ImportedTable döndürür.
    /// </summary>
    Result<ImportedTable> ReadTable(
        string filePath,
        ExcelReadOptions options);
}
