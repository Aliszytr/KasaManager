using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

public class IntermediateLiveUstRaporProvider : IIntermediateLiveUstRaporProvider
{
    private readonly IImportOrchestrator _import;
    public IntermediateLiveUstRaporProvider(IImportOrchestrator import)
    {
        _import = import;
    }

    public Result<KasaUstLiveSummary> GetSummary(DateOnly raporTarihi, string uploadFolderAbsolute, List<string> issues)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(uploadFolderAbsolute);
            if (!directoryInfo.Exists)
                return Result<KasaUstLiveSummary>.Fail("Upload directory not found.");

            var files = directoryInfo.GetFiles("*.xls*").OrderByDescending(f => f.CreationTime).ToList();
            var kasaUstFile = files.FirstOrDefault(f => f.Name.Contains("kasa", StringComparison.OrdinalIgnoreCase) && f.Name.Contains("ust", StringComparison.OrdinalIgnoreCase))?.Name;

            if (kasaUstFile == null)
            {
                issues.Add("KasaÜstRapor dosyası bulunamadı. Live summary 0 kabul edildi.");
                return Result<KasaUstLiveSummary>.Success(CreateEmptySummary());
            }

            var fullPath = Path.Combine(uploadFolderAbsolute, kasaUstFile);
            var importRes = _import.Import(fullPath, ImportFileKind.KasaUstRapor);
            if (!importRes.Ok || importRes.Value == null)
            {
                issues.Add($"Live KasaÜstRapor import hatası: {importRes.Error}. 0 kabul edildi.");
                return Result<KasaUstLiveSummary>.Success(CreateEmptySummary());
            }

            var table = importRes.Value;
            var summaryRow = table.Rows.FirstOrDefault(r => IsSummaryRow(r));
            if (summaryRow == null)
            {
                issues.Add("Live KasaÜstRapor'da TOPLAMLAR satırı bulunamadı. 0 kabul edildi.");
                return Result<KasaUstLiveSummary>.Success(CreateEmptySummary());
            }

            var dict = summaryRow;

            decimal ReadDec(string key)
            {
                return TryParseDecimal(TryGet(dict, key), out var d) ? d : 0m;
            }

            var countSuffixes = new[] { "_islem_sayisi", "_sayisi" };
            bool IsCountKey(string key) =>
                countSuffixes.Any(s => key.EndsWith(s, StringComparison.OrdinalIgnoreCase));

            string? FindKeyContains(params string[] tokens)
            {
                foreach (var k in dict.Keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var kk = k.Trim();
                    if (IsCountKey(kk)) continue;
                    var ok = true;
                    foreach (var t in tokens)
                    {
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (!kk.Contains(t, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
                    }
                    if (ok) return kk;
                }
                return null;
            }

            decimal ReadDecFallback(string preferredKey, params string[] containsTokens)
            {
                if (dict.ContainsKey(preferredKey))
                    return ReadDec(preferredKey);

                var altKey = FindKeyContains(containsTokens);
                if (!string.IsNullOrWhiteSpace(altKey))
                    return ReadDec(altKey);

                return 0m;
            }

            var posTahsilat = ReadDecFallback("pos_tahsilat", "pos", "tahsil");
            var onlineTahsilat = ReadDecFallback("online_tahsilat", "online", "tahsil");
            var postTahsilat = ReadDecFallback("post_tahsilat", "post", "tahsil");
            var tahsilat = ReadDecFallback("tahsilat", "tahsil");
            var reddiyat = ReadDecFallback("reddiyat", "redd");

            var posHarc = ReadDecFallback("pos_harc", "pos", "harc");
            var onlineHarc = ReadDecFallback("online_harc", "online", "harc");
            var postHarc = ReadDecFallback("post_harc", "post", "harc");
            var gelmeyenPost = ReadDecFallback("gelmeyen_post", "gelmeyen", "post");

            var harc = ReadDec("harc");
            if (harc == 0m && !dict.ContainsKey("harc"))
            {
                var kToplam = FindKeyContains("toplam", "harc") ?? FindKeyContains("harc");
                if (!string.IsNullOrWhiteSpace(kToplam)
                    && !kToplam.Contains("pos", StringComparison.OrdinalIgnoreCase)
                    && !kToplam.Contains("online", StringComparison.OrdinalIgnoreCase))
                {
                    harc = ReadDec(kToplam);
                }
            }

            var gelirVergisi = ReadDecFallback("gelir_vergisi", "gelir", "verg");
            var damgaVergisi = ReadDecFallback("damga_vergisi", "damga", "verg");

            var stopaj = ReadDec("stopaj");
            if (stopaj == 0m && !dict.ContainsKey("stopaj"))
            {
                var kStopaj = FindKeyContains("stopaj");
                if (!string.IsNullOrWhiteSpace(kStopaj))
                    stopaj = ReadDec(kStopaj);
            }
            if (stopaj == 0m && !dict.ContainsKey("stopaj"))
                stopaj = gelirVergisi + damgaVergisi;

            return Result<KasaUstLiveSummary>.Success(new KasaUstLiveSummary(
                posTahsilat, onlineTahsilat, postTahsilat, tahsilat, reddiyat,
                posHarc, onlineHarc, postHarc, gelmeyenPost, harc,
                gelirVergisi, damgaVergisi, stopaj));
        }
        catch (Exception ex)
        {
            issues.Add($"Live summary okuma hatası: {ex.Message}. 0 kabul edildi.");
            return Result<KasaUstLiveSummary>.Success(CreateEmptySummary());
        }
    }

    private static KasaUstLiveSummary CreateEmptySummary() => new(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

    private static bool IsSummaryRow(Dictionary<string, string?> row)
    {
        foreach (var kv in row)
        {
            var v = kv.Value;
            if (string.IsNullOrWhiteSpace(v)) continue;
            var t = v.Trim().ToLowerInvariant();
            if (t == "toplam" || t.Contains("genel toplam") || t.Contains("toplamlar"))
                return true;
        }
        return false;
    }

    private static string? TryGet(Dictionary<string, string?> dict, string key)
    {
        if (dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
                return kvp.Value;
        }
        return null;
    }

    private static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim().Replace("₺", "").Replace(" ", "");
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
        return false;
    }
}
