namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// Deklaratif formül şablonu.
/// Ham veriye dokunmaz; yalnızca okur ve çıktı üretir.
/// </summary>
public sealed class FormulaTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Üretilecek canonical key.
    /// Örn: bankaya_yatirilacak_tahsilat
    /// </summary>
    public string TargetKey { get; init; } = string.Empty;

    /// <summary>
    /// Örn: (toplam_tahsilat - online_tahsilat) + bankaya_yatirilacak_tahsilati_degistir
    /// </summary>
    public string Expression { get; init; } = string.Empty;

    public FormulaCategory Category { get; init; } = FormulaCategory.Unknown;

    /// <summary>
    /// Bu formül hangi kasa türüne uygulanır.
    /// </summary>
    public AppliesToKasa AppliesTo { get; init; } = AppliesToKasa.Any;

    /// <summary>
    /// Versiyon bilgisi (semver veya serbest).
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// İsteğe bağlı açıklama.
    /// </summary>
    public string? Notes { get; init; }
}
