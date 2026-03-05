#nullable enable
using KasaManager.Domain.Entities;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Karşılaştırma kısmi eşleşmeleri için kalıcı karar servisi.
/// </summary>
public interface IComparisonDecisionService
{
    /// <summary>Karar kaydet veya güncelle (upsert).</summary>
    Task<ComparisonDecision> SaveDecisionAsync(
        ComparisonType type,
        string dosyaNo,
        decimal miktar,
        string? birimAdi,
        decimal? bankaTutar,
        string? bankaAciklama,
        double confidence,
        string? matchReason,
        string decision,
        string? userName,
        CancellationToken ct = default);

    /// <summary>Belirli bir ComparisonType için tüm kararları getir.</summary>
    Task<List<ComparisonDecision>> GetDecisionsAsync(ComparisonType type, CancellationToken ct = default);

    /// <summary>Bir kararı sil (kayıt tekrar kısmi'ye döner).</summary>
    Task DeleteDecisionAsync(int decisionId, CancellationToken ct = default);

    /// <summary>
    /// Karşılaştırma sonuçlarına mevcut DB kararlarını uygular.
    /// Approved → Matched, Rejected → NotFound olarak yeniden sınıflandırır.
    /// Orijinal sonuç listesini değiştirir (in-place).
    /// </summary>
    int ApplyDecisions(List<ComparisonMatchResult> results, List<ComparisonDecision> decisions);
}
