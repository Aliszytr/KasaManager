#nullable enable
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Banka ve Online dosyalar arasında karşılaştırma servisi.
/// </summary>
public interface IComparisonService
{
    /// <summary>
    /// BankaTahsilat.xlsx ile onlineMasraf.xlsx dosyalarını karşılaştırır.
    /// Sadece Banka'ya giren (+/Alacak) kayıtlar karşılaştırılır.
    /// </summary>
    /// <param name="uploadFolder">Excel dosyalarının bulunduğu klasör</param>
    /// <param name="filterDate">Opsiyonel tarih filtresi</param>
    /// <param name="ct">İptal token'ı</param>
    /// <returns>Karşılaştırma raporu</returns>
    Task<Result<ComparisonReport>> CompareTahsilatMasrafAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// BankaHarc.xlsx ile onlineHarc.xlsx dosyalarını karşılaştırır.
    /// Sadece Banka'ya giren (+/Alacak) kayıtlar karşılaştırılır.
    /// </summary>
    /// <param name="uploadFolder">Excel dosyalarının bulunduğu klasör</param>
    /// <param name="filterDate">Opsiyonel tarih filtresi</param>
    /// <param name="ct">İptal token'ı</param>
    /// <returns>Karşılaştırma raporu</returns>
    Task<Result<ComparisonReport>> CompareHarcamaHarcAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// BankaTahsilat.xlsx (-/Borç) ile onlineReddiyat.xlsx dosyalarını karşılaştırır.
    /// Bankadan çıkan ödemeleri online reddiyat kayıtlarıyla eşleştirir.
    /// Stopaj (Gelir Vergisi + Damga Vergisi) hesaplaması yapar.
    /// </summary>
    /// <param name="uploadFolder">Excel dosyalarının bulunduğu klasör</param>
    /// <param name="filterDate">Opsiyonel tarih filtresi</param>
    /// <param name="ct">İptal token'ı</param>
    /// <returns>Karşılaştırma raporu (Stopaj bilgileri dahil)</returns>
    Task<Result<ComparisonReport>> CompareReddiyatCikisAsync(
        string uploadFolder,
        DateOnly? filterDate = null,
        CancellationToken ct = default);
}
