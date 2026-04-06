using System;

namespace KasaManager.Domain.Calculation.Data;

/// <summary>
/// Kullanıcı tarafından hesap hesaplamalarına edilen manuel müdahale ve overridelar.
/// DailyFact verilerinin üstüne yazılır.
/// </summary>
public class DailyOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly ForDate { get; set; }
    public string CanonicalKey { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
