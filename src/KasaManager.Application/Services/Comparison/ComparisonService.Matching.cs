#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services.Comparison;

// ─────────────────────────────────────────────────────────────
// ComparisonService — Matching (Tahsilat/Masraf Eşleştirme)
// ─────────────────────────────────────────────────────────────
public sealed partial class ComparisonService
{
    /// <summary>
    /// Online kayıt için en iyi Banka eşleşmesini bulur.
    /// </summary>
    private ComparisonMatchResult FindBestMatch(
        OnlineRecord online,
        List<BankaRecord> bankaRecords,
        HashSet<int> usedIndices,
        ComparisonType type)
    {
        var candidates = new List<(BankaRecord Record, double Score, string Reason)>();

        foreach (var banka in bankaRecords)
        {
            if (usedIndices.Contains(banka.RowIndex))
                continue;

            if (!IsAmountMatch(online.Miktar, banka.Tutar))
                continue;

            double score = 0;
            var reasons = new List<string>();

            score += 0.1;
            reasons.Add("Tutar eslesti");
            LogHarcCandidate(type, online, banka, "AmountMatched");

            if (!string.IsNullOrEmpty(online.DosyaNo) && !string.IsNullOrEmpty(banka.Parsed.EsasNo))
            {
                if (NormalizeDosyaNo(banka.Parsed.EsasNo) == online.DosyaNo)
                {
                    score += 0.4;
                    reasons.Add("EsasNo tam eslesti");
                }
                else if (banka.Aciklama?.Contains(online.DosyaNo, StringComparison.OrdinalIgnoreCase) == true)
                {
                    score += 0.3;
                    reasons.Add("EsasNo aciklamada bulundu");
                }
            }

            var courtNumberMismatch = false;
            if (!string.IsNullOrEmpty(online.BirimAdi) && !string.IsNullOrEmpty(banka.Aciklama))
            {
                var birimMatch = MatchBirimAdi(online.BirimAdi, banka.Aciklama, banka.Parsed.Mahkeme);
                if (birimMatch.score < 0)
                {
                    courtNumberMismatch = true;
                }
                else if (birimMatch.score == 0 && HasSameCourtIdentity(online.BirimAdi, banka.Aciklama))
                {
                    score += 0.3;
                    reasons.Add("Mahkeme tarih/tutar fallback eslesti");
                }
                else
                {
                    score += birimMatch.score;
                    if (!string.IsNullOrEmpty(birimMatch.reason))
                        reasons.Add(birimMatch.reason);
                }
            }

            var esasNoMatches = !string.IsNullOrEmpty(online.DosyaNo)
                && !string.IsNullOrEmpty(banka.Parsed.EsasNo)
                && NormalizeDosyaNo(banka.Parsed.EsasNo) == online.DosyaNo;

            if (esasNoMatches && courtNumberMismatch)
            {
                score -= 0.4;
                if (score < 0.1) score = 0.1;
                reasons.Add("Mahkeme numarasi uyusmuyor (EsasNo eslesti ama teyit gerekli)");
            }

            if (online.Tarih.HasValue && banka.Tarih.HasValue)
            {
                var daysDiff = Math.Abs((online.Tarih.Value - banka.Tarih.Value).Days);
                if (daysDiff == 0)
                {
                    score += 0.2;
                    reasons.Add("Ayni gun");
                }
                else if (daysDiff <= 1)
                {
                    score += 0.15;
                    reasons.Add("+/-1 gun");
                }
                else if (daysDiff <= DateToleranceDays)
                {
                    score += 0.1;
                    reasons.Add($"+/-{daysDiff} gun");
                }
            }

            var reason = string.Join(", ", reasons);
            LogHarcScore(type, online, banka, score, reason);

            if (score > 0.1)
                candidates.Add((banka, score, reason));
            else
                LogHarcRejected(type, online, banka, score, "BelowCandidateFloor");
        }

        if (candidates.Count == 0)
        {
            LogHarcRejected(type, online, null, 0, "NoCandidate");
            return CreateNotFoundResult(online);
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = candidates[0];

        MatchStatus status;
        if (candidates.Count > 1 && Math.Abs(candidates[0].Score - candidates[1].Score) < 0.05)
        {
            status = MatchStatus.MultipleMatches;
        }
        else if (best.Score >= 0.8)
        {
            status = MatchStatus.Matched;
        }
        else if (best.Score >= 0.5)
        {
            status = MatchStatus.PartialMatch;
        }
        else
        {
            status = MatchStatus.NotFound;
            LogHarcRejected(type, online, best.Record, best.Score, "BelowPartialThreshold");
        }

        if (status == MatchStatus.PartialMatch)
            LogHarcPartialCreated(type, online, best.Record, best.Score, best.Reason);

        return new ComparisonMatchResult
        {
            OnlineRowIndex = online.RowIndex,
            OnlineDosyaNo = online.DosyaNo,
            OnlineBirimAdi = online.BirimAdi,
            OnlineMiktar = online.Miktar,
            OnlineTarih = online.Tarih,
            BankaRowIndex = best.Record.RowIndex,
            BankaAciklama = best.Record.Aciklama,
            BankaTutar = best.Record.Tutar,
            BankaTarih = best.Record.Tarih,
            BankaBorcAlacak = best.Record.BorcAlacak,
            ParsedIl = best.Record.Parsed.Il,
            ParsedMahkeme = best.Record.Parsed.Mahkeme,
            ParsedEsasNo = best.Record.Parsed.EsasNo,
            ParsedKeyword = best.Record.Parsed.FoundKeyword,
            Status = status,
            ConfidenceScore = best.Score,
            MatchReason = best.Reason
        };
    }

    private void LogHarcCandidate(ComparisonType type, OnlineRecord online, BankaRecord banka, string reason)
    {
        if (type != ComparisonType.HarcamaHarc) return;
        _logger.LogInformation(
            "[HARC-MATCH-CANDIDATE] OnlineRow={OnlineRow} DosyaNo={DosyaNo} Birim={Birim} Miktar={Miktar} Tarih={OnlineTarih} BankaRow={BankaRow} BankaTutar={BankaTutar} BankaTarih={BankaTarih} ParsedMahkeme={ParsedMahkeme} ParsedEsasNo={ParsedEsasNo} Reason={Reason}",
            online.RowIndex, online.DosyaNo, online.BirimAdi, online.Miktar, online.Tarih,
            banka.RowIndex, banka.Tutar, banka.Tarih, banka.Parsed.Mahkeme, banka.Parsed.EsasNo, reason);
    }

    private void LogHarcScore(ComparisonType type, OnlineRecord online, BankaRecord banka, double score, string reason)
    {
        if (type != ComparisonType.HarcamaHarc) return;
        _logger.LogInformation(
            "[HARC-MATCH-SCORE] OnlineRow={OnlineRow} DosyaNo={DosyaNo} BankaRow={BankaRow} Score={Score} Reason={Reason}",
            online.RowIndex, online.DosyaNo, banka.RowIndex, score, reason);
    }

    private void LogHarcRejected(ComparisonType type, OnlineRecord online, BankaRecord? banka, double score, string reason)
    {
        if (type != ComparisonType.HarcamaHarc) return;
        _logger.LogInformation(
            "[HARC-MATCH-REJECTED] OnlineRow={OnlineRow} DosyaNo={DosyaNo} BankaRow={BankaRow} Score={Score} Reason={Reason} Consumed=false",
            online.RowIndex, online.DosyaNo, banka?.RowIndex, score, reason);
    }

    private void LogHarcPartialCreated(ComparisonType type, OnlineRecord online, BankaRecord banka, double score, string reason)
    {
        if (type != ComparisonType.HarcamaHarc) return;
        _logger.LogInformation(
            "[HARC-PARTIAL-CANDIDATE-CREATED] OnlineRow={OnlineRow} DosyaNo={DosyaNo} Birim={Birim} Miktar={Miktar} BankaRow={BankaRow} Score={Score} Reason={Reason}",
            online.RowIndex, online.DosyaNo, online.BirimAdi, online.Miktar, banka.RowIndex, score, reason);
    }

    private static bool HasSameCourtIdentity(string? onlineBirim, string? bankaAciklama)
    {
        var onlineCourt = TryExtractCourtIdentity(onlineBirim);
        var bankaCourt = TryExtractCourtIdentity(bankaAciklama);
        return onlineCourt.HasValue
            && bankaCourt.HasValue
            && onlineCourt.Value.Number == bankaCourt.Value.Number
            && onlineCourt.Value.Kind == bankaCourt.Value.Kind;
    }

    private static (string Number, string Kind)? TryExtractCourtIdentity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = NormalizeCourtSearchText(text);
        var match = System.Text.RegularExpressions.Regex.Match(
            normalized,
            @"(?<no>\d{1,2})\s*\.?\s*(?<kind>IDARE|VERGI|ICRA|ASLIYE|HUKUK|CEZA|SULH|TICARET|IS|KADASTRO|TUKETICI)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success
            ? (match.Groups["no"].Value, match.Groups["kind"].Value.ToUpperInvariant())
            : null;
    }

    private static string NormalizeCourtSearchText(string text)
    {
        return text.ToUpperInvariant()
            .Replace("\u0130", "I")
            .Replace("\u0131", "I")
            .Replace("\u00dc", "U")
            .Replace("\u00fc", "U")
            .Replace("\u00d6", "O")
            .Replace("\u00f6", "O")
            .Replace("\u015e", "S")
            .Replace("\u015f", "S")
            .Replace("\u00c7", "C")
            .Replace("\u00e7", "C")
            .Replace("\u011e", "G")
            .Replace("\u011f", "G");
    }
    /// <summary>
    /// Eşleşme bulunamayan sonuç oluşturur.
    /// </summary>
    private static ComparisonMatchResult CreateNotFoundResult(OnlineRecord online)
    {
        return new ComparisonMatchResult
        {
            OnlineRowIndex = online.RowIndex,
            OnlineDosyaNo = online.DosyaNo,
            OnlineBirimAdi = online.BirimAdi,
            OnlineMiktar = online.Miktar,
            OnlineTarih = online.Tarih,
            Status = MatchStatus.NotFound,
            ConfidenceScore = 0,
            MatchReason = "Eşleşen banka kaydı bulunamadı"
        };
    }

    /// <summary>
    /// Birim adı eşleşmesini kontrol eder.
    /// KRİTİK: Parse edilmiş mahkeme bilgisini kullanır (anahtar kelime sonrası doğru parse edilmiş veri).
    /// </summary>
    private static (double score, string? reason) MatchBirimAdi(
        string onlineBirim, 
        string bankaAciklama,
        string? parsedMahkeme)
    {
        // ───────────────────────────────────────────────────────────
        // Mahkeme numarası kontrolü
        // ───────────────────────────────────────────────────────────
        var onlineNum = ExtractMahkemeNo(onlineBirim);
        var onlineTur = ExtractMahkemeTuru(onlineBirim);
        
        // Parse edilmiş mahkemeden numara ve tür çıkar (doğru kaynak!)
        // Örn: "22. İdare Mahkemesi" -> num="22", tür="idare"
        string? bankaNum = null;
        string? bankaTur = null;
        
        if (!string.IsNullOrEmpty(parsedMahkeme))
        {
            bankaNum = ExtractMahkemeNo(parsedMahkeme);
            bankaTur = ExtractMahkemeTuru(parsedMahkeme);
        }
        
        // Her iki tarafta da mahkeme numarası VAR ama FARKLI ise → negatif skor
        if (!string.IsNullOrEmpty(onlineNum) && !string.IsNullOrEmpty(bankaNum))
        {
            if (onlineNum != bankaNum)
            {
                // Mahkeme numaraları farklı
                return (-1, "Mahkeme numarası uyuşmuyor");
            }
        }
        
        // Her iki tarafta da tür VAR ama FARKLI ise → negatif skor
        if (!string.IsNullOrEmpty(onlineTur) && !string.IsNullOrEmpty(bankaTur))
        {
            if (!onlineTur.Equals(bankaTur, StringComparison.OrdinalIgnoreCase))
            {
                return (-1, "Mahkeme türü uyuşmuyor");
            }
        }
        
        // ───────────────────────────────────────────────────────────
        // Pozitif eşleşme puanlama
        // ───────────────────────────────────────────────────────────
        var normalizedOnline = NormalizeBirimAdi(onlineBirim);
        var normalizedAciklama = NormalizeBirimAdi(bankaAciklama);

        // Tam match
        if (normalizedAciklama.Contains(normalizedOnline))
            return (0.3, "BirimAdı tam eşleşti");

        // Parse edilmiş mahkeme ile karşılaştır
        if (!string.IsNullOrEmpty(parsedMahkeme))
        {
            var normalizedParsed = NormalizeBirimAdi(parsedMahkeme);
            if (normalizedOnline.Contains(normalizedParsed) || normalizedParsed.Contains(normalizedOnline))
                return (0.25, "Mahkeme kısmen eşleşti");
        }

        // Mahkeme numarası ve türü eşleşiyorsa
        if (!string.IsNullOrEmpty(onlineNum) && onlineNum == bankaNum && 
            !string.IsNullOrEmpty(onlineTur) && onlineTur.Equals(bankaTur, StringComparison.OrdinalIgnoreCase))
        {
            return (0.2, $"{onlineNum}. {onlineTur} eşleşti");
        }
        
        // Bir tarafta mahkeme numarası yok, diğer kriterler var
        // Bu durumda kısmi eşleşme kabul edilebilir (numara girilmemiş olabilir)
        if (string.IsNullOrEmpty(onlineNum) || string.IsNullOrEmpty(bankaNum))
        {
            // En azından mahkeme türü eşleşiyorsa
            if (!string.IsNullOrEmpty(onlineTur) && !string.IsNullOrEmpty(bankaTur) &&
                onlineTur.Equals(bankaTur, StringComparison.OrdinalIgnoreCase))
            {
                return (0.15, "Mahkeme türü eşleşti (numara belirsiz)");
            }
        }

        return (0, null);
    }
}
