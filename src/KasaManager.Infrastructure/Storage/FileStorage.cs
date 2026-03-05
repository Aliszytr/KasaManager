using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;

namespace KasaManager.Infrastructure.Storage;

public sealed class FileStorage : IFileStorage
{
    private readonly string _webRoot;

    public FileStorage(string webRoot)
    {
        _webRoot = webRoot;
    }

    public Result<string> SaveUploadedFile(
        Stream fileStream,
        string originalFileName,
        string subFolder,
        string[] allowedExtensions,
        long maxBytes,
        bool overwrite)
    {
        if (fileStream is null) return Result<string>.Fail("Dosya akışı boş.");
        if (string.IsNullOrWhiteSpace(originalFileName)) return Result<string>.Fail("Dosya adı boş.");

        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return Result<string>.Fail($"İzin verilmeyen uzantı: {ext}");

        if (maxBytes > 0 && fileStream.CanSeek)
        {
            var len = fileStream.Length;
            if (len > maxBytes) return Result<string>.Fail($"Dosya çok büyük: {len} byte. Limit: {maxBytes}.");
        }

        var safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(originalFileName)) + ext;

        var folder = Path.Combine(_webRoot, subFolder);
        Directory.CreateDirectory(folder);

        var fullPath = Path.Combine(folder, safeName);

        if (!overwrite && File.Exists(fullPath))
            return Result<string>.Fail($"Dosya zaten var: {safeName}");

        // overwrite ise eskiyi silip yaz
        if (overwrite && File.Exists(fullPath))
            File.Delete(fullPath);

        using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        fileStream.CopyTo(fs);

        return Result<string>.Success(fullPath);
    }

    public IReadOnlyList<string> ListFiles(string subFolder)
    {
        subFolder = (subFolder ?? string.Empty).Trim();
        if (subFolder.Length == 0) subFolder = "Data\\Raporlar";

        var folder = Path.Combine(_webRoot, subFolder);
        Directory.CreateDirectory(folder);

        return Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();
    }

    private static string MakeSafeFileName(string input)
    {
        input = (input ?? string.Empty).Trim();
        if (input.Length == 0) input = "rapor";

        // Windows yasak karakterleri temizle
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();

        // çok uzun adları kes
        var s = new string(chars);
        if (s.Length > 80) s = s[..80];

        return s;
    }
}
