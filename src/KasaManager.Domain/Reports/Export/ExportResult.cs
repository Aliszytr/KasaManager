namespace KasaManager.Domain.Reports.Export;

/// <summary>
/// Çıktı Modülü: üretilen dosyanın byte[], MIME type ve dosya adı.
/// </summary>
public sealed class ExportResult
{
    public required byte[] FileBytes { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }

    // ── Yaygın MIME tipleri ──
    public const string MimePdf = "application/pdf";
    public const string MimeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string MimeCsv = "text/csv; charset=utf-8";
}
