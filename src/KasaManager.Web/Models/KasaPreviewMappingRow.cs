namespace KasaManager.Web.Models;

/// <summary>
/// R16-UI: Preview ekranında "rapor tasarımı" için mapping satırı.
/// - Map: Tek bir HAM input'u doğrudan hedef output'a taşır
/// - Formula: Expression ile üretir
///
/// Not: Bu model DB'ye yazılmaz. CRUD fazında CalculationRun + Mapping snapshot'a taşınacaktır.
/// </summary>
public sealed class KasaPreviewMappingRow
{
    /// <summary>
    /// UI tarafında satırı stabil tutmak için (JS add/remove).
    /// </summary>
    public string RowId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Üretilecek canonical output key.
    /// Örn: aksam.bankaya_yatirilacak_tahsilat veya bankaya_yatirilacak_tahsilat
    /// (Bu fazda serbest; öneriler datalist ile sunulur.)
    /// </summary>
    public string? TargetKey { get; set; }

    /// <summary>
    /// "Map" veya "Formula".
    /// </summary>
    public string Mode { get; set; } = "Formula";

    /// <summary>
    /// Mode=Map ise kullanılacak input key.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>
    /// Mode=Formula ise expression.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// UI'da "Sil" ile satırı gizlemek için. Binder index bozulmasın diye satır silmek yerine hide edilir.
    /// </summary>
    public bool IsHidden { get; set; }
}
