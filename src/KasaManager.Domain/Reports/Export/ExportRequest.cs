namespace KasaManager.Domain.Reports.Export;

/// <summary>
/// Çıktı Modülü: tek isteğe birden fazla format ve içerik türü verilebilir.
/// Controller → IExportService.ExportAsync(request) ile kullanılır.
/// </summary>
public sealed class ExportRequest
{
    /// <summary>Rapor verileri — IReportDataBuilder tarafından üretilir.</summary>
    public required KasaRaporData Data { get; init; }

    /// <summary>İstenen çıktı formatı.</summary>
    public ExportFormat Format { get; init; } = ExportFormat.Pdf_A4_Portrait;

    /// <summary>İstenen içerik türü.</summary>
    public ExportContent Content { get; init; } = ExportContent.GenelRapor;

    /// <summary>Banka yazıları için şablon. Null ise varsayılan şablon kullanılır.</summary>
    public DocumentTemplate? Template { get; init; }
}
