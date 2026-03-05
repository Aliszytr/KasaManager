#nullable enable
using KasaManager.Domain.Reports;
using KasaManager.Domain.Validation;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Hesaplama sonrası kural tabanlı doğrulama servisi.
/// Tutarsızlıkları, anomalileri ve eksik verileri tespit eder.
/// </summary>
public interface IKasaValidationService
{
    /// <summary>
    /// KasaRaporData üzerinde tüm kuralları çalıştırır.
    /// </summary>
    List<KasaValidationResult> Validate(KasaRaporData data);

    /// <summary>
    /// Uyarıyı "Çözüldü / Tamamlandı" olarak işaretle.
    /// </summary>
    Task DismissAsync(DateOnly raporTarihi, string kasaTuru, string ruleCode, string? note = null, string? user = null, CancellationToken ct = default);

    /// <summary>
    /// Belirli gün ve kasa tipi için dismiss edilen kural kodlarını getir.
    /// </summary>
    Task<HashSet<string>> GetDismissedCodesAsync(DateOnly raporTarihi, string kasaTuru, CancellationToken ct = default);
}
