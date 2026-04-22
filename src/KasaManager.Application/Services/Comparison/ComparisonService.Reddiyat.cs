#nullable enable
using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Application.Services.Comparison;

// ─────────────────────────────────────────────────────────────
// ComparisonService — Reddiyat (Çıkan Ödeme Karşılaştırma)
// ─────────────────────────────────────────────────────────────
public sealed partial class ComparisonService
{
    private Result<ComparisonReport> CompareReddiyatInternal(
        string uploadFolder, string bankaFileName, string reddiyatFileName, DateOnly? filterDate)
    {
        var issues = new List<string>();
        var results = new List<ComparisonMatchResult>();
        try
        {
            var bankaPath = ResolveFile(uploadFolder, bankaFileName);
            var reddiyatPath = ResolveFile(uploadFolder, reddiyatFileName);
            if (bankaPath == null) return Result<ComparisonReport>.Fail($"{bankaFileName} bulunamadı.");
            if (reddiyatPath == null) return Result<ComparisonReport>.Fail($"{reddiyatFileName} bulunamadı.");

            var bankaResult = _import.ImportTrueSource(bankaPath, ImportFileKind.BankaTahsilat);
            if (!bankaResult.Ok || bankaResult.Value == null)
                return Result<ComparisonReport>.Fail($"{bankaFileName} okunamadı: {bankaResult.Error}");
            var reddiyatResult = _import.ImportTrueSource(reddiyatPath, ImportFileKind.OnlineReddiyat);
            if (!reddiyatResult.Ok || reddiyatResult.Value == null)
                return Result<ComparisonReport>.Fail($"{reddiyatFileName} okunamadı: {reddiyatResult.Error}");

            var bankaTable = bankaResult.Value;
            var reddiyatTable = reddiyatResult.Value;

            // Kolon isimleri
            var bankaDateCol = CanonicalKeyHelper.FindDateCanonical(bankaTable) ?? "islem_tarihi";
            var bankaTutarCol = CanonicalKeyHelper.FindCanonical(bankaTable, "islem_tutari") ?? "islem_tutari";
            var bankaAciklamaCol = CanonicalKeyHelper.FindCanonical(bankaTable, "aciklama") ?? "aciklama";
            var bankaIslemAdiCol = CanonicalKeyHelper.FindCanonical(bankaTable, "islem_adi");
            var bankaBorcAlacakCol = CanonicalKeyHelper.FindCanonical(bankaTable, "borc_alacak")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "borç")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "borc")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(bankaTable, "alacak");
            var reddiyatDateCol = CanonicalKeyHelper.FindDateCanonical(reddiyatTable) ?? "tarih";
            var reddiyatBirimCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "birim_adi") ?? "birim_adi";
            var reddiyatEsasNoCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "dosya_no")
                ?? CanonicalKeyHelper.FindCanonical(reddiyatTable, "esas_no") ?? "dosya_no";
            var reddiyatMiktarCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "miktar") ?? "miktar";
            var reddiyatGVCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "gelir_vergisi")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(reddiyatTable, "gelir") ?? "gelir_vergisi";
            var reddiyatDVCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "damga_vergisi")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(reddiyatTable, "damga") ?? "damga_vergisi";
            var reddiyatRefNoCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "referans_no")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(reddiyatTable, "referans");
            var reddiyatOdenecekKisiCol = CanonicalKeyHelper.FindCanonical(reddiyatTable, "odenecek_kisi")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(reddiyatTable, "ödenecek")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(reddiyatTable, "odenecek");

            int totalBankaRecords = bankaTable.Rows.Count;

            // Banka kayıtlarını oku: hem Borç (-) hem Alacak (+) — iptal tespiti için gerekli
            var allBankaRecords = new List<BankaRecord>();
            for (int i = 0; i < bankaTable.Rows.Count; i++)
            {
                var row = bankaTable.Rows[i];
                if (row == null) continue;
                if (filterDate.HasValue && !DateParsingHelper.RowMatchesDate(row, bankaDateCol, filterDate.Value)) continue;
                if (!row.TryGetValue(bankaTutarCol, out var tutarRaw) || !DecimalParsingHelper.TryParseFromTurkish(tutarRaw, out var tutar)) continue;
                var borcAlacak = bankaBorcAlacakCol != null && row.TryGetValue(bankaBorcAlacakCol, out var baRaw) ? baRaw : null;
                var aciklamaRaw = row.TryGetValue(bankaAciklamaCol, out var acRaw) ? acRaw : null;
                var islemAdi = bankaIslemAdiCol != null && row.TryGetValue(bankaIslemAdiCol, out var iaRaw) ? iaRaw : null;
                var aciklama = string.Join(" ", new[] { islemAdi, aciklamaRaw }.Where(s => !string.IsNullOrEmpty(s)));
                DateTime? tarih = null;
                if (row.TryGetValue(bankaDateCol, out var dateRaw) && DateParsingHelper.TryParseDateTime(dateRaw, out var d)) tarih = d;
                var parsed = _parser.Parse(aciklama);
                var record = new BankaRecord(i, Math.Abs(tutar), tarih, aciklama, borcAlacak, parsed)
                {
                    IsBorc = IsBorc(tutar, borcAlacak)
                };
                allBankaRecords.Add(record);
            }

            // İptal edilen işlemleri tespit et ve işaretle
            var cancelledPairs = MarkCancelledRecords(allBankaRecords);

            // Sadece iptal edilmemiş Borç kayıtlarını karşılaştırmaya al
            var bankaRecords = allBankaRecords.Where(r => r.IsBorc && !r.IsCancelled).ToList();

            // Reddiyat kayıtlarını oku
            var reddiyatRecords = new List<ReddiyatRecord>();
            for (int i = 0; i < reddiyatTable.Rows.Count; i++)
            {
                var row = reddiyatTable.Rows[i];
                if (row == null) continue;
                if (filterDate.HasValue && !DateParsingHelper.RowMatchesDate(row, reddiyatDateCol, filterDate.Value)) continue;
                if (!row.TryGetValue(reddiyatMiktarCol, out var miktarRaw) || !DecimalParsingHelper.TryParseFromTurkish(miktarRaw, out var odenecekMiktar)) continue;
                var birimAdi = row.TryGetValue(reddiyatBirimCol, out var birimRaw) ? birimRaw : null;
                var esasNo = row.TryGetValue(reddiyatEsasNoCol, out var esasRaw) ? esasRaw : null;
                string? referansNo = null;
                if (reddiyatRefNoCol != null && row.TryGetValue(reddiyatRefNoCol, out var refRaw) && !string.IsNullOrWhiteSpace(refRaw))
                    referansNo = refRaw.Trim();
                if (string.IsNullOrWhiteSpace(referansNo) && reddiyatOdenecekKisiCol != null
                    && row.TryGetValue(reddiyatOdenecekKisiCol, out var odenecekKisiRaw) && !string.IsNullOrWhiteSpace(odenecekKisiRaw))
                {
                    var refMatch = System.Text.RegularExpressions.Regex.Match(odenecekKisiRaw, @"\((\d{9,15})\)");
                    if (refMatch.Success) referansNo = refMatch.Groups[1].Value;
                }
                DateTime? tarih = null;
                if (row.TryGetValue(reddiyatDateCol, out var dateRaw2) && DateParsingHelper.TryParseDateTime(dateRaw2, out var d2)) tarih = d2;
                decimal gelirVergisi = 0, damgaVergisi = 0;
                if (row.TryGetValue(reddiyatGVCol, out var gvRaw)) DecimalParsingHelper.TryParseFromTurkish(gvRaw, out gelirVergisi);
                if (row.TryGetValue(reddiyatDVCol, out var dvRaw)) DecimalParsingHelper.TryParseFromTurkish(dvRaw, out damgaVergisi);
                decimal netOdenecek = odenecekMiktar - (gelirVergisi + damgaVergisi);
                reddiyatRecords.Add(new ReddiyatRecord(i, birimAdi, esasNo, referansNo, tarih, odenecekMiktar, netOdenecek, gelirVergisi, damgaVergisi));
            }

            // Toplam hesaplamalar
            decimal totalGelirVergisi = reddiyatRecords.Sum(r => r.GelirVergisi);
            decimal totalDamgaVergisi = reddiyatRecords.Sum(r => r.DamgaVergisi);
            decimal totalStopaj = totalGelirVergisi + totalDamgaVergisi;
            decimal totalOdenecekMiktar = reddiyatRecords.Sum(r => r.OdenecekMiktar);
            decimal totalNetOdenecek = reddiyatRecords.Sum(r => r.NetOdenecek);
            decimal totalBankaCikis = bankaRecords.Sum(b => b.Tutar); // iptal edilenler zaten filtrelendi

            // Gelişmiş Eşleştirme: Gruplu referans + tekli
            var usedBankaIndices = new HashSet<int>();
            var matchedReddiyatIndices = new HashSet<int>();

            var groupedByRef = reddiyatRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.ReferansNo))
                .GroupBy(r => r.ReferansNo!.Trim())
                .Select(g => new GroupedReddiyatMatch(g.Key, g.ToList(), g.Sum(r => r.NetOdenecek), g.Sum(r => r.OdenecekMiktar)))
                .ToList();

            foreach (var group in groupedByRef)
            {
                var groupMatchResult = FindBestMatchForGroupedReddiyat(group, bankaRecords, usedBankaIndices);
                if (groupMatchResult.BankaRowIndex.HasValue) usedBankaIndices.Add(groupMatchResult.BankaRowIndex.Value);
                foreach (var record in group.Records)
                {
                    matchedReddiyatIndices.Add(record.RowIndex);
                    results.Add(new ComparisonMatchResult
                    {
                        OnlineRowIndex = record.RowIndex, OnlineDosyaNo = record.EsasNo,
                        OnlineBirimAdi = record.BirimAdi, OnlineMiktar = record.NetOdenecek,
                        OnlineTarih = record.Tarih,
                        BankaRowIndex = groupMatchResult.BankaRowIndex, BankaTutar = groupMatchResult.BankaTutar,
                        BankaTarih = groupMatchResult.BankaTarih, BankaAciklama = groupMatchResult.BankaAciklama,
                        BankaBorcAlacak = groupMatchResult.BankaBorcAlacak,
                        ParsedIl = groupMatchResult.ParsedIl, ParsedMahkeme = groupMatchResult.ParsedMahkeme,
                        ParsedEsasNo = groupMatchResult.ParsedEsasNo, ParsedKeyword = groupMatchResult.ParsedKeyword,
                        Status = groupMatchResult.Status, ConfidenceScore = groupMatchResult.ConfidenceScore,
                        MatchReason = $"Gruplu eşleşme (Ref: {group.ReferansNo}, {group.Records.Count} kayıt, Toplam: {group.TotalNetOdenecek:N2} ₺)"
                    });
                }
            }

            foreach (var reddiyat in reddiyatRecords.Where(r => !matchedReddiyatIndices.Contains(r.RowIndex)))
            {
                var matchResult = FindBestMatchForReddiyat(reddiyat, bankaRecords, usedBankaIndices);
                results.Add(matchResult);
                if (matchResult.BankaRowIndex.HasValue) usedBankaIndices.Add(matchResult.BankaRowIndex.Value);
            }

            // İstatistikler
            int matchedCount = results.Count(r => r.Status == MatchStatus.Matched);
            int partialCount = results.Count(r => r.Status == MatchStatus.PartialMatch);
            int notFoundCount = results.Count(r => r.Status == MatchStatus.NotFound);
            decimal matchedAmount = results.Where(r => r.Status == MatchStatus.Matched || r.Status == MatchStatus.PartialMatch).Sum(r => r.OnlineMiktar);

            var surplusBankaRecords = bankaRecords.Where(b => !usedBankaIndices.Contains(b.RowIndex))
                .Select(b => new UnmatchedBankaRecord { RowIndex = b.RowIndex, Tutar = b.Tutar, Tarih = b.Tarih,
                    Aciklama = b.Aciklama, DetectedType = DetectRecordType(b.Aciklama, ComparisonType.ReddiyatCikis),
                    PossibleReason = "Online reddiyat kaydı bulunamadı", ParsedEsasNo = b.Parsed.EsasNo, ParsedMahkeme = b.Parsed.Mahkeme }).ToList();
            var missingBankaRecords = results.Where(r => r.Status == MatchStatus.NotFound)
                .Select(r => new MissingBankaRecord { RowIndex = r.OnlineRowIndex, DosyaNo = r.OnlineDosyaNo,
                    BirimAdi = r.OnlineBirimAdi, Miktar = r.OnlineMiktar, Tarih = r.OnlineTarih, Reason = "Banka çıkış kaydı bulunamadı" }).ToList();

            decimal surplusAmount = surplusBankaRecords.Sum(s => s.Tutar);
            decimal missingAmount = missingBankaRecords.Sum(m => m.Miktar);
            decimal actualDiff = totalNetOdenecek - totalBankaCikis;
            string stopajStatus = CreateStopajStatus(totalNetOdenecek, totalBankaCikis, totalStopaj, totalOdenecekMiktar);
            string balanceSummary = CreateReddiyatBalanceSummary(totalOdenecekMiktar, totalNetOdenecek, totalBankaCikis, totalStopaj,
                surplusBankaRecords.Count, surplusAmount, missingBankaRecords.Count, missingAmount);

            // Commit 4: 3 durumlu stopaj analizi (iptal-aware)
            var virmanTutarlar = surplusBankaRecords
                .Where(x => string.Equals(x.DetectedType, "VIRMAN", StringComparison.OrdinalIgnoreCase))
                .Select(x => Math.Abs(x.Tutar))
                .ToList();
            var (stopajDurum, stopajMesaj) = DetermineStopajDurum(totalStopaj, virmanTutarlar, cancelledPairs);

            return Result<ComparisonReport>.Success(new ComparisonReport
            {
                Type = ComparisonType.ReddiyatCikis, GeneratedAt = DateTime.UtcNow, ReportDate = filterDate,
                TotalOnlineRecords = reddiyatRecords.Count, TotalBankaRecords = totalBankaRecords,
                TotalBankaAlacakRecords = bankaRecords.Count, MatchedCount = matchedCount,
                PartialMatchCount = partialCount, NotFoundCount = notFoundCount,
                TotalOnlineAmount = totalNetOdenecek, TotalMatchedAmount = matchedAmount,
                UnmatchedAmount = totalNetOdenecek - matchedAmount,
                SurplusBankaCount = surplusBankaRecords.Count, SurplusAmount = surplusAmount,
                MissingBankaCount = missingBankaRecords.Count, MissingAmount = missingAmount,
                NetAmountDifference = actualDiff, BalanceSummary = balanceSummary,
                TotalGelirVergisi = totalGelirVergisi, TotalDamgaVergisi = totalDamgaVergisi,
                TotalStopaj = totalStopaj, TotalOdenecekMiktar = totalOdenecekMiktar,
                TotalNetOdenecek = totalNetOdenecek, TotalBankaCikis = totalBankaCikis,
                StopajStatus = stopajStatus, Results = results,
                SurplusBankaRecords = surplusBankaRecords, MissingBankaRecords = missingBankaRecords, Issues = issues,
                // İptal edilen işlem bilgileri
                CancelledPairs = cancelledPairs,
                CancelledRecordsCount = cancelledPairs.Count,
                CancelledRecordsTotal = cancelledPairs.Sum(p => p.Tutar),
                // Commit 4: Enum tabanlı stopaj durumu (UI renk mapping)
                StopajDurum = stopajDurum,
                StopajMesaj = stopajMesaj
            });
        }
        catch (Exception ex) { return Result<ComparisonReport>.Fail($"Reddiyat karşılaştırma hatası: {ex.Message}"); }
    }

    private ComparisonMatchResult FindBestMatchForReddiyat(ReddiyatRecord reddiyat, List<BankaRecord> bankaRecords, HashSet<int> usedIndices)
    {
        var candidates = new List<(BankaRecord Record, double Score, string Reason, ParsedReddiyatGonderici? Parsed)>();
        foreach (var banka in bankaRecords)
        {
            if (usedIndices.Contains(banka.RowIndex)) continue;
            if (!IsAmountMatch(reddiyat.NetOdenecek, banka.Tutar)) continue;
            double score = 0.1;
            var reasons = new List<string> { "Tutar eşleşti" };
            var parsedGonderici = _parser.ParseReddiyatGonderici(banka.Aciklama);
            if (!string.IsNullOrEmpty(parsedGonderici.GonderenMahkeme) && !string.IsNullOrEmpty(reddiyat.BirimAdi))
            {
                var np = NormalizeMahkeme(parsedGonderici.GonderenMahkeme);
                var nr = NormalizeMahkeme(reddiyat.BirimAdi);
                if (np == nr) { score += 0.35; reasons.Add($"Mahkeme eşleşti: {reddiyat.BirimAdi}"); }
                else if (np.Contains("vezne") && nr.Contains("vezne") && np.Contains("ankara") && nr.Contains("ankara"))
                { score += 0.30; reasons.Add($"Vezne birim eşleşti: {reddiyat.BirimAdi}"); }
            }
            if (!string.IsNullOrEmpty(parsedGonderici.GonderenEsasNo) && !string.IsNullOrEmpty(reddiyat.EsasNo))
            {
                if ((NormalizeDosyaNo(parsedGonderici.GonderenEsasNo) ?? "") == (NormalizeDosyaNo(reddiyat.EsasNo) ?? ""))
                { score += 0.45; reasons.Add($"Esas No eşleşti: {reddiyat.EsasNo}"); }
            }
            else if (!string.IsNullOrEmpty(reddiyat.EsasNo) && !string.IsNullOrEmpty(banka.Parsed.EsasNo))
            {
                if ((NormalizeDosyaNo(reddiyat.EsasNo) ?? "") == (NormalizeDosyaNo(banka.Parsed.EsasNo) ?? ""))
                { score += 0.3; reasons.Add("EsasNo (alıcı) eşleşti"); }
            }
            if (!string.IsNullOrEmpty(reddiyat.BirimAdi) && !string.IsNullOrEmpty(banka.Aciklama))
            {
                var bm = MatchBirimAdi(reddiyat.BirimAdi, banka.Aciklama, banka.Parsed.Mahkeme);
                if (bm.score > 0) { score += bm.score * 0.5; if (!string.IsNullOrEmpty(bm.reason)) reasons.Add(bm.reason); }
            }
            if (reddiyat.Tarih.HasValue && banka.Tarih.HasValue)
            {
                var dd = Math.Abs((reddiyat.Tarih.Value - banka.Tarih.Value).Days);
                if (dd == 0) { score += 0.1; reasons.Add("Aynı gün"); }
                else if (dd <= 1) { score += 0.05; reasons.Add("±1 gün"); }
            }
            candidates.Add((banka, score, string.Join(", ", reasons), parsedGonderici));
        }
        if (candidates.Count == 0)
            return new ComparisonMatchResult { OnlineRowIndex = reddiyat.RowIndex, OnlineDosyaNo = reddiyat.EsasNo,
                OnlineBirimAdi = reddiyat.BirimAdi, OnlineMiktar = reddiyat.NetOdenecek, OnlineTarih = reddiyat.Tarih,
                Status = MatchStatus.NotFound, ConfidenceScore = 0, MatchReason = "Eşleşen banka çıkış kaydı bulunamadı" };
        var best = candidates.OrderByDescending(c => c.Score).First();
        var status = best.Score >= ConfidenceThresholdFull ? MatchStatus.Matched :
                     best.Score >= ConfidenceThresholdPartial ? MatchStatus.PartialMatch : MatchStatus.NotFound;
        return new ComparisonMatchResult
        {
            OnlineRowIndex = reddiyat.RowIndex, OnlineDosyaNo = reddiyat.EsasNo,
            OnlineBirimAdi = reddiyat.BirimAdi, OnlineMiktar = reddiyat.NetOdenecek, OnlineTarih = reddiyat.Tarih,
            BankaRowIndex = best.Record.RowIndex, BankaTutar = best.Record.Tutar,
            BankaTarih = best.Record.Tarih, BankaAciklama = best.Record.Aciklama,
            BankaBorcAlacak = best.Record.BorcAlacak,
            ParsedIl = best.Record.Parsed.Il, ParsedMahkeme = best.Record.Parsed.Mahkeme,
            ParsedEsasNo = best.Record.Parsed.EsasNo, ParsedKeyword = best.Record.Parsed.FoundKeyword,
            Status = status, ConfidenceScore = best.Score, MatchReason = best.Reason
        };
    }

    private ComparisonMatchResult FindBestMatchForGroupedReddiyat(GroupedReddiyatMatch group, List<BankaRecord> bankaRecords, HashSet<int> usedIndices)
    {
        var candidates = new List<(BankaRecord Record, double Score, string Reason)>();
        foreach (var banka in bankaRecords)
        {
            if (usedIndices.Contains(banka.RowIndex)) continue;
            if (!IsAmountMatch(group.TotalNetOdenecek, banka.Tutar)) continue;
            double score = 0.15;
            var reasons = new List<string> { $"Grup tutarı eşleşti ({group.Records.Count} kayıt)" };
            if (!string.IsNullOrEmpty(banka.Aciklama) && !string.IsNullOrEmpty(group.ReferansNo))
            {
                var refMatch = System.Text.RegularExpressions.Regex.Match(banka.Aciklama,
                    @"REFERANS\s*NO[:\s]*(\d{9,15})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (refMatch.Success && refMatch.Groups[1].Value == group.ReferansNo)
                { score += 0.7; reasons.Add($"Referans No tam eşleşti: {group.ReferansNo}"); }
                else if (banka.Aciklama.Contains(group.ReferansNo, StringComparison.OrdinalIgnoreCase))
                { score += 0.5; reasons.Add($"Referans No açıklamada: {group.ReferansNo}"); }
            }
            bool esasNoMatched = false;
            foreach (var record in group.Records)
            {
                if (!string.IsNullOrEmpty(record.EsasNo) && !string.IsNullOrEmpty(banka.Parsed.EsasNo)
                    && (NormalizeDosyaNo(record.EsasNo) ?? "") == (NormalizeDosyaNo(banka.Parsed.EsasNo) ?? ""))
                { score += 0.2; reasons.Add($"EsasNo eşleşti: {record.EsasNo}"); esasNoMatched = true; break; }
            }
            if (!esasNoMatched)
            {
                foreach (var record in group.Records)
                {
                    if (!string.IsNullOrEmpty(record.BirimAdi) && !string.IsNullOrEmpty(banka.Aciklama))
                    {
                        var bm = MatchBirimAdi(record.BirimAdi, banka.Aciklama, banka.Parsed.Mahkeme);
                        if (bm.score > 0) { score += bm.score * 0.5; if (!string.IsNullOrEmpty(bm.reason)) reasons.Add(bm.reason); break; }
                    }
                }
            }
            candidates.Add((banka, score, string.Join(", ", reasons)));
        }
        var representative = group.Records.First();
        if (candidates.Count == 0)
            return new ComparisonMatchResult { OnlineRowIndex = representative.RowIndex, OnlineDosyaNo = representative.EsasNo,
                OnlineBirimAdi = representative.BirimAdi, OnlineMiktar = group.TotalNetOdenecek, OnlineTarih = representative.Tarih,
                Status = MatchStatus.NotFound, ConfidenceScore = 0,
                MatchReason = $"Gruplu eşleşme bulunamadı (Ref: {group.ReferansNo}, Toplam: {group.TotalNetOdenecek:N2} ₺)" };
        var best = candidates.OrderByDescending(c => c.Score).First();
        var status = best.Score >= ConfidenceThresholdFull ? MatchStatus.Matched :
                     best.Score >= ConfidenceThresholdPartial ? MatchStatus.PartialMatch : MatchStatus.NotFound;
        return new ComparisonMatchResult
        {
            OnlineRowIndex = representative.RowIndex, OnlineDosyaNo = representative.EsasNo,
            OnlineBirimAdi = representative.BirimAdi, OnlineMiktar = group.TotalNetOdenecek,
            OnlineTarih = representative.Tarih,
            BankaRowIndex = best.Record.RowIndex, BankaTutar = best.Record.Tutar,
            BankaTarih = best.Record.Tarih, BankaAciklama = best.Record.Aciklama,
            BankaBorcAlacak = best.Record.BorcAlacak,
            ParsedIl = best.Record.Parsed.Il, ParsedMahkeme = best.Record.Parsed.Mahkeme,
            ParsedEsasNo = best.Record.Parsed.EsasNo, ParsedKeyword = best.Record.Parsed.FoundKeyword,
            Status = status, ConfidenceScore = best.Score, MatchReason = best.Reason
        };
    }

    private static bool IsBorc(decimal tutar, string? borcAlacak)
    {
        if (tutar < 0) return true;
        if (string.IsNullOrWhiteSpace(borcAlacak)) return false;
        var lower = borcAlacak.ToLowerInvariant();
        return lower.Contains("borç") || lower.Contains("borc") || lower == "b" || lower == "-";
    }

    private static string CreateStopajStatus(decimal netOdenecek, decimal bankaCikis, decimal stopaj, decimal odenecekMiktar)
    {
        decimal diff = netOdenecek - bankaCikis;
        decimal bruteCheck = odenecekMiktar - bankaCikis;
        const decimal tolerance = 1m;
        if (Math.Abs(diff) <= tolerance) return "✅ Dengeli: Net ödenecek tutar ile banka çıkışı eşleşiyor. Stopaj virmanı yapılmamış.";
        if (Math.Abs(diff - stopaj) <= tolerance) return $"✅ Normal: Fark ({diff:N2} ₺) stopaj tutarına ({stopaj:N2} ₺) eşit. Stopaj henüz virman edilmemiş.";
        if (Math.Abs(bruteCheck) <= tolerance) return "✅ Tam Dengeli: Ödenecek miktar (brüt) ile banka çıkışı eşleşiyor. Stopaj virmanı yapılmış.";
        return diff > 0
            ? $"⚠️ Eksik Çıkış: Bankadan {diff:N2} ₺ eksik çıkmış. Beklenen fark (stopaj): {stopaj:N2} ₺, Gerçek fark: {diff:N2} ₺"
            : $"⚠️ Fazla Çıkış: Bankadan {Math.Abs(diff):N2} ₺ fazla çıkmış. Beklenen fark (stopaj): {stopaj:N2} ₺, Gerçek fark: {diff:N2} ₺";
    }

    private static string CreateReddiyatBalanceSummary(
        decimal odenecekMiktar, decimal netOdenecek, decimal bankaCikis, decimal stopaj,
        int surplusCount, decimal surplusAmount, int missingCount, decimal missingAmount)
    {
        var messages = new List<string>
        {
            "📋 **Özet Bilgiler:**",
            $"• Toplam Ödenecek (Brüt): {odenecekMiktar:N2} ₺",
            $"• Toplam Net Ödenecek: {netOdenecek:N2} ₺",
            $"• Toplam Stopaj: {stopaj:N2} ₺",
            $"• Toplam Banka Çıkışı: {bankaCikis:N2} ₺"
        };
        if (surplusCount > 0) messages.Add($"\n⚠️ Bankada {surplusCount} adet fazla çıkış tespit edildi (Toplam: {surplusAmount:N2} ₺).");
        if (missingCount > 0) messages.Add($"⚠️ Online'da {missingCount} adet ödeme için banka çıkışı bulunamadı (Toplam: {missingAmount:N2} ₺).");
        return string.Join("\n", messages);
    }

    // ─────────────────────────────────────────────────────────────
    // İptal Edilen İşlem Tespiti (Plan v4 — MarkCancelledRecords)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Türkçe kültür bilgisi — İ↔i, I↔ı dönüşümleri için.
    /// </summary>
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    /// <summary>
    /// İptal anahtar kelimeleri (Türkçe lowercase). "iade" bilinçli olarak hariç.
    /// </summary>
    internal static readonly string[] CancelKeywords =
        { "iptal", "ıptal", "geri alma", "geri alındı", "düzeltme" };

    /// <summary>
    /// Açıklama metninde iptal anahtar kelimelerinden birinin geçip geçmediğini kontrol eder.
    /// Türkçe kültür kurallarıyla lowercase normalize eder (İ→i, I→ı).
    /// </summary>
    internal static bool ContainsCancelKeyword(string? aciklama)
    {
        if (string.IsNullOrWhiteSpace(aciklama)) return false;
        var lower = aciklama.ToLower(TurkishCulture);
        foreach (var keyword in CancelKeywords)
        {
            if (lower.Contains(keyword))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Banka kayıtları içinde iptal edilmiş işlemleri tespit eder ve eşleştirir.
    /// <para>Kurallar (Plan v4):</para>
    /// <list type="bullet">
    ///   <item>Tarih Sırası: orijinal.Tarih &lt; iptal.Tarih (kesinlikle küçük, eşitlik hariç)</item>
    ///   <item>EFT_OTOMATIK_IADE türü taramaya dahil edilmez (meşru iadeler korunur)</item>
    ///   <item>İşlem Türü Eşleşmesi: "Virman İptal" sadece "Virman" ile eşleşir</item>
    ///   <item>Zaman Olarak En Yakın: birden fazla aday varsa iptale en yakın öncesindeki seçilir</item>
    ///   <item>Tek Seferlik: IsCancelled == true olan kayıt tekrar eşleşemez</item>
    /// </list>
    /// </summary>
    internal static List<CancelledPair> MarkCancelledRecords(List<BankaRecord> allRecords)
    {
        var pairs = new List<CancelledPair>();

        // 1. İptal satırlarını bul (Alacak/+ yönlü, iptal anahtar kelimesi içeren)
        var iptalKayitlari = new List<BankaRecord>();
        foreach (var record in allRecords)
        {
            // Borç kayıtları iptal satırı olamaz
            if (record.IsBorc) continue;
            // Zaten işaretlenmiş kaydı atla
            if (record.IsCancelled) continue;

            var aciklama = record.Aciklama;
            if (string.IsNullOrWhiteSpace(aciklama)) continue;

            // EFT_OTOMATIK_IADE türündeki kayıtlar kesinlikle hariç (meşru iade)
            var detectedType = DetectRecordType(aciklama, ComparisonType.ReddiyatCikis);
            if (string.Equals(detectedType, "EFT_OTOMATIK_IADE", StringComparison.OrdinalIgnoreCase))
                continue;

            // İptal anahtar kelimesi kontrolü (Türkçe kültür kurallarıyla)
            if (!ContainsCancelKeyword(aciklama)) continue;

            iptalKayitlari.Add(record);
        }

        // 2. Her iptal kaydı için orijinal Borç eşleşmesini bul
        foreach (var iptal in iptalKayitlari)
        {
            if (iptal.IsCancelled) continue; // Başka bir iptal ile zaten eşleşmiş olabilir

            // İptal kaydının işlem türünü belirle ("Virman İptal" → "VIRMAN" bekleriz)
            var iptalType = DetectRecordType(iptal.Aciklama, ComparisonType.ReddiyatCikis);

            // Adayları filtrele: Borç, aynı tutar, daha erken tarih, henüz iptal edilmemiş
            var adaylar = allRecords
                .Where(a => a.IsBorc                                          // Borç yönlü olmalı
                         && !a.IsCancelled                                    // Tek seferlik kuralı
                         && a.Tutar == iptal.Tutar                            // Aynı tutar
                         && a.Tarih.HasValue && iptal.Tarih.HasValue          // Tarih bilgisi olmalı
                         && a.Tarih.Value < iptal.Tarih.Value)                // KESİNLİKLE küçük
                .ToList();

            // İşlem türü eşleşmesi: "Virman İptal" sadece "Virman" ile eşleşmeli
            if (string.Equals(iptalType, "VIRMAN", StringComparison.OrdinalIgnoreCase))
            {
                var virmanAdaylar = adaylar
                    .Where(a => string.Equals(
                        DetectRecordType(a.Aciklama, ComparisonType.ReddiyatCikis),
                        "VIRMAN", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (virmanAdaylar.Count > 0)
                    adaylar = virmanAdaylar;
            }

            if (adaylar.Count == 0) continue;

            // Zaman olarak en yakın: iptale en yakın öncesindeki orijinal işlem
            var enYakin = adaylar
                .OrderByDescending(a => a.Tarih!.Value) // En yakın olan ilk sıraya
                .First();

            // Eşleşme — her iki kaydı da işaretle
            enYakin.IsCancelled = true;
            iptal.IsCancelled = true;

            // Okunabilir açıklama üret
            var sure = iptal.Tarih!.Value - enYakin.Tarih!.Value;
            var aciklama = FormatCancelledPairAciklama(iptal.Tutar, sure);

            pairs.Add(new CancelledPair(
                OrijinalRowIndex: enYakin.RowIndex,
                IptalRowIndex: iptal.RowIndex,
                Tutar: iptal.Tutar,
                OrijinalTarih: enYakin.Tarih!.Value,
                IptalTarihi: iptal.Tarih!.Value,
                Aciklama: aciklama,
                Tur: DetectRecordType(enYakin.Aciklama, ComparisonType.ReddiyatCikis)
            ));
        }

        return pairs;
    }

    /// <summary>
    /// Stopaj virman durumunu belirler.
    /// Mantık, BankaHesapKontrolService.Helpers.CheckStopajFromAllVirmans ile aynı karar ağacını uygular.
    /// Bkz: src/KasaManager.Infrastructure/Services/BankaHesapKontrolService.Helpers.cs
    /// Application → Infrastructure referansı olmadığı için burada yeniden uygulanmıştır.
    /// </summary>
    internal static (StopajStatus Status, string Mesaj) DetermineStopajDurum(
        decimal hedefStopajTutari,
        List<decimal> gecerliVirmanTutarlar,
        List<CancelledPair>? iptalKayitlari = null)
    {
        if (hedefStopajTutari <= 0)
            return (StopajStatus.Ok, "Stopaj tutarı yok.");

        iptalKayitlari ??= new List<CancelledPair>();

        // Sadece Virman türündeki iptalleri al
        var iptalEdilenVirmanlar = iptalKayitlari
            .Where(r => string.Equals(r.Tur, "VIRMAN", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal toplamGecerliVirman = gecerliVirmanTutarlar.Sum();

        if (iptalEdilenVirmanlar.Count > 0 && toplamGecerliVirman > 0 && toplamGecerliVirman >= hedefStopajTutari)
        {
            // Durum: İptal var AMA yerine doğrusu yapılmış
            return (StopajStatus.OkWithNote,
                $"ℹ️ {iptalEdilenVirmanlar.Sum(x => x.Tutar):N2} ₺ virman iptal edildi, yerine doğru tutarla yeniden yapıldı.");
        }

        if (iptalEdilenVirmanlar.Count > 0 && toplamGecerliVirman < hedefStopajTutari)
        {
            // Durum: İptal var, yerine yenisi YAPILMAMIŞ
            return (StopajStatus.WarningPending,
                toplamGecerliVirman == 0
                    ? "⚠️ Tüm virman işlemleri iptal edildi. Stopaj virmanı yapılmamış durumda."
                    : $"⚠️ {iptalEdilenVirmanlar.Sum(x => x.Tutar):N2} ₺ virman iptal edildi, ancak yerine yeni virman yapılmamış. Stopaj askıda.");
        }

        if (iptalEdilenVirmanlar.Count == 0 && toplamGecerliVirman < hedefStopajTutari)
        {
            // Durum: Gerçek eksik
            return (StopajStatus.Error,
                toplamGecerliVirman == 0
                    ? $"⚠️ Stopaj Hesabına {hedefStopajTutari:N2} ₺ Virman yapılmadığını görüyorum. Lütfen Hesapları kontrol ediniz."
                    : $"❌ Stopaj Hesabına {hedefStopajTutari:N2} ₺ virman bekleniyor, en yakın virman: {toplamGecerliVirman:N2} ₺.");
        }

        // Durum: Normal başarılı
        return (StopajStatus.Ok, $"✅ Stopaj virmanı başarıyla yapılmış: {toplamGecerliVirman:N2} ₺");
    }

    /// <summary>
    /// İptal çifti için okunabilir açıklama metni üretir.
    /// Örn: "18.940,88 ₺ virman yapıldı, 4 dk sonra iptal edildi"
    /// </summary>
    internal static string FormatCancelledPairAciklama(decimal tutar, TimeSpan sure)
    {
        string sureMetni;
        if (sure.TotalMinutes < 1)
            sureMetni = "1 dk içinde";
        else if (sure.TotalMinutes < 60)
            sureMetni = $"{(int)sure.TotalMinutes} dk sonra";
        else if (sure.TotalHours < 24)
            sureMetni = $"{(int)sure.TotalHours} saat {sure.Minutes} dk sonra";
        else
            sureMetni = $"{(int)sure.TotalDays} gün sonra";

        return $"{tutar:N2} ₺ virman yapıldı, {sureMetni} iptal edildi";
    }
}
