#nullable enable
using System.Globalization;

namespace KasaManager.Application.Services.Draft.Helpers;

/// <summary>
/// Decimal parse ve dönüşüm işlemleri için yardımcı sınıf.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public static class DecimalParsingHelper
{
    /// <summary>
    /// JSON/DB kaynaklı veriler için (nokta = ondalık ayracı)
    /// Kullanım: CarryoverResolver, DB ResultsJson okuma, API response parse
    /// </summary>
    public static bool TryParseFromJson(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        
        // raw = raw.Trim().Replace("₺", "").Replace(" ", ""); // Eğer özel temizlik gerekirse (şu an devir zinciri bunu kendi yapıyordu)
        // Ancak Invariant zaten standart JSON formatını okur
        var s = raw.Trim().Replace("₺", "").Replace("TL", "", StringComparison.OrdinalIgnoreCase).Trim();
        s = s.Replace("\u00A0", " ").Replace(" ", "");
        
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        
        return false;
    }

    /// <summary>
    /// Excel/UI kaynaklı veriler için (Türkçe format: virgül = ondalık ayracı)
    /// Kullanım: Excel okuma, HesapKontrol, Banka karşılaştırma, UI form parse
    /// </summary>
    public static bool TryParseFromTurkish(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim().Replace("₺", "").Replace("TL", "", StringComparison.OrdinalIgnoreCase).Trim();
        s = s.Replace("\u00A0", " ").Replace(" ", "");

        // Önce TR culture (1.234,56 veya 13.668,90)
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value))
            return true;

        // Sonra invariant (1234.56 veya 13668.90 DB json formatı)
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;

        return false;
    }

    /// <summary>
    /// Eski TryParseDecimal — geriye uyumluluk için (tr-TR öncelikli, eski davranış)
    /// </summary>
    [Obsolete("TryParseFromJson veya TryParseFromTurkish kullanın")]
    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        return TryParseFromTurkish(raw, out value);
    }

    /// <summary>
    /// Borç/Alacak durumuna göre işaret uygular.
    /// Borç = negatif, Alacak = pozitif.
    /// </summary>
    public static decimal ApplyDebitCreditSign(decimal amount, string? borcAlacak)
    {
        if (string.IsNullOrWhiteSpace(borcAlacak)) return amount;

        var normalized = borcAlacak.Trim().ToUpperInvariant();

        if (normalized.Contains("BORÇ") || normalized.Contains("BORC") || normalized == "B")
            return -Math.Abs(amount);

        if (normalized.Contains("ALACAK") || normalized == "A")
            return Math.Abs(amount);

        return amount;
    }

    /// <summary>
    /// Decimal değeri formatlanmış string'e çevirir.
    /// </summary>
    public static string FormatDecimal(decimal value, string format = "N2")
        => value.ToString(format, CultureInfo.InvariantCulture);
}
