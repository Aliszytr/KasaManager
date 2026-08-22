#nullable enable
using KasaManager.Application.Abstractions;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// IEffectiveAnalysisDateResolver implementasyonu — Revision 3 Section 6/7.
/// GET-safe: yalnızca okuma yapar, hiçbir alanı DB'ye yazmaz.
/// </summary>
public sealed class EffectiveAnalysisDateResolver : IEffectiveAnalysisDateResolver
{
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly IKasaReportDateRulesService _dateRules;

    public EffectiveAnalysisDateResolver(IKasaRaporSnapshotService snapshots, IKasaReportDateRulesService dateRules)
    {
        _snapshots = snapshots;
        _dateRules = dateRules;
    }

    public async Task<EffectiveAnalysisDateResult> ResolveAsync(
        DateOnly? explicitContextDate,
        string kasaScope,
        string uploadFolderAbsolute,
        CancellationToken ct = default)
    {
        // Tier 0: explicit/context date — server-side validated (yalnızca sözdizimsel: default(DateOnly)
        // kabul edilmez). Yeni bir iş kuralı (ör. gelecek tarih reddi) burada İCAT EDİLMEZ.
        if (explicitContextDate.HasValue && explicitContextDate.Value != default)
        {
            return new EffectiveAnalysisDateResult(
                explicitContextDate.Value,
                AnalysisDateSourceTier.ExplicitContext,
                "Explicit context date (server-validated: non-default).");
        }

        // Tier 1: son "hesaplanmış" (Results dolu, ValuesJson'da Genel Kasa alanı olan) snapshot.
        // Üst sınır olarak "bugün"e kadar aranır — DÖNEN DEĞER DateTime.Now DEĞİL, gerçek persisted
        // RaporTarihi'dir; Now yalnızca sorgu penceresini sınırlamak için kullanılır.
        var searchUpperBound = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastCalculated = await _snapshots.GetLastGenelKasaSnapshotBeforeOrOnAsync(searchUpperBound, ct);
        if (lastCalculated != null)
        {
            return new EffectiveAnalysisDateResult(
                lastCalculated.RaporTarihi,
                AnalysisDateSourceTier.SuccessfulPersistedKasa,
                $"GetLastGenelKasaSnapshotBeforeOrOnAsync → {lastCalculated.RaporTarihi:yyyy-MM-dd}",
                PersistedSnapshot: lastCalculated);
        }

        // Tier 2: sunucu tarafında analiz edilebilir en güncel Excel tarihi (mevcut R7 motoru).
        var eval = await _dateRules.EvaluateAsync(uploadFolderAbsolute, ct);
        if (eval.ProposedDate.HasValue)
        {
            return new EffectiveAnalysisDateResult(
                eval.ProposedDate.Value,
                AnalysisDateSourceTier.AnalyzableExcel,
                "IKasaReportDateRulesService.EvaluateAsync → ProposedDate");
        }

        // Tier 3: hiçbir kaynak yok.
        return new EffectiveAnalysisDateResult(null, AnalysisDateSourceTier.NoAnalyzableSource, null);
    }
}
