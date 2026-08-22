#nullable enable
namespace KasaManager.Application.Abstractions;

/// <summary>
/// Manual Resolve (HesapKontrol financial reversal) için WRITE-tarafı authoritative BusinessDate
/// çözümü. READ-tarafı <see cref="IEffectiveAnalysisDateResolver"/>'dan kasıtlı olarak ayrıdır:
/// bu resolver hiçbir zaman sistem saatini/DateTime.Now'ı veya HesapKontrol kayıtlarını okumaz —
/// yalnızca sunucu tarafında analiz edilebilir Excel BusinessDate'i persisted Genel Kasa tarihiyle
/// kıyaslayıp tip-güvenli, fail-closed bir sonuç döner.
/// </summary>
public interface IManualResolveWriteBusinessDateResolver
{
    Task<ManualResolveWriteBusinessDateResult> ResolveAsync(
        string uploadFolderAbsolute,
        CancellationToken ct = default);
}

public enum ManualResolveWriteBusinessDateFailureReason
{
    /// <summary>Excel'den hiçbir tarih üretilemedi.</summary>
    NoAnalyzableExcelDate = 0,
    /// <summary>Excel kaynakları arasında/içinde tarih çakışması var — kullanıcı kararı gerekir.</summary>
    ExcelDateConflict = 1,
    /// <summary>Excel tarihi persisted Genel Kasa tarihiyle aynı — o tarih zaten frozen.</summary>
    ExcelDateEqualsPersistedKasa = 2,
    /// <summary>Excel tarihi persisted Genel Kasa tarihinden eski — stale/backdating kaynağı.</summary>
    ExcelDateBeforePersistedKasa = 3
}

public sealed record ManualResolveWriteBusinessDateResult(
    bool Success,
    DateOnly? BusinessDate,
    ManualResolveWriteBusinessDateFailureReason? FailureReason,
    string Detail)
{
    public static ManualResolveWriteBusinessDateResult Ok(DateOnly date, string detail) =>
        new(true, date, null, detail);

    public static ManualResolveWriteBusinessDateResult FailClosed(
        ManualResolveWriteBusinessDateFailureReason reason, string detail) =>
        new(false, null, reason, detail);
}
