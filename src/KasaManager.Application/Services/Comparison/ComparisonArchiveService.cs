#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Reports;
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
    public void ArchiveComparisonFiles(string uploadFolder, DateOnly? reportDate = null)
    {
        if (string.IsNullOrWhiteSpace(uploadFolder) || !Directory.Exists(uploadFolder))
            return;

        var archiveDate = reportDate ?? DateOnly.FromDateTime(DateTime.Now);
        var archiveDir = Path.Combine(uploadFolder, ArchiveSubFolder, archiveDate.ToString("yyyy-MM-dd"));
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
            _logger.LogInformation("Karşılaştırma arşivi oluşturuldu: {Date} ({Count} dosya)", archiveDate, copied);
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

/// <summary>
/// Arşiv ve güncel Hesap Kontrol kaynaklarını strict kurallarla, yan etkisiz doğrular.
/// </summary>
public sealed class HesapKontrolSourceResolver :
    IHesapKontrolSourceResolver,
    IPartialHesapKontrolSourceValidator
{
    private readonly IComparisonArchiveService _archive;
    private readonly IExcelTableReader _excelReader;
    private readonly ILogger<HesapKontrolSourceResolver> _logger;

    public HesapKontrolSourceResolver(
        IComparisonArchiveService archive,
        IExcelTableReader excelReader,
        ILogger<HesapKontrolSourceResolver> logger)
    {
        _archive = archive;
        _excelReader = excelReader;
        _logger = logger;
    }

    public HesapKontrolSourceResolution Resolve(string baseFolder, DateOnly analizTarihi)
    {
        var userFailures = new List<string>();
        var technicalFailures = new List<string>();
        var archiveFolder = _archive.GetArchiveFolder(baseFolder, analizTarihi);

        if (archiveFolder is null)
        {
            userFailures.Add($"Arşiv kaynağı: {analizTarihi:yyyy-MM-dd} tarihli klasör bulunamadı.");
            technicalFailures.Add($"Arşiv klasörü bulunamadı. BaseFolder='{baseFolder}', Date={analizTarihi:yyyy-MM-dd}.");
        }
        else
        {
            var archiveError = Validate(archiveFolder, analizTarihi);
            if (archiveError is null)
                return HesapKontrolSourceResolution.Success(
                    archiveFolder, HesapKontrolSourceKind.Archive);

            // Canonical production arşivinde global beş-dosya/tarih şartı
            // yerine bağımsız karşılaştırma çiftlerini değerlendir.
            if (IsCanonicalArchiveFolder(baseFolder, archiveFolder, analizTarihi))
            {
                var partialError = ValidateForAnalysis(archiveFolder, analizTarihi);
                if (partialError is null)
                    return HesapKontrolSourceResolution.Success(
                        archiveFolder, HesapKontrolSourceKind.Archive);
            }

            userFailures.Add($"Arşiv kaynağı: {archiveError}");
            technicalFailures.Add($"Arşiv kaynağı ('{archiveFolder}'): {archiveError}");
        }

        var currentError = Validate(baseFolder, analizTarihi);
        if (currentError is null || ValidateForAnalysis(baseFolder, analizTarihi) is null)
            return HesapKontrolSourceResolution.Success(
                baseFolder, HesapKontrolSourceKind.Current);

        userFailures.Add($"Güncel kaynak: {currentError}");
        technicalFailures.Add($"Güncel kaynak ('{baseFolder}'): {currentError}");
        return HesapKontrolSourceResolution.Fail(
            "Seçilen tarih için gerekli Excel kaynakları doğrulanamadı. " +
            string.Join(" ", userFailures),
            string.Join(" ", technicalFailures));
    }

    public string? Validate(string folder, DateOnly analizTarihi)
    {
        if (!Directory.Exists(folder))
            return "Kaynak klasör bulunamadı.";

        var missingFiles = ExcelFileNames.ComparisonFiles
            .Where(fileName => !File.Exists(Path.Combine(folder, fileName)))
            .ToList();
        if (missingFiles.Count > 0)
            return $"Eksik zorunlu dosya: {string.Join(", ", missingFiles.Select(x => $"'{x}'"))}.";

        var tables = new Dictionary<string, ImportedTable>(StringComparer.OrdinalIgnoreCase);
        var unreadableFiles = new List<string>();

        foreach (var fileName in ExcelFileNames.ComparisonFiles)
        {
            try
            {
                var result = _excelReader.ReadTable(
                    Path.Combine(folder, fileName),
                    new ExcelReadOptions { MaxRows = 5000, SkipEmptyRows = true });

                if (!result.Ok || result.Value is null)
                {
                    unreadableFiles.Add(fileName);
                    _logger.LogDebug(
                        "[HK-STRICT-VALIDATE] Dosya okunamadı. Folder={Folder} File={File} Error={Error}",
                        folder, fileName, result.Error);
                    continue;
                }

                tables[fileName] = result.Value;
            }
            catch (Exception ex)
            {
                unreadableFiles.Add(fileName);
                _logger.LogDebug(ex,
                    "[HK-STRICT-VALIDATE] Dosya okuma istisnası. Folder={Folder} File={File}",
                    folder, fileName);
            }
        }

        if (unreadableFiles.Count > 0)
            return $"Dosya okunamadı: {string.Join(", ", unreadableFiles.Select(x => $"'{x}'"))}.";

        var filesWithoutDate = new List<string>();
        foreach (var fileName in ExcelFileNames.ComparisonFiles)
        {
            var table = tables[fileName];
            var dateColumn = FindDateColumn(table);
            var containsDate = dateColumn is not null && table.Rows.Any(row =>
                row is not null && DateParsingHelper.RowMatchesDate(row, dateColumn, analizTarihi));

            if (!containsDate)
                filesWithoutDate.Add(fileName);
        }

        if (filesWithoutDate.Count > 0)
        {
            return $"Seçilen tarihi içermeyen dosya: " +
                   $"{string.Join(", ", filesWithoutDate.Select(x => $"'{x}'"))}. " +
                   $"{analizTarihi:dd.MM.yyyy} tarihine ait satır bulunamadı.";
        }

        return null;
    }

    public string? ValidateForAnalysis(string folder, DateOnly analizTarihi)
    {
        if (!Directory.Exists(folder))
            return "Kaynak klasör bulunamadı.";

        var pairs = new[]
        {
            new[] { ExcelFileNames.BankaTahsilat, ExcelFileNames.OnlineMasraf },
            new[] { ExcelFileNames.BankaHarc, ExcelFileNames.OnlineHarc },
            new[] { ExcelFileNames.BankaTahsilat, ExcelFileNames.OnlineReddiyat }
        };

        var readable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in ExcelFileNames.ComparisonFiles)
        {
            var path = GetCanonicalChildPath(folder, fileName);
            if (path is null || !File.Exists(path))
            {
                readable[fileName] = false;
                continue;
            }

            try
            {
                var result = _excelReader.ReadTable(
                    path,
                    new ExcelReadOptions { MaxRows = 5000, SkipEmptyRows = true });
                readable[fileName] = result.Ok && result.Value is not null;
            }
            catch (Exception ex)
            {
                readable[fileName] = false;
                _logger.LogDebug(ex,
                    "[HK-PARTIAL-VALIDATE] Dosya okuma istisnası. Folder={Folder} File={File}",
                    folder, fileName);
            }
        }

        if (pairs.Any(pair => pair.All(file => readable.GetValueOrDefault(file))))
            return null;

        return "Çalıştırılabilir karşılaştırma kaynak çifti bulunamadı.";
    }

    private static bool IsCanonicalArchiveFolder(
        string baseFolder,
        string archiveFolder,
        DateOnly analizTarihi)
    {
        try
        {
            var expected = Path.GetFullPath(Path.Combine(
                baseFolder, "archive", analizTarihi.ToString("yyyy-MM-dd")));
            var actual = Path.GetFullPath(archiveFolder);
            return string.Equals(
                expected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                actual.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetCanonicalChildPath(string folder, string fileName)
    {
        try
        {
            var root = Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, fileName));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindDateColumn(ImportedTable table)
    {
        string[] candidates =
        {
            "islem_tarihi", "tarih", "date", "islemtarihi",
            "işlem_tarihi", "işlem tarihi", "tarih/saat"
        };

        foreach (var candidate in candidates)
        {
            var match = table.Columns.FirstOrDefault(column =>
                string.Equals(column, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return table.Columns.FirstOrDefault(column =>
            column.Contains("tarih", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("date", StringComparison.OrdinalIgnoreCase));
    }
}
