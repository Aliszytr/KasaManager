using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R6: Snapshot tabanlı kasa raporu kaydetme/okuma servisi.
/// Import/Preview aşamalarında DB'ye yazılmaz; sadece "Kaydet" ile yazılır.
/// </summary>
public interface IKasaRaporSnapshotService
{
    Task<KasaRaporSnapshot> SaveAsync(KasaRaporSnapshot snapshot, CancellationToken ct = default);

    Task<KasaRaporSnapshot?> GetAsync(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct = default);

    /// <summary>
    /// R19: Snapshot yüklerken eksik alanları varsayılan değerlerle doldurur.
    /// Eski snapshot'ların yeni alan yapısıyla uyumlu çalışmasını sağlar.
    /// </summary>
    Task<KasaRaporSnapshot?> GetWithMissingFieldsAsync(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct = default);

    Task<KasaRaporSnapshot?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// R7: Tarih sürekliliği kontrolü için ilgili rapor türünün DB'deki en son snapshot tarihini döndürür.
    /// </summary>
    Task<DateOnly?> GetLastSnapshotDateAsync(KasaRaporTuru raporTuru, CancellationToken ct = default);


    /// <summary>
    /// Verilen tarihten (dahil) önceki en son snapshot'ı döndürür.
    /// (Örn: Genel Kasa devreden hesabı için)
    /// </summary>
    Task<KasaRaporSnapshot?> GetLastBeforeOrOnAsync(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct = default);

    /// <summary>
    /// R12.5: "Genel Kasa" için, yalnızca Results içeren ve sonuç JSON'unda "Genel Kasa" alanı bulunan
    /// snapshot'ı hedefler. Bu, KasaÜstRapor (Genel) snapshot'ları ile karışmayı engeller.
    ///
    /// Not: RaporTuru=Genel olarak kaydedilmiş snapshot'lar içinde, sadece "hesaplanmış" olanları seçer.
    /// </summary>
    Task<KasaRaporSnapshot?> GetLastGenelKasaSnapshotBeforeOrOnAsync(DateOnly raporTarihi, CancellationToken ct = default);

    /// <summary>
    /// Verilen rapor türü için DB'deki tüm benzersiz snapshot tarihlerini azalan sırada döndürür.
    /// (Hesap Kontrol tarih seçici dropdown'u için)
    /// </summary>
    Task<List<DateOnly>> GetAllSnapshotDatesAsync(KasaRaporTuru raporTuru, CancellationToken ct = default);

}