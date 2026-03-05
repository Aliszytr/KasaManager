#nullable enable
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R17: Hesaplanmış Kasa snapshot'ları yönetimi.
/// Preview'da hesaplanan sonuçları VT'ye kaydeder/getirir.
/// Versioning ve soft delete ile audit trail sağlar.
/// </summary>
public interface ICalculatedKasaSnapshotService
{
    /// <summary>
    /// Yeni hesaplanmış kasayı kaydet.
    /// Eğer aynı tarih+kasa için aktif kayıt varsa, onu pasif yapar ve yeni versiyon oluşturur.
    /// </summary>
    Task<CalculatedKasaSnapshot> SaveAsync(CalculatedKasaSnapshot snapshot, CancellationToken ct = default);
    
    /// <summary>Tarih + Kasa Tipi ile aktif kaydı getir</summary>
    Task<CalculatedKasaSnapshot?> GetActiveAsync(DateOnly raporTarihi, KasaRaporTuru kasaTuru, CancellationToken ct = default);
    
    /// <summary>ID ile getir</summary>
    Task<CalculatedKasaSnapshot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Tüm versiyonları getir (audit trail).
    /// En yeni önce sıralı döner.
    /// </summary>
    Task<List<CalculatedKasaSnapshot>> GetAllVersionsAsync(DateOnly raporTarihi, KasaRaporTuru kasaTuru, CancellationToken ct = default);
    
    /// <summary>
    /// Soft delete.
    /// Kayıt silinmez, IsDeleted=true, DeletedAtUtc ve DeletedBy set edilir.
    /// </summary>
    Task DeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    
    /// <summary>
    /// Son N günün kasalarını listele.
    /// Sadece aktif kayıtları döner.
    /// </summary>
    Task<List<CalculatedKasaSnapshot>> ListRecentAsync(KasaRaporTuru? kasaTuru = null, int days = 30, CancellationToken ct = default);
    
    /// <summary>
    /// Tarih aralığında kasaları listele.
    /// Sadece aktif kayıtları döner.
    /// </summary>
    Task<List<CalculatedKasaSnapshot>> ListByDateRangeAsync(
        DateOnly startDate, 
        DateOnly endDate, 
        KasaRaporTuru? kasaTuru = null, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Belirli bir kasayı aktif yap (diğer versiyonları pasif yapar).
    /// Yanlışlıkla kayıt yapıldığında eski versiyona dönmek için.
    /// </summary>
    Task ActivateVersionAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Gelişmiş arama: İsim, açıklama, notlarda full-text + tarih aralığı + kasa tipi filtresi.
    /// Sayfalama ve sıralama destekli.
    /// </summary>
    Task<PagedResult<CalculatedKasaSnapshot>> SearchAsync(KasaReportSearchQuery query, CancellationToken ct = default);
    
    /// <summary>
    /// Soft delete geri alma. IsDeleted=false yapılır, IsActive olarak ayarlanır.
    /// </summary>
    Task RestoreAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Rapor adı, açıklama ve notlarını günceller.
    /// </summary>
    Task UpdateAsync(Guid id, string? name, string? description, string? notes, CancellationToken ct = default);
    
    /// <summary>
    /// Aktif (silinmemiş) rapor sayısını döner.
    /// </summary>
    Task<int> CountAsync(KasaRaporTuru? kasaTuru = null, CancellationToken ct = default);
}
