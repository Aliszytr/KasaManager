#nullable enable
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// KASAMANAGER-2026-08-21-FINAL-PLAN-CLOSURE Revision 3, Section 6 (Helpy override — kesin sıra):
/// 0. Explicit/context BusinessDate (server-side validated)
/// 1. Son "hesaplanmış" (successful) persisted Kasa BusinessDate
/// 2. Sunucu tarafında analiz edilebilir en güncel Excel BusinessDate
/// 3. NoAnalyzableSource
/// Finansal business-date fallback DateTime.Today/Now DÖNMEZ — yalnızca sorgu üst sınırı olarak
/// (ör. "bugüne kadarki son snapshot") kullanılabilir, hiçbir zaman dönen değerin kendisi olamaz.
/// </summary>
public enum AnalysisDateSourceTier
{
    ExplicitContext = 0,
    SuccessfulPersistedKasa = 1,
    AnalyzableExcel = 2,
    NoAnalyzableSource = 3
}

/// <summary>
/// <paramref name="PersistedSnapshot"/>: yalnızca <see cref="AnalysisDateSourceTier.SuccessfulPersistedKasa"/>
/// tier'ında dolu — Kasa freshness closure: caller'ın (KasaPreviewController) ikinci bir DB round-trip
/// yapmadan bu snapshot'ın SourceEvidenceJson'ını okuyup Current/Stale/Unknown tazelik kontrolü
/// yapabilmesi için. Resolver TIER'I KENDİSİ freshness değildir — bkz. KasaSourceFingerprintHelper.
/// </summary>
public sealed record EffectiveAnalysisDateResult(
    DateOnly? Date,
    AnalysisDateSourceTier Tier,
    string? SourceDetail,
    KasaRaporSnapshot? PersistedSnapshot = null);

public interface IEffectiveAnalysisDateResolver
{
    Task<EffectiveAnalysisDateResult> ResolveAsync(
        DateOnly? explicitContextDate,
        string kasaScope,
        string uploadFolderAbsolute,
        CancellationToken ct = default);
}
