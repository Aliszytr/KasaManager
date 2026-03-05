namespace KasaManager.Domain.Constants;

/// <summary>
/// Kasa scope türleri için merkezi sabitler.
/// Magic string kullanımını ortadan kaldırır.
/// </summary>
public static class KasaScopeTypes
{
    public const string Aksam = "Aksam";
    public const string Sabah = "Sabah";
    public const string Genel = "Genel";
    public const string Ortak = "Ortak";
    public const string Custom = "Custom";

    /// <summary>
    /// Verilen scope türünün geçerli olup olmadığını kontrol eder.
    /// </summary>
    public static bool IsValid(string? scopeType) =>
        !string.IsNullOrWhiteSpace(scopeType) &&
        (scopeType.Equals(Aksam, StringComparison.OrdinalIgnoreCase) ||
         scopeType.Equals(Sabah, StringComparison.OrdinalIgnoreCase) ||
         scopeType.Equals(Genel, StringComparison.OrdinalIgnoreCase) ||
         scopeType.Equals(Ortak, StringComparison.OrdinalIgnoreCase) ||
         scopeType.Equals(Custom, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Scope türünü normalize eder (case-insensitive → PascalCase).
    /// Bilinmeyen değerlerde Custom döner.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Custom;

        if (raw.Equals(Aksam, StringComparison.OrdinalIgnoreCase)) return Aksam;
        if (raw.Equals(Sabah, StringComparison.OrdinalIgnoreCase)) return Sabah;
        if (raw.Equals(Genel, StringComparison.OrdinalIgnoreCase)) return Genel;
        if (raw.Equals(Ortak, StringComparison.OrdinalIgnoreCase)) return Ortak;
        if (raw.Equals(Custom, StringComparison.OrdinalIgnoreCase)) return Custom;

        // GenelKasa, AksamKasa gibi eski değerler
        if (raw.IndexOf("genel", StringComparison.OrdinalIgnoreCase) >= 0) return Genel;
        if (raw.IndexOf("aksam", StringComparison.OrdinalIgnoreCase) >= 0) return Aksam;
        if (raw.IndexOf("sabah", StringComparison.OrdinalIgnoreCase) >= 0) return Sabah;

        return Custom;
    }
}
