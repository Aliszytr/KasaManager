#nullable enable
using System.Text.RegularExpressions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 1: Key Normalizer - tüm key'leri standart formata dönüştürür.
/// Standart format: snake_case, küçük harf, boşluk yok.
/// </summary>
public static partial class KeyNormalizer
{
    /// <summary>
    /// Key'i standart formata dönüştürür.
    /// Örnek: "BankaTahsilat" -> "banka_tahsilat"
    /// Örnek: "TOPLAM_HARC" -> "toplam_harc"
    /// </summary>
    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;
        
        // 1. Trim ve lowercase
        var result = key.Trim();
        
        // 2. PascalCase/camelCase -> snake_case
        result = PascalToSnakeRegex().Replace(result, "$1_$2");
        
        // 3. Birden fazla underscore'u tek underscore'a indir
        result = MultiUnderscoreRegex().Replace(result, "_");
        
        // 4. Başta/sonda underscore varsa kaldır
        result = result.Trim('_');
        
        // 5. Lowercase
        return result.ToLowerInvariant();
    }
    
    /// <summary>
    /// İki key'in normalize edilmiş hallerinin eşit olup olmadığını kontrol eder.
    /// </summary>
    public static bool KeysEqual(string? key1, string? key2)
    {
        return string.Equals(Normalize(key1), Normalize(key2), StringComparison.Ordinal);
    }
    
    /// <summary>
    /// Key lookup için normalize edilmiş key üzerinden karşılaştırıcı.
    /// </summary>
    public static readonly IEqualityComparer<string> NormalizedComparer = new NormalizedKeyComparer();
    
    // Regex patterns (Source Generated for performance)
    [GeneratedRegex(@"([a-z])([A-Z])", RegexOptions.Compiled)]
    private static partial Regex PascalToSnakeRegex();
    
    [GeneratedRegex(@"_+", RegexOptions.Compiled)]
    private static partial Regex MultiUnderscoreRegex();
    
    private sealed class NormalizedKeyComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => KeysEqual(x, y);
        public int GetHashCode(string obj) => Normalize(obj).GetHashCode(StringComparison.Ordinal);
    }
}
