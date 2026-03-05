#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

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
        HashSet<int> usedIndices)
    {
        var candidates = new List<(BankaRecord Record, double Score, string Reason)>();

        foreach (var banka in bankaRecords)
        {
            // Zaten kullanılmış mı?
            if (usedIndices.Contains(banka.RowIndex))
                continue;

            // Tutar kontrolü (zorunlu)
            if (!IsAmountMatch(online.Miktar, banka.Tutar))
                continue;

            // Puan hesapla
            double score = 0;
            var reasons = new List<string>();

            // 1. Tutar eşleşmesi bazal puan
            score += 0.1;
            reasons.Add("Tutar eşleşti");

            // 2. Esas No eşleşmesi (+0.4)
            if (!string.IsNullOrEmpty(online.DosyaNo) && !string.IsNullOrEmpty(banka.Parsed.EsasNo))
            {
                if (NormalizeDosyaNo(banka.Parsed.EsasNo) == online.DosyaNo)
                {
                    score += 0.4;
                    reasons.Add("EsasNo tam eşleşti");
                }
                else if (banka.Aciklama?.Contains(online.DosyaNo, StringComparison.OrdinalIgnoreCase) == true)
                {
                    score += 0.3;
                    reasons.Add("EsasNo açıklamada bulundu");
                }
            }

            // 3. Birim Adı eşleşmesi (+0.3)
            // NOT: Mahkeme numarası farklı olsa bile diğer kriterlerle eşleşebilir
            bool courtNumberMismatch = false;
            if (!string.IsNullOrEmpty(online.BirimAdi) && !string.IsNullOrEmpty(banka.Aciklama))
            {
                var birimMatch = MatchBirimAdi(online.BirimAdi, banka.Aciklama, banka.Parsed.Mahkeme);
                
                if (birimMatch.score < 0)
                {
                    // Mahkeme numarası farklı - sadece işaretle, henüz diskalifiye etme
                    courtNumberMismatch = true;
                    // BirimAdı skoru 0 kalacak, bonus yok
                }
                else
                {
                    score += birimMatch.score;
                    if (!string.IsNullOrEmpty(birimMatch.reason))
                        reasons.Add(birimMatch.reason);
                }
            }
            
            // KRİTİK KURAL: Esas No TAM eşleşiyor VE mahkeme numarası farklı ise → diskalifiye
            // Çünkü aynı esas no farklı mahkemelere ait olamaz
            bool esasNoMatches = !string.IsNullOrEmpty(online.DosyaNo) && 
                                 !string.IsNullOrEmpty(banka.Parsed.EsasNo) &&
                                 NormalizeDosyaNo(banka.Parsed.EsasNo) == online.DosyaNo;
            
            if (esasNoMatches && courtNumberMismatch)
            {
                // Esas no eşleşiyor ama mahkeme numarası farklı - bu kesinlikle yanlış eşleşme
                continue;
            }

            // 4. Tarih eşleşmesi (+0.2)
            if (online.Tarih.HasValue && banka.Tarih.HasValue)
            {
                var daysDiff = Math.Abs((online.Tarih.Value - banka.Tarih.Value).Days);
                if (daysDiff == 0)
                {
                    score += 0.2;
                    reasons.Add("Aynı gün");
                }
                else if (daysDiff <= 1)
                {
                    score += 0.15;
                    reasons.Add("±1 gün");
                }
                else if (daysDiff <= DateToleranceDays)
                {
                    score += 0.1;
                    reasons.Add($"±{daysDiff} gün");
                }
            }

            if (score > 0.1) // Sadece tutardan fazla eşleşme varsa
            {
                candidates.Add((banka, score, string.Join(", ", reasons)));
            }
        }

        // En yüksek puanlı eşleşmeyi seç
        if (candidates.Count == 0)
        {
            return CreateNotFoundResult(online);
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = candidates[0];

        // Çoklu eşleşme kontrolü
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
        }

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
