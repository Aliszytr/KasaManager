#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services.Comparison;

/// <summary>
/// Karşılaştırma dosyalarını tarih bazlı arşivleme servisi.
/// wwwroot/Data/Raporlar/archive/yyyy-MM-dd/ altına kopyalar.
/// </summary>
public sealed class ComparisonArchiveService : IComparisonArchiveService
{
    private readonly ILogger<ComparisonArchiveService> _logger;

    /// <summary>Karşılaştırma modülünün kullandığı Excel dosya adları.</summary>
    private static readonly string[] ComparisonFileNames = ExcelFileNames.ComparisonFiles;

    private const string ArchiveSubFolder = "archive";

    public ComparisonArchiveService(ILogger<ComparisonArchiveService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void ArchiveComparisonFiles(string uploadFolder)
    {
        if (string.IsNullOrWhiteSpace(uploadFolder) || !Directory.Exists(uploadFolder))
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var archiveDir = Path.Combine(uploadFolder, ArchiveSubFolder, today.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(archiveDir);

        int copied = 0;
        foreach (var fileName in ComparisonFileNames)
        {
            var source = Path.Combine(uploadFolder, fileName);
            if (!File.Exists(source)) continue;

            var dest = Path.Combine(archiveDir, fileName);
            try
            {
                File.Copy(source, dest, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Arşiv kopyalama hatası: {File}", fileName);
            }
        }

        if (copied > 0)
            _logger.LogInformation("Karşılaştırma arşivi oluşturuldu: {Date} ({Count} dosya)", today, copied);
    }

    /// <inheritdoc />
    public List<DateOnly> GetAvailableArchiveDates(string uploadFolder)
    {
        var dates = new List<DateOnly>();

        var archiveRoot = Path.Combine(uploadFolder, ArchiveSubFolder);
        if (!Directory.Exists(archiveRoot))
            return dates;

        foreach (var dir in Directory.GetDirectories(archiveRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (DateOnly.TryParseExact(dirName, "yyyy-MM-dd", out var date))
            {
                // En az bir karşılaştırma dosyası var mı kontrol et
                if (ComparisonFileNames.Any(f => File.Exists(Path.Combine(dir, f))))
                    dates.Add(date);
            }
        }

        // En yeniden eskiye sırala
        dates.Sort((a, b) => b.CompareTo(a));
        return dates;
    }

    /// <inheritdoc />
    public string? GetArchiveFolder(string uploadFolder, DateOnly date)
    {
        var archiveDir = Path.Combine(uploadFolder, ArchiveSubFolder, date.ToString("yyyy-MM-dd"));
        return Directory.Exists(archiveDir) ? archiveDir : null;
    }

    /// <inheritdoc />
    public int CleanupOldArchives(string uploadFolder, int retentionDays = 60)
    {
        var archiveRoot = Path.Combine(uploadFolder, ArchiveSubFolder);
        if (!Directory.Exists(archiveRoot))
            return 0;

        var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        int deleted = 0;

        foreach (var dir in Directory.GetDirectories(archiveRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (!DateOnly.TryParseExact(dirName, "yyyy-MM-dd", out var date)) continue;
            if (date >= cutoff) continue;

            try
            {
                Directory.Delete(dir, recursive: true);
                deleted++;
                _logger.LogInformation("Eski arşiv silindi: {Date}", dirName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Arşiv silme hatası: {Dir}", dirName);
            }
        }

        return deleted;
    }
}
