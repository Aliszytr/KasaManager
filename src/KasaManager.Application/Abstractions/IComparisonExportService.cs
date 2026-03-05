#nullable enable
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Karşılaştırma sonuçları PDF çıktı servisi.
/// </summary>
public interface IComparisonExportService
{
    /// <summary>
    /// Karşılaştırma raporunu, kullanıcı kararlarını dikkate alarak PDF'e dönüştürür.
    /// </summary>
    /// <param name="report">Orijinal karşılaştırma raporu.</param>
    /// <param name="decisions">
    /// Kullanıcı kararları. Key = Results dizisindeki index,
    /// Value = "approved" (eşleşenlere dahil) veya "rejected" (eşleşmeyenlere dahil).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PDF dosya byte dizisi.</returns>
    Task<byte[]> ExportToPdfAsync(
        ComparisonReport report,
        IReadOnlyDictionary<int, string>? decisions = null,
        CancellationToken ct = default);
}
