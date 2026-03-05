#nullable enable
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Comparison;

// ─────────────────────────────────────────────────────────────
// ComparisonService — Helpers (Normalizasyon, Tespit, Araçlar)
// ─────────────────────────────────────────────────────────────
public sealed partial class ComparisonService
{
    /// <summary>
    /// Tutar eşleşmesini kontrol eder (toleranslı).
    /// </summary>
    private static bool IsAmountMatch(decimal online, decimal banka)
    {
        if (online == 0 || banka == 0) return false;
        var diff = Math.Abs(online - banka);
        var tolerance = online * AmountTolerancePercent;
        return diff <= tolerance || diff <= 0.01m;
    }

    /// <summary>
    /// Alacak (+) kaydı mı kontrol eder.
    /// </summary>
    private static bool IsAlacak(decimal tutar, string? borcAlacak)
    {
        if (tutar > 0)
        {
            if (string.IsNullOrEmpty(borcAlacak)) return true;
            var upper = borcAlacak.Trim().ToUpperInvariant();
            if (upper.Contains("BORÇ") || upper.Contains("BORC") || upper == "-") return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Dosya numarasını normalize eder.
    /// </summary>
    private static string? NormalizeDosyaNo(string? dosyaNo)
    {
        if (string.IsNullOrWhiteSpace(dosyaNo)) return null;
        return dosyaNo.Trim().Replace(" ", "").Replace("-", "/");
    }

    /// <summary>
    /// Birim adını normalize eder (karşılaştırma için).
    /// </summary>
    private static string NormalizeBirimAdi(string birim)
    {
        return birim
            .ToLowerInvariant()
            .Replace("ı", "i").Replace("ğ", "g").Replace("ü", "u")
            .Replace("ş", "s").Replace("ö", "o").Replace("ç", "c")
            .Replace(".", " ").Replace("-", " ").Replace("  ", " ")
            .Trim();
    }

    /// <summary>
    /// Mahkeme numarasını çıkarır.
    /// </summary>
    private static string? ExtractMahkemeNo(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{1,2})\s*\.?\s*(İdare|Vergi|İcra|Asliye|Hukuk|Ceza)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Mahkeme türünü çıkarır.
    /// </summary>
    private static string? ExtractMahkemeTuru(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\d{1,2}\s*\.?\s*(İdare|Vergi|İcra|Asliye|Hukuk|Ceza)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Dosya yolunu çözümler.
    /// </summary>
    private static string? ResolveFile(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) return path;
        if (Directory.Exists(folder))
        {
            var files = Directory.GetFiles(folder);
            var match = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }
    
    /// <summary>
    /// Banka kaydının türünü tespit eder (MASRAF, HARÇ, vb.)
    /// </summary>
    private static string DetectRecordType(string? aciklama, ComparisonType type)
    {
        if (string.IsNullOrWhiteSpace(aciklama))
            return type == ComparisonType.TahsilatMasraf ? "MASRAF" : "HARÇ";
        
        var upper = aciklama.ToUpperInvariant();
        
        if (upper.Contains("EFT OTOMATIK") || upper.Contains("EFT OTOMATİK")
            || upper.Contains("GERI ODEME") || upper.Contains("GERİ ÖDEME")
            || upper.Contains("IADE") || upper.Contains("İADE"))
            return "EFT_OTOMATIK_IADE";
        if (upper.Contains("GELEN HAVALE") || upper.Contains("HAVALE GELİŞ") || upper.Contains("HAVALE GELIS"))
            return "GELEN_HAVALE";
        if (upper.Contains("MEVDUATA PARA") || upper.Contains("MEVDUAT YATIR"))
            return "MEVDUAT_YATIRMA";
        if (upper.Contains("VIRMAN") || upper.Contains("VİRMAN"))
            return "VIRMAN";
        if (upper.Contains("HARÇ") || upper.Contains("HARC"))
            return "HARÇ";
        if (upper.Contains("MASRAF"))
            return "MASRAF";
        if (upper.Contains("PARAM EP"))
            return "PARAM EP";
        if (upper.Contains("PORTAL"))
            return "PORTAL";
        if (upper.Contains("BAROBİRLİK") || upper.Contains("BAROBIRLIK"))
            return "BAROBİRLİK";
        return "BILINMEYEN";
    }
    
    /// <summary>
    /// Tutar dengesi için kullanıcı dostu özet mesaj oluşturur.
    /// </summary>
    private static string CreateBalanceSummary(
        decimal totalOnline, decimal totalBanka,
        int surplusCount, decimal surplusAmount,
        int missingCount, decimal missingAmount)
    {
        var messages = new List<string>();
        if (surplusCount == 0 && missingCount == 0)
        {
            messages.Add("✅ Tüm kayıtlar eşleşti, tutar dengesi sağlandı.");
        }
        else
        {
            if (surplusCount > 0)
                messages.Add($"⚠️ Banka dosyasında {surplusCount} adet fazla kayıt tespit edildi (Toplam: {surplusAmount:N2} ₺). " +
                            "Bu kayıtların online sistemde karşılığı bulunamamıştır, manuel giriş olabilir.");
            if (missingCount > 0)
                messages.Add($"⚠️ Online dosyada {missingCount} adet kayıt için banka karşılığı bulunamadı (Toplam: {missingAmount:N2} ₺). " +
                            "Bu kayıtların bedeli henüz banka hesabına yatmamış olabilir.");
        }
        var netDiff = totalBanka - totalOnline;
        if (Math.Abs(netDiff) > 0.01m)
        {
            messages.Add(netDiff > 0
                ? $"📊 Net tutar farkı: Bankada +{netDiff:N2} ₺ fazla."
                : $"📊 Net tutar farkı: Bankada {netDiff:N2} ₺ eksik.");
            if (surplusCount > 0 && missingCount > 0 && Math.Abs(surplusAmount - missingAmount) < 1m)
                messages.Add("💡 Dikkat: Fazla ve eksik tutarlar birbirine yakın ancak farklı kayıtlar söz konusu. Detaylı inceleme önerilir.");
        }
        else
        {
            messages.Add("📊 Toplam tutarlar dengeli.");
        }
        return string.Join("\n\n", messages);
    }

    /// <summary>
    /// Mahkeme adını normalize eder.
    /// "Ankara 5. Vergi Mahkemesi" -> "ankara5vergimahkemesi"
    /// </summary>
    private static string NormalizeMahkeme(string? mahkeme)
    {
        if (string.IsNullOrWhiteSpace(mahkeme)) return "";
        return mahkeme
            .ToLowerInvariant().Replace(" ", "").Replace(".", "")
            .Replace("ı", "i").Replace("ğ", "g").Replace("ü", "u")
            .Replace("ş", "s").Replace("ö", "o").Replace("ç", "c")
            .Trim();
    }
}
