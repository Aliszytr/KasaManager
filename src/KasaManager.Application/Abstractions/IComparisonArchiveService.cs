#nullable enable
namespace KasaManager.Application.Abstractions;

/// <summary>
/// Karşılaştırma dosyalarını tarih bazlı arşivleme servisi.
/// Yüklenen Excel dosyalarının kopyalarını tarih damgalı klasörlerde saklar.
/// </summary>
public interface IComparisonArchiveService
{
    /// <summary>
    /// Karşılaştırma dosyalarını archive alt klasörüne kopyalar.
    /// reportDate verilmişse rapor tarihi, verilmemişse bugünün tarihiyle bir snapshot oluşturur.
    /// </summary>
    void ArchiveComparisonFiles(string uploadFolder, DateOnly? reportDate = null);

    /// <summary>
    /// Mevcut arşiv tarihlerini döndürür (en yeniden eskiye).
    /// </summary>
    List<DateOnly> GetAvailableArchiveDates(string uploadFolder);

    /// <summary>
    /// Belirli bir arşiv tarihinin klasör yolunu döndürür.
    /// Klasör mevcut değilse null döner.
    /// </summary>
    string? GetArchiveFolder(string uploadFolder, DateOnly date);

    /// <summary>
    /// Retention süresini aşmış arşivleri siler.
    /// </summary>
    int CleanupOldArchives(string uploadFolder, int retentionDays = 60);
}
