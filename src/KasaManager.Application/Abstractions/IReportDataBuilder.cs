#nullable enable
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Çıktı Modülü: CalculationRun → KasaRaporData dönüştürücü.
/// 
/// KRİTİK PRENSİP: 
/// - Hiçbir UI verisini (Request.Form, hidden input) kullanmaz.
/// - Tek veri kaynağı: CalculationRun.Outputs + CalculationRun.Inputs + GlobalDefaults.
/// - Aynı CalculationRun ile üretilen veriler her zaman aynı sonucu verir (deterministik).
/// </summary>
public interface IReportDataBuilder
{
    /// <summary>
    /// CalculationRun + KasaÜstRapor (opsiyonel) → KasaRaporData.
    /// Backend'de hesaplanmış verileri PDF/Excel/CSV için hazırlar.
    /// </summary>
    Task<KasaRaporData> BuildAsync(
        CalculationRun run,
        string kasaTuru,
        ImportedTable? ustRaporTable,
        CancellationToken ct);

    /// <summary>
    /// Kayıtlı CalculatedKasaSnapshot'tan veri oluşturur.
    /// Geçmiş raporların yeniden basımı için kullanılır.
    /// </summary>
    Task<KasaRaporData> BuildFromSnapshotAsync(
        Guid snapshotId,
        CancellationToken ct);
}
