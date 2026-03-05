#nullable enable
using System.Text.RegularExpressions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 2: Formül tanımı - kullanıcı tarafından oluşturulan hesaplama.
/// Excel formülü benzeri yapı.
/// </summary>
public sealed partial record FormulaDefinition
{
    /// <summary>Benzersiz formül ID'si.</summary>
    public required Guid Id { get; init; }
    
    /// <summary>Hedef alan (formül sonucunun atanacağı key).</summary>
    public required string TargetKey { get; init; }
    
    /// <summary>Formül ifadesi (örn: "a + b - c * 2").</summary>
    public required string Expression { get; init; }
    
    /// <summary>Görüntüleme adı (UI için).</summary>
    public string? DisplayName { get; init; }
    
    /// <summary>Sıralama (hesaplama önceliği).</summary>
    public int SortOrder { get; init; }
    
    /// <summary>Gizli mi? (Soft delete için).</summary>
    public bool IsHidden { get; init; }
    
    /// <summary>Açıklama/notlar.</summary>
    public string? Notes { get; init; }
    
    /// <summary>
    /// Bağımlılıklar - formülde kullanılan diğer alanlar.
    /// Expression'dan otomatik çıkarılır.
    /// </summary>
    public IReadOnlySet<string> Dependencies => _dependencies ??= ExtractDependencies(Expression);
    private IReadOnlySet<string>? _dependencies;
    
    /// <summary>
    /// Expression'dan identifier'ları (alan adlarını) çıkarır.
    /// </summary>
    private static HashSet<string> ExtractDependencies(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Regex: letter ile başlayan, letter/digit/underscore devam eden identifier'lar
        var matches = IdentifierRegex().Matches(expression);
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (Match match in matches)
        {
            var id = match.Value;
            // Sayıları ve built-in fonksiyonları atla
            if (IsBuiltInFunction(id)) continue;
            deps.Add(KeyNormalizer.Normalize(id));
        }
        
        return deps;
    }
    
    private static bool IsBuiltInFunction(string id)
    {
        return id.Equals("max", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("min", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("abs", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("round", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("floor", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("ceiling", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("sum", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("avg", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("if", StringComparison.OrdinalIgnoreCase);
    }
    
    [GeneratedRegex(@"[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
}
