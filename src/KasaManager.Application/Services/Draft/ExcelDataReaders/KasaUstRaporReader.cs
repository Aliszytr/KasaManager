#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Application.Services.Draft.Helpers;

namespace KasaManager.Application.Services.Draft.ExcelDataReaders;

/// <summary>
/// R19: KasaÜstRapor snapshot'ından veri okuma.
/// KasaDraftService'ten çıkarılan reader sınıfı.
/// </summary>
public static class KasaUstRaporReader
{
    /// <summary>
    /// KasaÜstRapor summary değerlerini tutan record.
    /// </summary>
    public record KasaUstSummary(
        decimal PosTahsilat,
        decimal OnlineTahsilat,
        decimal PostTahsilat,
        decimal Tahsilat,
        decimal Reddiyat,
        decimal PosHarc,
        decimal OnlineHarc,
        decimal PostHarc,
        decimal GelmeyenPost,
        decimal Harc,
        decimal GelirVergisi,
        decimal DamgaVergisi,
        decimal Stopaj);

    /// <summary>
    /// Genel snapshot'tan KasaÜstRapor satır toplamlarını okur.
    /// </summary>
    public static KasaUstSummary ReadSummary(KasaRaporSnapshot genSnap, List<string> issues)
    {
        try
        {
            var sum = genSnap.Rows.FirstOrDefault(r => r.IsSummaryRow);
            if (sum == null)
            {
                issues.Add("KasaÜstRapor snapshot'ında TOPLAMLAR satırı bulunamadı (IsSummaryRow=false). Tahsilat/Harç/Reddiyat/Stopaj hesapları 0 kabul edildi.");
                return CreateEmptySummary();
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(sum.ColumnsJson) ?? new();

            decimal ReadDec(string key)
            {
                if (TryGet(dict, key, out var val))
                {
                    System.IO.File.AppendAllText(@"H:\kasa_debug.txt", $"KEY={key} | RAW_VAL={val}\n");
                    if (DecimalParsingHelper.TryParseFromTurkish(val, out var d))
                    {
                        System.IO.File.AppendAllText(@"H:\kasa_debug.txt", $"  PARSED={d}\n");
                        return d;
                    }
                }
                return 0m;
            }

            string? FindKeyContains(params string[] tokens)
            {
                foreach (var k in dict.Keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var kk = k.Trim();
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
                var v = ReadDec(preferredKey);
                if (v != 0m) return v;

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
            if (harc == 0m)
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
            if (stopaj == 0m)
            {
                var kStopaj = FindKeyContains("stopaj");
                if (!string.IsNullOrWhiteSpace(kStopaj))
                    stopaj = ReadDec(kStopaj);
            }
            if (stopaj == 0m)
                stopaj = gelirVergisi + damgaVergisi;

            return new KasaUstSummary(
                PosTahsilat: posTahsilat,
                OnlineTahsilat: onlineTahsilat,
                PostTahsilat: postTahsilat,
                Tahsilat: tahsilat,
                Reddiyat: reddiyat,
                PosHarc: posHarc,
                OnlineHarc: onlineHarc,
                PostHarc: postHarc,
                GelmeyenPost: gelmeyenPost,
                Harc: harc,
                GelirVergisi: gelirVergisi,
                DamgaVergisi: damgaVergisi,
                Stopaj: stopaj);
        }
        catch (Exception ex)
        {
            issues.Add($"KasaÜstRapor TOPLAMLAR okuma hatası: {ex.Message}. Tahsilat/Harç/Reddiyat/Stopaj 0 kabul edildi.");
            return CreateEmptySummary();
        }
    }

    private static KasaUstSummary CreateEmptySummary() 
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static bool TryGet(Dictionary<string, string?> dict, string key, out string? value)
    {
        value = null;
        if (dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            value = v;
            return true;
        }
        // Case-insensitive fallback
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                value = kvp.Value;
                return true;
            }
        }
        return false;
    }
}
