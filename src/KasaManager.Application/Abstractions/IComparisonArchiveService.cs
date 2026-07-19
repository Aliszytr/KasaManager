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

public sealed record HesapKontrolSourceResolution(
    string? Folder,
    string? Error,
    string? TechnicalError = null,
    HesapKontrolSourceKind SourceKind = HesapKontrolSourceKind.Unknown)
{
    public bool IsValid => Folder is not null && Error is null;

    public static HesapKontrolSourceResolution Success(
        string folder,
        HesapKontrolSourceKind sourceKind = HesapKontrolSourceKind.Unknown) =>
        new(folder, null, null, sourceKind);
    public static HesapKontrolSourceResolution Fail(string error, string? technicalError = null) =>
        new(null, error, technicalError);
}

public enum HesapKontrolSourceKind
{
    Unknown = 0,
    Archive = 1,
    Current = 2
}

/// <summary>
/// Hesap Kontrol analiz kaynağını arşiv öncelikli çözümler ve Excel dosyalarını
/// herhangi bir import veya kalıcı veri işlemi başlatmadan doğrular.
/// </summary>
public interface IHesapKontrolSourceResolver
{
    HesapKontrolSourceResolution Resolve(string baseFolder, DateOnly analizTarihi);
    string? Validate(string folder, DateOnly analizTarihi);
}

/// <summary>
/// Her karşılaştırmayı kendi dosya çiftiyle değerlendiren analizler için
/// en az bir çalışabilir dosya çifti bulunduğunu doğrular.
/// </summary>
public interface IPartialHesapKontrolSourceValidator
{
    string? ValidateForAnalysis(string folder, DateOnly analizTarihi);
}
