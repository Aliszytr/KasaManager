#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Hesap Kontrol sonuçlarını PDF'e dönüştüren servis arayüzü.
/// </summary>
public interface IHesapKontrolExportService
{
    /// <summary>
    /// Dashboard özet verisi, açık kayıtlar ve geçmiş kayıtları
    /// tek bir PDF belgesine dönüştürür.
    /// </summary>
    /// <param name="dashboard">Dashboard özet kartı.</param>
    /// <param name="acikKayitlar">Açık durumdaki kayıtlar.</param>
    /// <param name="gecmisKayitlar">Geçmiş (filtrelenmiş) kayıtlar.</param>
    /// <param name="raporTarihi">Raporun kapak tarihi.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PDF dosya byte dizisi.</returns>
    Task<byte[]> ExportToPdfAsync(
        HesapKontrolDashboard dashboard,
        List<HesapKontrolKaydi> acikKayitlar,
        List<HesapKontrolKaydi> gecmisKayitlar,
        DateOnly raporTarihi,
        CancellationToken ct = default);
}
