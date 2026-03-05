#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using QuestPDF.Fluent;
using System.Collections.Frozen;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// Karşılaştırma raporunu PDF'e dönüştüren servis.
/// Kullanıcı kararlarını uygulayarak olağan/olağandışı kategorilere ayırır.
/// </summary>
public sealed class ComparisonExportService : IComparisonExportService
{
    /// <summary>
    /// Olağan (beklenen) olarak sınıflandırılan fazla kayıt türleri.
    /// EFT, Otomatik İade, Gelen Havale, Virman: bankada karşılığı olması beklenen türler.
    /// </summary>
    private static readonly FrozenSet<string> OlaganTurler = new[]
    {
        "EFT", "Otomatik İade", "Gelen Havale", "Virman",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<byte[]> ExportToPdfAsync(
        ComparisonReport report,
        IReadOnlyDictionary<int, string>? decisions = null,
        CancellationToken ct = default)
    {
        var pdfData = BuildPdfData(report, decisions);
        var document = new Pdf.ComparisonRaporDocument(pdfData);
        var bytes = document.GeneratePdf();
        return Task.FromResult(bytes);
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal: Report → PdfData dönüşüm
    // ═══════════════════════════════════════════════════════════════

    internal static ComparisonPdfData BuildPdfData(
        ComparisonReport report,
        IReadOnlyDictionary<int, string>? decisions)
    {
        decisions ??= new Dictionary<int, string>();

        // ── Kullanıcı kararlarını uygula ─────────────────
        var (approvedCount, rejectedCount, rejectedResults) = ApplyDecisions(report.Results, decisions);

        // ── Fazla kayıtları olağan / olağandışı olarak ayır ─────
        var (olagan, olagandisi) = CategoriseSurplus(report.SurplusBankaRecords);

        return new ComparisonPdfData
        {
            // Başlık
            Type = report.Type,
            GeneratedAt = report.GeneratedAt,
            ReportDate = report.ReportDate,

            // İstatistikler
            TotalOnlineRecords = report.TotalOnlineRecords,
            TotalBankaAlacakRecords = report.TotalBankaAlacakRecords,
            MatchedCount = report.MatchedCount + approvedCount,
            PartialMatchCount = report.PartialMatchCount - approvedCount - rejectedCount,
            NotFoundCount = report.NotFoundCount + rejectedCount,
            TotalOnlineAmount = report.TotalOnlineAmount,
            TotalMatchedAmount = report.TotalMatchedAmount,
            UnmatchedAmount = report.UnmatchedAmount,
            ApprovedCount = approvedCount,
            RejectedCount = rejectedCount,

            // Fazla gelenler
            OlaganFazlaGelenler = olagan,
            OlagandisiFazlaGelenler = olagandisi,

            // Gelmeyenler
            Gelmeyenler = report.MissingBankaRecords,
            ReddedilenEslesmeler = rejectedResults,

            // Tutar dengesi
            SurplusAmount = report.SurplusAmount,
            MissingAmount = report.MissingAmount,
            NetAmountDifference = report.NetAmountDifference,
            BalanceSummary = report.BalanceSummary,

            // Reddiyat / Stopaj
            TotalGelirVergisi = report.TotalGelirVergisi,
            TotalDamgaVergisi = report.TotalDamgaVergisi,
            TotalStopaj = report.TotalStopaj,
            TotalOdenecekMiktar = report.TotalOdenecekMiktar,
            TotalNetOdenecek = report.TotalNetOdenecek,
            TotalBankaCikis = report.TotalBankaCikis,
            StopajStatus = report.StopajStatus,
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Kullanıcı kararlarını uygulama
    // ─────────────────────────────────────────────────────────────

    private static (int approved, int rejected, List<ComparisonMatchResult> rejectedResults)
        ApplyDecisions(List<ComparisonMatchResult> results, IReadOnlyDictionary<int, string> decisions)
    {
        int approved = 0, rejected = 0;
        var rejectedResults = new List<ComparisonMatchResult>();

        foreach (var (index, decision) in decisions)
        {
            if (index < 0 || index >= results.Count) continue;

            var result = results[index];
            if (result.Status != MatchStatus.PartialMatch) continue;

            switch (decision.ToLowerInvariant())
            {
                case "approved":
                    approved++;
                    break;
                case "rejected":
                    rejected++;
                    rejectedResults.Add(result);
                    break;
            }
        }

        return (approved, rejected, rejectedResults);
    }

    // ─────────────────────────────────────────────────────────────
    // Fazla kayıtları kategorilere ayırma
    // ─────────────────────────────────────────────────────────────

    private static (List<UnmatchedBankaRecord> olagan, List<UnmatchedBankaRecord> olagandisi)
        CategoriseSurplus(List<UnmatchedBankaRecord> records)
    {
        var olagan = new List<UnmatchedBankaRecord>();
        var olagandisi = new List<UnmatchedBankaRecord>();

        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.DetectedType) && OlaganTurler.Contains(r.DetectedType))
                olagan.Add(r);
            else
                olagandisi.Add(r);
        }

        return (olagan, olagandisi);
    }
}
