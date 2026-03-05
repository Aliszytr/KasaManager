using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using KasaManager.Web.Models;
using Microsoft.Extensions.Logging;

namespace KasaManager.Web.Helpers;

/// <summary>
/// Kasa hesaplama sonuçlarını bellek içi statik depoda saklayarak
/// kullanıcı başka menüye gidip döndüğünde veri kaybını önler.
/// 
/// Depolama: ViewModel'in TÜM alanlarını doğrudan JSON olarak saklar.
/// DTO dönüşümü yok — veri kaybı riski sıfır.
/// Restore: JSON → KasaPreviewViewModel, gerekli alanlar direkt kopyalanır.
/// 
/// Eviction: Periyodik background timer ile süresi dolmuş entry'ler
/// otomatik temizlenir (memory leak önleme).
/// </summary>
public static class KasaDraftCacheHelper
{
    private const string KeyPrefix = "KasaDraft";
    private static readonly TimeSpan DraftExpiry = TimeSpan.FromHours(8);
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(30);

    /// <summary>Statik bellek deposu: Key → DraftEntry</summary>
    private static readonly ConcurrentDictionary<string, DraftEntry> _store = new();

    /// <summary>Background eviction timer — süresi dolmuş entry'leri otomatik temizler.</summary>
    private static readonly Timer _evictionTimer = new(
        _ => EvictExpiredEntries(), null, EvictionInterval, EvictionInterval);

    /// <summary>Circular reference güvenli + case-insensitive enum parse.</summary>
    private static readonly JsonSerializerOptions SafeJsonOpts = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Hesaplama sonrası ViewModel'i doğrudan JSON olarak cache'e yazar.
    /// DTO dönüşümü yok — tüm alanlar korunur.
    /// </summary>
    public static Task SaveDraftAsync(
        string userName,
        string kasaType,
        KasaPreviewViewModel model,
        ILogger? log = null)
    {
        var key = BuildKey(userName, kasaType);

        try
        {
            var json = JsonSerializer.Serialize(model, SafeJsonOpts);

            var infoMessage = $"📋 {model.KasaType} Kasa — {model.SelectedDate:dd.MM.yyyy} tarihli veriler, saat {DateTime.Now:HH:mm} itibariyle hesaplanmıştır.";

            _store[key] = new DraftEntry(json, infoMessage, DateTime.UtcNow);

            log?.LogDebug(
                "KasaDraft SAVED: Key={Key}, HasResults={HR}, FormulaRun={FR}, JSON={Size}b, Store={Count}",
                key, model.HasResults, model.FormulaRun != null, json.Length, _store.Count);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "KasaDraft SAVE FAILED: Key={Key}", key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Depodan draft'ı yükler. JSON'dan yeni bir ViewModel oluşturup
    /// tüm alanları hedef modele kopyalar.
    /// </summary>
    public static Task<bool> TryLoadDraftAsync(
        string userName,
        string kasaType,
        KasaPreviewViewModel target,
        ILogger? log = null)
    {
        var key = BuildKey(userName, kasaType);
        log?.LogDebug("KasaDraft LOAD ATTEMPT: Key={Key}, Store={Count}",
            key, _store.Count);

        if (!_store.TryGetValue(key, out var entry))
        {
            log?.LogDebug("KasaDraft LOAD: Key={Key} bulunamadı", key);
            return Task.FromResult(false);
        }

        // TTL kontrolü
        if (DateTime.UtcNow - entry.SavedAtUtc > DraftExpiry)
        {
            log?.LogInformation("KasaDraft LOAD: Key={Key} süresi dolmuş, temizleniyor", key);
            _store.TryRemove(key, out _);
            return Task.FromResult(false);
        }

        try
        {
            // JSON → yeni ViewModel instance
            var restored = JsonSerializer.Deserialize<KasaPreviewViewModel>(entry.ViewModelJson, SafeJsonOpts);
            if (restored == null)
            {
                log?.LogWarning("KasaDraft LOAD: JSON deserialize null döndü");
                return Task.FromResult(false);
            }

            // Tüm alanları hedef modele kopyala
            CopyAllProperties(restored, target);

            log?.LogDebug(
                "KasaDraft LOAD OK: HasResults={HR}, KasaType={KT}, Date={D}, Age={Age}",
                target.HasResults, target.KasaType, target.SelectedDate,
                DateTime.UtcNow - entry.SavedAtUtc);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "KasaDraft LOAD FAILED: Key={Key}", key);
            _store.TryRemove(key, out _);
            return Task.FromResult(false);
        }
    }

    /// <summary>Tüm public settable property'leri source'dan target'a kopyalar.</summary>
    private static void CopyAllProperties(KasaPreviewViewModel source, KasaPreviewViewModel target)
    {
        var props = typeof(KasaPreviewViewModel).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (prop.CanRead && prop.CanWrite)
            {
                var value = prop.GetValue(source);
                prop.SetValue(target, value);
            }
        }
    }

    /// <summary>Draft info mesajı.</summary>
    public static string? GetDraftInfoMessage(string userName, string kasaType)
    {
        var key = BuildKey(userName, kasaType);
        if (!_store.TryGetValue(key, out var entry))
            return null;

        if (DateTime.UtcNow - entry.SavedAtUtc > DraftExpiry)
            return null;

        return entry.InfoMessage;
    }

    /// <summary>Draft'ı temizler.</summary>
    public static Task ClearDraftAsync(string userName, string kasaType)
    {
        var key = BuildKey(userName, kasaType);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(string userName, string kasaType)
        => $"{KeyPrefix}:{(userName ?? "anon").ToLowerInvariant()}:{(kasaType ?? "unknown").ToLowerInvariant()}";

    /// <summary>Süresi dolmuş tüm draft entry'lerini temizler.</summary>
    private static void EvictExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _store
            .Where(kvp => now - kvp.Value.SavedAtUtc > DraftExpiry)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _store.TryRemove(key, out _);
        }
    }

    // ─── Draft Entry ───

    private sealed record DraftEntry(string ViewModelJson, string InfoMessage, DateTime SavedAtUtc);
}
