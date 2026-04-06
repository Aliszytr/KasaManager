namespace KasaManager.Domain.Settings;

/// <summary>
/// P1(C): Environment variable tabanlı feature flag yerine typed config.
/// USE_LIVE_USTRAPOR_SOURCE env-var davranışını IOptions pattern'e taşır.
/// Default: false (legacy snapshot-based davranış korunur).
/// </summary>
public sealed class UstRaporSourceOptions
{
    public const string SectionName = "UstRaporSource";

    /// <summary>
    /// true → Live provider (Excel re-read) kullanılır.
    /// false → Snapshot-based ustRapor summary kullanılır (mevcut production default).
    /// </summary>
    public bool UseLiveSource { get; set; } = false;
}
