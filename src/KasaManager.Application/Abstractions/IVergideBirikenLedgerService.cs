#nullable enable
namespace KasaManager.Application.Abstractions;

/// <summary>
/// Vergide Biriken akıllı ledger servisi.
/// Kaydedilmiş Sabah/Aksam snapshot'larından running total hesaplayarak
/// doğru VergideBiriken değerini belirler.
/// 
/// Formül: VergideBiriken = InitialSeed + Σ(VergiKasa) − Σ(VergidenGelen)
/// (tüm aktif snapshot'lar, RaporTarihi ≤ upToDate)
/// </summary>
public interface IVergideBirikenLedgerService
{
    /// <summary>
    /// Belirtilen tarihe kadar VergideBiriken running total'ını hesaplar.
    /// <para>
    /// Sabah Kasa: upToDate-1'e kadar hesaplar (bugünü dahil etmez, mükerrer önleme).
    /// Aksam Kasa: upToDate'e kadar hesaplar (bugün dahil).
    /// </para>
    /// </summary>
    /// <param name="upToDate">Hesaplama yapılan kasa tarihi</param>
    /// <param name="kasaType">Kasa türü: "Sabah", "Aksam" veya "Genel"</param>
    Task<VergideBirikenResult> CalculateAsync(DateOnly upToDate, string kasaType, CancellationToken ct = default);
}

/// <summary>
/// Vergide Biriken hesaplama sonucu — breakdown bilgisiyle.
/// </summary>
public sealed record VergideBirikenResult(
    /// <summary>İlk seed değeri (Genel Kasa'dan aktarılan)</summary>
    decimal InitialSeed,
    
    /// <summary>Tüm snapshot'lardaki VergiKasa toplamı (kasaya giren)</summary>
    decimal TotalVergiKasa,
    
    /// <summary>Tüm snapshot'lardaki VergidenGelen toplamı (kasadan çıkan)</summary>
    decimal TotalVergidenGelen,
    
    /// <summary>Net VergideBiriken = InitialSeed + TotalVergiKasa − TotalVergidenGelen</summary>
    decimal VergideBiriken,
    
    /// <summary>Hesaplamaya dahil edilen snapshot sayısı</summary>
    int SnapshotCount,
    
    /// <summary>Son snapshot tarihi (null ise hiç snapshot yok)</summary>
    DateOnly? LastSnapshotDate
);
