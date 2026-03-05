#nullable enable
using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Comparison;

/// <summary>
/// Banka ve Online dosyalar arasında karşılaştırma yapan servis.
/// Partial class: Matching, Reddiyat ve Helpers ayrı dosyalardadır.
/// </summary>
public sealed partial class ComparisonService : IComparisonService
{
    private readonly IImportOrchestrator _import;
    private readonly BankaAciklamaParser _parser;

    // Tutar toleransı: ±%0.5 (banka masrafları/yuvarlamalar için)
    private const decimal AmountTolerancePercent = 0.005m;
    
    // Tarih toleransı: ±2 gün
    private const int DateToleranceDays = 2;
    
    // Güven eşikleri
    private const double ConfidenceThresholdFull = 0.8;
    private const double ConfidenceThresholdPartial = 0.5;

    public ComparisonService(IImportOrchestrator import, BankaAciklamaParser parser)
    {
        _import = import;
        _parser = parser;
    }

    /// <inheritdoc />
    public Task<Result<ComparisonReport>> CompareTahsilatMasrafAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(CompareInternal(
            uploadFolder,
            "BankaTahsilat.xlsx",
            "onlineMasraf.xlsx",
            ComparisonType.TahsilatMasraf,
            filterDate));
    }

    /// <inheritdoc />
    public Task<Result<ComparisonReport>> CompareHarcamaHarcAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(CompareInternal(
            uploadFolder,
            "BankaHarc.xlsx",
            "onlineHarc.xlsx",
            ComparisonType.HarcamaHarc,
            filterDate));
    }

    /// <inheritdoc />
    public Task<Result<ComparisonReport>> CompareReddiyatCikisAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(CompareReddiyatInternal(
            uploadFolder,
            "BankaTahsilat.xlsx",
            "OnlineReddiyat.xlsx",
            filterDate));
    }

    /// <summary>
    /// Ana karşılaştırma mantığı.
    /// </summary>
    private Result<ComparisonReport> CompareInternal(
        string uploadFolder,
        string bankaFileName,
        string onlineFileName,
        ComparisonType type,
        DateOnly? filterDate)
    {
        var issues = new List<string>();
        var results = new List<ComparisonMatchResult>();

        try
        {
            // 1. Dosyaları yükle
            var bankaPath = ResolveFile(uploadFolder, bankaFileName);
            var onlinePath = ResolveFile(uploadFolder, onlineFileName);

            if (bankaPath == null)
                return Result<ComparisonReport>.Fail($"{bankaFileName} bulunamadı.");
            if (onlinePath == null)
                return Result<ComparisonReport>.Fail($"{onlineFileName} bulunamadı.");

            // 2. Excel dosyalarını oku
            var bankaKind = type == ComparisonType.TahsilatMasraf 
                ? ImportFileKind.BankaTahsilat 
                : ImportFileKind.BankaHarcama;
            var onlineKind = type == ComparisonType.TahsilatMasraf 
                ? ImportFileKind.OnlineMasraf 
                : ImportFileKind.OnlineHarcama;

            var bankaResult = _import.ImportTrueSource(bankaPath, bankaKind);
            if (!bankaResult.Ok || bankaResult.Value == null)
                return Result<ComparisonReport>.Fail($"{bankaFileName} okunamadı: {bankaResult.Error}");

            var onlineResult = _import.ImportTrueSource(onlinePath, onlineKind);
            if (!onlineResult.Ok || onlineResult.Value == null)
                return Result<ComparisonReport>.Fail($"{onlineFileName} okunamadı: {onlineResult.Error}");

            var bankaTable = bankaResult.Value;
            var onlineTable = onlineResult.Value;

            // 3. Kolon isimlerini bul
            var bankaDateCol = CanonicalKeyHelper.FindDateCanonical(bankaTable) ?? "islem_tarihi";
            var bankaTutarCol = CanonicalKeyHelper.FindCanonical(bankaTable, "islem_tutari") ?? "islem_tutari";
            var bankaAciklamaCol = CanonicalKeyHelper.FindCanonical(bankaTable, "aciklama") ?? "aciklama";
            var bankaBorcAlacakCol = CanonicalKeyHelper.FindCanonical(bankaTable, "borc_alacak")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "borç")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "borc")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "alacak");

            var onlineDateCol = CanonicalKeyHelper.FindDateCanonical(onlineTable) ?? "tarih";
            var onlineMiktarCol = CanonicalKeyHelper.FindCanonical(onlineTable, "miktar") ?? "miktar";
            var onlineDosyaNoCol = CanonicalKeyHelper.FindCanonical(onlineTable, "dosya_no") ?? "dosya_no";
            var onlineBirimCol = CanonicalKeyHelper.FindCanonical(onlineTable, "birim_adi") ?? "birim_adi";

            // 4. Banka kayıtlarını filtrele (sadece Alacak/+ olanlar)
            var bankaRecords = new List<BankaRecord>();
            int totalBankaRecords = 0;
            
            for (int i = 0; i < bankaTable.Rows.Count; i++)
            {
                var row = bankaTable.Rows[i];
                if (row == null) continue;
                totalBankaRecords++;

                if (filterDate.HasValue && !DateParsingHelper.RowMatchesDate(row, bankaDateCol, filterDate.Value))
                    continue;

                if (!row.TryGetValue(bankaTutarCol, out var tutarRaw) || 
                    !DecimalParsingHelper.TryParseDecimal(tutarRaw, out var tutar))
                    continue;

                var borcAlacak = bankaBorcAlacakCol != null && row.TryGetValue(bankaBorcAlacakCol, out var baRaw) 
                    ? baRaw : null;
                if (!IsAlacak(tutar, borcAlacak)) continue;

                var aciklama = row.TryGetValue(bankaAciklamaCol, out var acRaw) ? acRaw : null;
                if (string.IsNullOrWhiteSpace(aciklama)) continue;

                DateTime? tarih = null;
                if (row.TryGetValue(bankaDateCol, out var dateRaw) && DateParsingHelper.TryParseDateTime(dateRaw, out var d))
                    tarih = d;

                var parsed = _parser.Parse(aciklama);
                bankaRecords.Add(new BankaRecord(i, Math.Abs(tutar), tarih, aciklama, borcAlacak, parsed));
            }

            // 5. Online kayıtları oku
            var onlineRecords = new List<OnlineRecord>();
            for (int i = 0; i < onlineTable.Rows.Count; i++)
            {
                var row = onlineTable.Rows[i];
                if (row == null) continue;

                if (filterDate.HasValue && !DateParsingHelper.RowMatchesDate(row, onlineDateCol, filterDate.Value))
                    continue;

                if (!row.TryGetValue(onlineMiktarCol, out var miktarRaw) ||
                    !DecimalParsingHelper.TryParseDecimal(miktarRaw, out var miktar))
                    continue;

                var dosyaNo = row.TryGetValue(onlineDosyaNoCol, out var dnRaw) ? dnRaw : null;
                var birimAdi = row.TryGetValue(onlineBirimCol, out var baRaw2) ? baRaw2 : null;

                DateTime? tarih = null;
                if (row.TryGetValue(onlineDateCol, out var dateRaw) && DateParsingHelper.TryParseDateTime(dateRaw, out var d))
                    tarih = d;

                onlineRecords.Add(new OnlineRecord(i, NormalizeDosyaNo(dosyaNo), birimAdi, miktar, tarih));
            }

            // 6. Global Optimal Eşleştirme
            var usedBankaIndices = new HashSet<int>();
            var usedOnlineIndices = new HashSet<int>();

            // 6a. Tüm online-banka çiftleri için aday listesi oluştur
            var allCandidates = new List<(int OnlineIdx, OnlineRecord Online, ComparisonMatchResult Result)>();
            for (int oi = 0; oi < onlineRecords.Count; oi++)
            {
                var online = onlineRecords[oi];
                var matchResult = FindBestMatch(online, bankaRecords, new HashSet<int>());
                allCandidates.Add((oi, online, matchResult));
            }

            // 6b. Puanına göre azalan sırala
            allCandidates.Sort((a, b) => b.Result.ConfidenceScore.CompareTo(a.Result.ConfidenceScore));

            // 6c. En yüksek puandan başlayarak ata
            var resultsByOnlineIdx = new Dictionary<int, ComparisonMatchResult>();
            foreach (var (onlineIdx, online, matchResult) in allCandidates)
            {
                if (usedOnlineIndices.Contains(onlineIdx)) continue;

                if (matchResult.BankaRowIndex.HasValue && usedBankaIndices.Contains(matchResult.BankaRowIndex.Value))
                {
                    var retryResult = FindBestMatch(online, bankaRecords, usedBankaIndices);
                    resultsByOnlineIdx[onlineIdx] = retryResult;
                    if (retryResult.BankaRowIndex.HasValue)
                        usedBankaIndices.Add(retryResult.BankaRowIndex.Value);
                }
                else
                {
                    resultsByOnlineIdx[onlineIdx] = matchResult;
                    if (matchResult.BankaRowIndex.HasValue)
                        usedBankaIndices.Add(matchResult.BankaRowIndex.Value);
                }
                usedOnlineIndices.Add(onlineIdx);
            }

            // 6d. Sonuçları orijinal sırada topla
            for (int oi = 0; oi < onlineRecords.Count; oi++)
            {
                results.Add(resultsByOnlineIdx.TryGetValue(oi, out var res)
                    ? res
                    : CreateNotFoundResult(onlineRecords[oi]));
            }

            // 7. İstatistikler
            int matchedCount = results.Count(r => r.Status == MatchStatus.Matched);
            int partialCount = results.Count(r => r.Status == MatchStatus.PartialMatch);
            int notFoundCount = results.Count(r => r.Status == MatchStatus.NotFound);

            decimal totalOnlineAmount = onlineRecords.Sum(r => r.Miktar);
            decimal matchedAmount = results
                .Where(r => r.Status == MatchStatus.Matched || 
                           r.Status == MatchStatus.PartialMatch ||
                           r.Status == MatchStatus.MultipleMatches)
                .Sum(r => r.OnlineMiktar);
            
            // 8. Fazla banka kayıtları
            var surplusBankaRecords = bankaRecords
                .Where(b => !usedBankaIndices.Contains(b.RowIndex))
                .Select(b => new UnmatchedBankaRecord
                {
                    RowIndex = b.RowIndex, Tutar = b.Tutar, Tarih = b.Tarih,
                    Aciklama = b.Aciklama,
                    DetectedType = DetectRecordType(b.Aciklama, type),
                    PossibleReason = "Online dosyada karşılığı bulunamadı (manuel giriş olabilir)",
                    ParsedEsasNo = b.Parsed.EsasNo, ParsedMahkeme = b.Parsed.Mahkeme
                }).ToList();
            
            // 9. Eksik banka kayıtları
            var missingBankaRecords = results
                .Where(r => r.Status == MatchStatus.NotFound)
                .Select(r => new MissingBankaRecord
                {
                    RowIndex = r.OnlineRowIndex, DosyaNo = r.OnlineDosyaNo,
                    BirimAdi = r.OnlineBirimAdi, Miktar = r.OnlineMiktar, Tarih = r.OnlineTarih,
                    Reason = "Banka dosyasında karşılığı bulunamadı (bedeli gelmemiş olabilir)"
                }).ToList();
            
            // 10. Tutar dengesi
            decimal totalBankaAmount = bankaRecords.Sum(b => b.Tutar);
            decimal surplusAmount = surplusBankaRecords.Sum(s => s.Tutar);
            decimal missingAmount = missingBankaRecords.Sum(m => m.Miktar);
            decimal netDifference = totalBankaAmount - totalOnlineAmount;
            
            string balanceSummary = CreateBalanceSummary(
                totalOnlineAmount, totalBankaAmount, 
                surplusBankaRecords.Count, surplusAmount,
                missingBankaRecords.Count, missingAmount);

            var report = new ComparisonReport
            {
                Type = type,
                GeneratedAt = DateTime.UtcNow,
                ReportDate = filterDate,
                TotalOnlineRecords = onlineRecords.Count,
                TotalBankaRecords = totalBankaRecords,
                TotalBankaAlacakRecords = bankaRecords.Count,
                MatchedCount = matchedCount,
                PartialMatchCount = partialCount,
                NotFoundCount = notFoundCount,
                TotalOnlineAmount = totalOnlineAmount,
                TotalMatchedAmount = matchedAmount,
                UnmatchedAmount = totalOnlineAmount - matchedAmount,
                SurplusBankaCount = surplusBankaRecords.Count,
                SurplusAmount = surplusAmount,
                MissingBankaCount = missingBankaRecords.Count,
                MissingAmount = missingAmount,
                NetAmountDifference = netDifference,
                BalanceSummary = balanceSummary,
                Results = results,
                SurplusBankaRecords = surplusBankaRecords,
                MissingBankaRecords = missingBankaRecords,
                Issues = issues
            };

            return Result<ComparisonReport>.Success(report);
        }
        catch (Exception ex)
        {
            return Result<ComparisonReport>.Fail($"Karşılaştırma hatası: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // İç record türleri
    // ─────────────────────────────────────────────────────────────

    private sealed record BankaRecord(
        int RowIndex, decimal Tutar, DateTime? Tarih,
        string? Aciklama, string? BorcAlacak, ParsedBankaAciklama Parsed);

    private sealed record OnlineRecord(
        int RowIndex, string? DosyaNo, string? BirimAdi,
        decimal Miktar, DateTime? Tarih);
    
    private sealed record ReddiyatRecord(
        int RowIndex, string? BirimAdi, string? EsasNo,
        string? ReferansNo, DateTime? Tarih,
        decimal OdenecekMiktar, decimal NetOdenecek,
        decimal GelirVergisi, decimal DamgaVergisi);
    
    /// <summary>
    /// Gruplu referans eşleştirmesi için yardımcı record.
    /// </summary>
    private sealed record GroupedReddiyatMatch(
        string ReferansNo, List<ReddiyatRecord> Records,
        decimal TotalNetOdenecek, decimal TotalOdenecek);
}
