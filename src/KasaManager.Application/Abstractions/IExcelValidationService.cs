using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// MS7: Excel dosya validasyonu — import öncesi erken hata tespiti.
/// </summary>
public interface IExcelValidationService
{
    /// <summary>
    /// Excel dosyasını boyut, tür, worksheet ve zorunlu kolon açısından doğrular.
    /// </summary>
    Result<ExcelValidationResult> Validate(string filePath, ImportFileKind kind);
}

/// <summary>
/// Validasyon sonucu.
/// </summary>
public sealed class ExcelValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Warnings { get; init; } = new();
    public int SheetCount { get; init; }
    public int RowCount { get; init; }
    public List<string> DetectedColumns { get; init; } = new();
}
