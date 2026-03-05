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
    /// String değeri decimal'e dönüştürmeyi dener.
    /// Türkçe ve İngilizce formatları destekler.
    /// </summary>
    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim();

        // Önce TR culture (1.234,56)
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value))
            return true;

        // Sonra invariant (1,234.56)
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;

        return false;
    }

    /// <summary>
    /// Borç/Alacak durumuna göre işaret uygular.
    /// Borç = negatif, Alacak = pozitif.
    /// </summary>
    public static decimal ApplyDebitCreditSign(decimal amount, string? borcAlacak)
    {
        if (string.IsNullOrWhiteSpace(borcAlacak)) return amount;

        var normalized = borcAlacak.Trim().ToUpperInvariant();

        if (normalized.Contains("BORÇ") || normalized.Contains("BORC"))
            return -Math.Abs(amount);

        if (normalized.Contains("ALACAK"))
            return Math.Abs(amount);

        return amount;
    }

    /// <summary>
    /// Decimal değeri formatlanmış string'e çevirir.
    /// </summary>
    public static string FormatDecimal(decimal value, string format = "N2")
        => value.ToString(format, CultureInfo.InvariantCulture);
}
