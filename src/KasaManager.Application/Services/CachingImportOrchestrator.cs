using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services;

/// <summary>
/// MS4: Decorator — Excel dosya okumalarını IAppCache üzerinden cache'ler.
/// Aynı dosya (path + lastWriteTime + kind) ikinci kez okunduğunda
/// disk I/O + parse maliyeti sıfırlanır.
///
/// Cache anahtarı: {prefix}:{dosyaYolu}:{lastWriteTimeTicks}:{kind}
/// Dosya değiştirilirse (lastWriteTime değişir) otomatik invalidate olur.
/// TTL: config'ten gelir, varsayılan 60 dakika.
/// </summary>
public sealed class CachingImportOrchestrator : IImportOrchestrator
{
    private readonly ImportOrchestrator _inner;
    private readonly IAppCache _cache;
    private readonly ILogger<CachingImportOrchestrator> _log;

    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(60);

    public CachingImportOrchestrator(
        ImportOrchestrator inner,
        IAppCache cache,
        ILogger<CachingImportOrchestrator> log)
    {
        _inner = inner;
        _cache = cache;
        _log = log;
    }

    public Result<ImportedTable> Import(string absoluteFilePath, ImportFileKind kind)
    {
        var key = BuildKey(absoluteFilePath, kind, trueSource: false);
        if (key != null)
        {
            var cached = CacheGet<Result<ImportedTable>>(key);
            if (cached != null)
            {
                _log.LogDebug("ExcelCache HIT: {Key}", key);
                return cached;
            }
        }

        var result = _inner.Import(absoluteFilePath, kind);

        if (key != null && result.Ok)
        {
            CacheSet(key, result, DefaultCacheTtl);
            _log.LogDebug("ExcelCache STORED: {Key}", key);
        }

        CacheEvict();
        return result;
    }

    public Result<ImportedTable> ImportTrueSource(string absoluteFilePath, ImportFileKind kind)
    {
        var key = BuildKey(absoluteFilePath, kind, trueSource: true);
        if (key != null)
        {
            var cached = CacheGet<Result<ImportedTable>>(key);
            if (cached != null)
            {
                _log.LogDebug("ExcelCache HIT (TrueSource): {Key}", key);
                return cached;
            }
        }

        var result = _inner.ImportTrueSource(absoluteFilePath, kind);

        if (key != null && result.Ok)
        {
            CacheSet(key, result, DefaultCacheTtl);
            _log.LogDebug("ExcelCache STORED (TrueSource): {Key}", key);
        }

        CacheEvict();
        return result;
    }

    public ImportFileKind GuessKindFromFileName(string fileName)
        => _inner.GuessKindFromFileName(fileName);

    // ─── Helpers ───

    private static string? BuildKey(string? filePath, ImportFileKind kind, bool trueSource)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(filePath).Ticks;
            var prefix = trueSource ? "TS" : "STD";
            return $"{prefix}:{filePath}:{lastWrite}:{kind}";
        }
        catch (IOException)
        {
            return null; // Dosya erişim hatası — cache'siz devam et
        }
    }

    // ─── OB-1 FIX: Native sync cache erişim ───
    // IAppCache.Get/Set/EvictExpired kullanılıyor — .GetAwaiter().GetResult() yok.
    // MemoryAppCache native sync, farklı provider da interface default ile çalışır.

    private T? CacheGet<T>(string key)
        => _cache.Get<T>(key);

    private void CacheSet<T>(string key, T value, TimeSpan ttl)
        => _cache.Set(key, value, ttl);

    private void CacheEvict()
        => _cache.EvictExpired();
}

