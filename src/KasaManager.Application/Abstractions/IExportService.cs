#nullable enable
using KasaManager.Domain.Reports.Export;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Çıktı Modülü: Strateji tabanlı çoklu format export servisi.
/// PDF (A4/A5/Landscape), Excel (.xlsx), CSV destekler.
/// 
/// Kullanım:
///   var result = await _export.ExportAsync(new ExportRequest { Data = data, Format = ..., Content = ... }, ct);
///   return File(result.FileBytes, result.ContentType, result.FileName);
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Verilen istek parametrelerine göre dosya üretir.
    /// Format ve Content kombinasyonuna uygun strateji seçilir.
    /// </summary>
    Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken ct);

    /// <summary>
    /// Desteklenen format-içerik kombinasyonları listesi (UI dropdown'ları için).
    /// </summary>
    IReadOnlyList<ExportOption> GetAvailableOptions();
}

/// <summary>
/// UI'da gösterilecek çıktı seçeneği.
/// </summary>
public sealed record ExportOption(
    ExportFormat Format,
    ExportContent Content,
    string DisplayName,
    string Icon
);
