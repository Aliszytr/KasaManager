#nullable enable
using KasaManager.Domain.Reports;

namespace KasaManager.Domain.Models;

/// <summary>
/// REFACTOR R1: Unified Kasa data record.
/// 
/// Tek bir model ile tüm kasa tiplerini (Sabah, Aksam, Genel) temsil eder.
/// Legacy modeller (SabahKasaNesnesi, AksamKasaNesnesi, GenelKasaRaporNesnesi) 
/// bu modele dönüştürülebilir veya bu modelden oluşturulabilir.
/// 
/// Avantajlar:
/// - Tek veri yapısı, birden fazla repository değil
/// - Canonical key bazlı esnek alan erişimi
/// - Yeni alan eklemek için model değişikliği gerekmez
/// </summary>
public sealed class UnifiedKasaRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Rapor/işlem tarihi.
    /// </summary>
    public DateOnly RaporTarihi { get; set; }
    
    /// <summary>
    /// Kasa türü: Sabah, Aksam veya Genel.
    /// </summary>
    public KasaRaporTuru RaporTuru { get; set; }
    
    /// <summary>
    /// Oluşturulma zamanı (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Oluşturan kullanıcı.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Decimal alanlar (monetary values).
    /// Key: Canonical key (KasaCanonicalKeys'den).
    /// Value: decimal değer.
    /// </summary>
    public Dictionary<string, decimal> DecimalFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// String alanlar (metadata, açıklamalar).
    /// Key: Canonical key.
    /// Value: string değer.
    /// </summary>
    public Dictionary<string, string?> TextFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DateTime alanlar (tarihler).
    /// Key: Canonical key.
    /// Value: DateTime değer.
    /// </summary>
    public Dictionary<string, DateTime> DateFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Integer alanlar (sayıcılar, işlem sayıları).
    /// Key: Canonical key.
    /// Value: int değer.
    /// </summary>
    public Dictionary<string, int> IntFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    #region Helper Methods

    /// <summary>
    /// Decimal alan değerini güvenli şekilde al.
    /// </summary>
    public decimal GetDecimal(string canonicalKey, decimal defaultValue = 0m)
        => DecimalFields.TryGetValue(canonicalKey, out var value) ? value : defaultValue;

    /// <summary>
    /// Decimal alan değerini set et.
    /// </summary>
    public void SetDecimal(string canonicalKey, decimal value)
        => DecimalFields[canonicalKey] = value;

    /// <summary>
    /// String alan değerini güvenli şekilde al.
    /// </summary>
    public string? GetText(string canonicalKey)
        => TextFields.TryGetValue(canonicalKey, out var value) ? value : null;

    /// <summary>
    /// String alan değerini set et.
    /// </summary>
    public void SetText(string canonicalKey, string? value)
        => TextFields[canonicalKey] = value;

    /// <summary>
    /// DateTime alan değerini güvenli şekilde al.
    /// </summary>
    public DateTime? GetDate(string canonicalKey)
        => DateFields.TryGetValue(canonicalKey, out var value) ? value : null;

    /// <summary>
    /// DateTime alan değerini set et.
    /// </summary>
    public void SetDate(string canonicalKey, DateTime value)
        => DateFields[canonicalKey] = value;

    /// <summary>
    /// Int alan değerini güvenli şekilde al.
    /// </summary>
    public int GetInt(string canonicalKey, int defaultValue = 0)
        => IntFields.TryGetValue(canonicalKey, out var value) ? value : defaultValue;

    /// <summary>
    /// Int alan değerini set et.
    /// </summary>
    public void SetInt(string canonicalKey, int value)
        => IntFields[canonicalKey] = value;

    /// <summary>
    /// Belirtilen canonical key'in herhangi bir sözlükte olup olmadığını kontrol et.
    /// </summary>
    public bool HasField(string canonicalKey)
        => DecimalFields.ContainsKey(canonicalKey) 
           || TextFields.ContainsKey(canonicalKey) 
           || DateFields.ContainsKey(canonicalKey)
           || IntFields.ContainsKey(canonicalKey);

    /// <summary>
    /// Tüm decimal alanları kopyala (clone için).
    /// </summary>
    public UnifiedKasaRecord Clone()
    {
        return new UnifiedKasaRecord
        {
            Id = Guid.NewGuid(),
            RaporTarihi = RaporTarihi,
            RaporTuru = RaporTuru,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = CreatedBy,
            DecimalFields = new Dictionary<string, decimal>(DecimalFields, StringComparer.OrdinalIgnoreCase),
            TextFields = new Dictionary<string, string?>(TextFields, StringComparer.OrdinalIgnoreCase),
            DateFields = new Dictionary<string, DateTime>(DateFields, StringComparer.OrdinalIgnoreCase),
            IntFields = new Dictionary<string, int>(IntFields, StringComparer.OrdinalIgnoreCase)
        };
    }

    #endregion
}
