using System;
using System.Text.RegularExpressions;

namespace KasaManager.Application.Processing.Narratives;

/// <summary>
/// R3/R4: Preview doğrulama amacıyla açıklama metninden
/// (mahkeme birimi, dosya no vb.) çıkarım yapan saf parser.
/// IO/DB bağı yoktur.
///
/// Not: Bu parser Matching'e geçmeden önce "gözle doğrulama" katmanıdır.
/// İleride daha güçlü bir parser'a evrilebilir.
/// </summary>
public static class BankNarrativeParser
{
    private static readonly Regex FileNoRx = new(@"\b(20\d{2})\s*/\s*(\d{1,6})\b", RegexOptions.Compiled);

    public static NarrativeParseResult Parse(string input)
    {
        input ??= string.Empty;
        var raw = input.Trim();

        // SelectedSegment: çok uzun açıklamalarda ilk 200 char preview için yeterli
        var selected = raw.Length <= 200 ? raw : raw.Substring(0, 200);

        // Normalize: küçük harf + whitespace normalize
        var normalized = NormalizeText(raw);

        // CourtUnit: "Mahkemesi" kelimesini içeren parçayı yakalamaya çalış
        var court = ExtractCourtUnit(raw);

        // FileNo: 2025/1883 gibi pattern
        var fileNo = ExtractFileNo(raw);

        var result = new NarrativeParseResult
        {
            SelectedSegment = selected,
            Normalized = normalized,
            NormalizedCourtUnit = court,
            FileNo = fileNo
        };

        if (string.IsNullOrWhiteSpace(raw))
            result.Issues.Add(new NarrativeIssue("Açıklama boş"));

        if (string.IsNullOrWhiteSpace(fileNo))
            result.Issues.Add(new NarrativeIssue("Dosya no bulunamadı"));

        return result;
    }

    private static string NormalizeText(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        // TR I/İ normalizasyonu (çok agresif olmadan)
        var t = s.Replace('İ', 'I').Replace('ı', 'i');
        t = t.ToLowerInvariant();
        t = Regex.Replace(t, @"\s+", " ");
        return t.Trim();
    }

    private static string ExtractFileNo(string s)
    {
        var m = FileNoRx.Match(s ?? string.Empty);
        if (!m.Success) return string.Empty;
        return $"{m.Groups[1].Value}/{m.Groups[2].Value}";
    }

    private static string ExtractCourtUnit(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        // "Mahkemesi" geçen kısmı kaba yakalama
        var idx = s.IndexOf("Mahkemesi", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = s.IndexOf("Mahk.", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        // Geriye doğru 60 karakter, ileriye doğru 20 karakter al
        var start = Math.Max(0, idx - 60);
        var len = Math.Min(s.Length - start, 80);
        var piece = s.Substring(start, len);

        // Temizle
        piece = Regex.Replace(piece, @"\s+", " ").Trim();
        return piece;
    }
}
