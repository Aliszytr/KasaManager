namespace KasaManager.Web.Models;

/// <summary>
/// R16: Sol panelde gösterilecek "Input Catalog" satırı.
///
/// - UnifiedPool (HAM) = gerçek input kaynakları (checkbox ile seçilebilir)
/// - Virtual = modelde tanımlı ama UnifiedPool'a asla girmemesi gereken
///   çıktı/taşıma/UI alanları (checkbox yok; sadece görünür)
/// </summary>
public sealed class KasaInputCatalogEntry
{
    public string Key { get; set; } = string.Empty;

    /// <summary>UnifiedPool'dan mı geliyor?</summary>
    public bool IsFromUnifiedPool { get; set; }

    /// <summary>Pool dışı sanal/model alanı mı?</summary>
    public bool IsVirtual { get; set; }

    /// <summary>Pool satırı varsa değer.</summary>
    public string? ValueText { get; set; }

    /// <summary>Kısa açıklama/kaynak.</summary>
    public string? Hint { get; set; }
}
