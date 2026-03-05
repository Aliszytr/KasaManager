#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Entities;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Kalıcı karşılaştırma kararları servisi.
/// </summary>
public sealed class ComparisonDecisionService : IComparisonDecisionService
{
    private readonly KasaManagerDbContext _db;

    public ComparisonDecisionService(KasaManagerDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ComparisonDecision> SaveDecisionAsync(
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
        CancellationToken ct)
    {
        // Upsert: aynı kayıt varsa güncelle, yoksa ekle
        var existing = await _db.ComparisonDecisions
            .FirstOrDefaultAsync(d =>
                d.ComparisonType == type &&
                d.OnlineDosyaNo == dosyaNo &&
                d.OnlineMiktar == miktar &&
                d.OnlineBirimAdi == birimAdi, ct);

        if (existing is not null)
        {
            existing.Decision = decision;
            existing.DecidedAtUtc = DateTime.UtcNow;
            existing.DecidedBy = userName;
            existing.BankaTutar = bankaTutar;
            existing.BankaAciklamaSummary = bankaAciklama?.Length > 500
                ? bankaAciklama[..500]
                : bankaAciklama;
        }
        else
        {
            existing = new ComparisonDecision
            {
                ComparisonType = type,
                OnlineDosyaNo = dosyaNo,
                OnlineMiktar = miktar,
                OnlineBirimAdi = birimAdi,
                BankaTutar = bankaTutar,
                BankaAciklamaSummary = bankaAciklama?.Length > 500
                    ? bankaAciklama[..500]
                    : bankaAciklama,
                Decision = decision,
                DecidedAtUtc = DateTime.UtcNow,
                DecidedBy = userName,
                OriginalConfidence = confidence,
                OriginalMatchReason = matchReason?.Length > 500
                    ? matchReason[..500]
                    : matchReason
            };
            _db.ComparisonDecisions.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task<List<ComparisonDecision>> GetDecisionsAsync(
        ComparisonType type, CancellationToken ct)
    {
        return await _db.ComparisonDecisions
            .Where(d => d.ComparisonType == type)
            .OrderByDescending(d => d.DecidedAtUtc)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteDecisionAsync(int decisionId, CancellationToken ct)
    {
        var decision = await _db.ComparisonDecisions.FindAsync(new object[] { decisionId }, ct);
        if (decision is not null)
        {
            _db.ComparisonDecisions.Remove(decision);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public int ApplyDecisions(List<ComparisonMatchResult> results, List<ComparisonDecision> decisions)
    {
        if (decisions.Count == 0) return 0;

        int applied = 0;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.Status != MatchStatus.PartialMatch) continue;

            // Tüm decisions aynı ComparisonType'a ait (GetDecisionsAsync zaten filtreler)
            var match = decisions.FirstOrDefault(d =>
                d.OnlineDosyaNo == (r.OnlineDosyaNo ?? "") &&
                d.OnlineMiktar == r.OnlineMiktar &&
                (d.OnlineBirimAdi ?? "") == (r.OnlineBirimAdi ?? ""));

            if (match is null) continue;

            // In-place değiştirmek için yeni instance oluştur (init-only properties)
            var newStatus = match.Decision == "approved" ? MatchStatus.Matched : MatchStatus.NotFound;
            var suffix = match.Decision == "approved" ? " ✅ Kullanıcı onayı" : " ❌ Kullanıcı reddi";
            var newReason = (r.MatchReason ?? "") + suffix;

            results[i] = new ComparisonMatchResult
            {
                OnlineRowIndex = r.OnlineRowIndex,
                OnlineDosyaNo = r.OnlineDosyaNo,
                OnlineBirimAdi = r.OnlineBirimAdi,
                OnlineMiktar = r.OnlineMiktar,
                OnlineTarih = r.OnlineTarih,
                BankaRowIndex = r.BankaRowIndex,
                BankaAciklama = r.BankaAciklama,
                BankaTutar = r.BankaTutar,
                BankaTarih = r.BankaTarih,
                BankaBorcAlacak = r.BankaBorcAlacak,
                ParsedIl = r.ParsedIl,
                ParsedMahkeme = r.ParsedMahkeme,
                ParsedEsasNo = r.ParsedEsasNo,
                ParsedKeyword = r.ParsedKeyword,
                Status = newStatus,
                ConfidenceScore = r.ConfidenceScore,
                MatchReason = newReason
            };
            applied++;
        }

        return applied;
    }
}
