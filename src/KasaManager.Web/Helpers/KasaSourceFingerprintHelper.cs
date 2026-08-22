#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KasaManager.Web.Helpers;

/// <summary>
/// Revision 3 — PERSISTED SOURCE FRESHNESS CLOSURE (KASAMANAGER-2026-08-21-FINAL-PLAN-CLOSURE).
///
/// Tek, paylaşılan fingerprint/freshness mantığı. Daha önce yalnızca KasaPreviewController'a özel
/// (private instance) CaptureKasaDraftSourceContextAsync/VerifyKasaDraftSourceContextAsync olarak var
/// olan SHA256 manifest algoritması BİREBİR AYNI ŞEKİLDE buraya taşındı — ikinci, rakip bir kanıt
/// formatı İCAT EDİLMEDİ. KasaPreviewController.Helpers.cs'teki iki metot artık bu sınıfa delege eden
/// ince (thin) wrapper'lardır (davranış değişmedi). KasaUstRaporController.Save de AYNI CaptureAsync'i
/// kullanarak KasaRaporSnapshot.SourceEvidenceJson'ı doldurur.
///
/// NOT: KasaDraftSourceContext.SourceKind alanı yalnızca açıklayıcı/bilgi amaçlıdır (fingerprint
/// hesaplamasına dahil değildir). Eskiden nameof(HesapKontrolSourceKind.Current) kullanılıyordu; o enum
/// HesapKontrol immutable-audit alt sistemine ait, bu genel-amaçlı sınıfın ona bağımlı olmasını
/// gerektirecek bir sebep yok — sabit "Current" string'i ile birebir aynı değeri üretir.
/// </summary>
public static class KasaSourceFingerprintHelper
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Yükleme klasöründeki *.xls* dosyalarının (isim + içerik SHA256) sıralı manifest'inden tek bir
    /// bundle fingerprint üretir. kasaScope yalnızca kayıt/açıklama amaçlıdır — hash GİRDİSİNE dahil
    /// DEĞİLDİR (mevcut CaptureKasaDraftSourceContextAsync davranışı birebir korunur — bu bilinçli:
    /// tek upload klasörü tüm kasa tiplerince paylaşılıyor). selectedDate yoksa ya da klasörde .xls*
    /// dosyası yoksa null döner (mevcut davranış).
    /// </summary>
    public static async Task<KasaDraftSourceContext?> CaptureAsync(
        string uploadFolder,
        DateOnly? selectedDate,
        string kasaScope,
        ILogger? log,
        CancellationToken ct)
    {
        if (!selectedDate.HasValue || !Directory.Exists(uploadFolder))
            return null;

        try
        {
            var files = Directory.GetFiles(uploadFolder, "*.xls*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
                return null;

            var manifest = new StringBuilder();
            var fileNames = new List<string>(files.Length);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                fileNames.Add(fileName);

                await using var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 81920, useAsync: true);
                var contentHash = await SHA256.HashDataAsync(stream, ct);
                manifest
                    .Append(fileName.ToUpperInvariant())
                    .Append(':')
                    .Append(Convert.ToHexString(contentHash))
                    .Append('\n');
            }

            var bundleHash = SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString()));
            return new KasaDraftSourceContext(
                Version: 1,
                SelectedDate: selectedDate.Value,
                KasaType: NormalizeKasaScope(kasaScope),
                SourceKind: "Current",
                SourceIdentifier: "current-upload-bundle",
                FileNames: fileNames,
                Fingerprint: Convert.ToHexString(bundleHash));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Kasa kaynak baglami olusturulamadi. KasaScope={KasaScope}", kasaScope);
            return null;
        }
    }

    /// <summary>
    /// Aynı request içinde önce/sonra karşılaştırması — LoadAndCalculate'in mevcut davranışı,
    /// değiştirilmeden buraya taşındı. Kaynak (Version veya Fingerprint) değiştiyse null döner.
    /// </summary>
    public static async Task<KasaDraftSourceContext?> VerifyAsync(
        KasaDraftSourceContext? beforeCalculation,
        string uploadFolder,
        DateOnly? selectedDate,
        string kasaScope,
        ILogger? log,
        CancellationToken ct)
    {
        if (beforeCalculation is null)
            return null;

        var afterCalculation = await CaptureAsync(uploadFolder, selectedDate, kasaScope, log, ct);
        if (afterCalculation is null
            || beforeCalculation.Version != afterCalculation.Version
            || !string.Equals(beforeCalculation.Fingerprint, afterCalculation.Fingerprint, StringComparison.Ordinal))
        {
            log?.LogWarning(
                "Kasa hesaplamasi sirasinda kaynak paketi degisti; kaynak baglami kaydedilmedi. KasaScope={KasaScope}",
                kasaScope);
            return null;
        }

        return afterCalculation;
    }

    /// <summary>
    /// KasaPreviewController.NormalizeKasaType ile davranışça birebir aynı normalizasyon. Ayrı bir
    /// kopya olarak tutuluyor çünkü orijinali controller'a private static bağlı — bu paylaşılan sınıfın
    /// bir controller'a bağımlı olması yön olarak tersine (Helpers → Controller) bir bağımlılık
    /// yaratırdı. Fonksiyon saf ve altı satırlık; kopyalanması broad-refactor SAYILMAZ.
    /// </summary>
    public static string NormalizeKasaScope(string kasaScope)
    {
        if (string.IsNullOrWhiteSpace(kasaScope))
            return kasaScope ?? string.Empty;

        return kasaScope.Trim().ToLowerInvariant() switch
        {
            "aksam" or "akşam" or "aksamkasa" => "Aksam",
            "sabah" or "sabahkasa" => "Sabah",
            "genel" or "genelkasa" => "Genel",
            "ortak" or "ortakkasa" or "ozet" or "özetkasa" => "Ortak",
            _ => kasaScope.Trim()
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // Step 2/3: Persisted evidence (de)serialization + explicit freshness
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// KasaRaporSnapshot.SourceEvidenceJson'a yazılacak JSON — mevcut KasaDraftSourceContext formatı
    /// AYNEN kullanılıyor (Step 1/2: "ikinci, rakip bir kanıt formatı icat edilmez").
    /// </summary>
    public static string SerializeEvidence(KasaDraftSourceContext evidence)
        => JsonSerializer.Serialize(evidence, EvidenceJsonOptions);

    /// <summary>
    /// Persisted SourceEvidenceJson'ı okur. Null/boş, deserialize edilemeyen, ya da tanınan
    /// fingerprint şemasına uymayan (ör. TryAutoProvisionGenelSnapshotAsync'in eski, FARKLI anonim JSON
    /// şekli — {Source,File,FileDate,TargetDate,RowCount}) her durumda null döner — bu durumların
    /// hepsi CheckFreshnessAsync tarafından Unknown'a çevrilir, ASLA sessizce Stale/Current sanılmaz.
    /// </summary>
    public static KasaDraftSourceContext? TryDeserializeEvidence(string? sourceEvidenceJson, ILogger? log)
    {
        if (string.IsNullOrWhiteSpace(sourceEvidenceJson))
            return null;

        try
        {
            var evidence = JsonSerializer.Deserialize<KasaDraftSourceContext>(sourceEvidenceJson, EvidenceJsonOptions);
            if (evidence is null || evidence.Version < 1 || string.IsNullOrWhiteSpace(evidence.Fingerprint))
            {
                // Sema uyuşmuyor (ör. eski/yabancı JSON şekli STJ tarafından sessizce varsayılan
                // değerlerle "başarılı" deserialize edilmiş olabilir) — bilerek Unknown'a düşürülüyor.
                log?.LogDebug("Persisted SourceEvidenceJson taninan fingerprint semasina uymuyor (Unknown).");
                return null;
            }

            return evidence;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Persisted SourceEvidenceJson deserialize edilemedi (Unknown freshness).");
            return null;
        }
    }

    /// <summary>
    /// Step 3: Explicit Current/Stale/Unknown sonucu — resolver tier'ından TAMAMEN bağımsız.
    /// Current: persisted evidence var, geçerli, güncel kaynakla eşleşiyor.
    /// Stale: persisted evidence var, geçerli, güncel kaynaktan FARKLI.
    /// Unknown: kanıt yok/bozuk/okunamaz VEYA güncel kaynak şu an fingerprint'lenemiyor — fail-closed.
    /// Invariant: Unknown asla Current/Stale'e YÜKSELTİLMEZ.
    /// </summary>
    public static async Task<KasaSourceFreshness> CheckFreshnessAsync(
        string? persistedSourceEvidenceJson,
        string uploadFolder,
        DateOnly persistedDate,
        string kasaScope,
        ILogger? log,
        CancellationToken ct)
    {
        var persistedEvidence = TryDeserializeEvidence(persistedSourceEvidenceJson, log);
        if (persistedEvidence is null)
            return KasaSourceFreshness.Unknown;

        KasaDraftSourceContext? currentEvidence;
        try
        {
            currentEvidence = await CaptureAsync(uploadFolder, persistedDate, kasaScope, log, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Freshness check icin kaynak yeniden fingerprint'lenemedi (Unknown).");
            return KasaSourceFreshness.Unknown;
        }

        if (currentEvidence is null)
            return KasaSourceFreshness.Unknown;

        return string.Equals(persistedEvidence.Fingerprint, currentEvidence.Fingerprint, StringComparison.Ordinal)
            ? KasaSourceFreshness.Current
            : KasaSourceFreshness.Stale;
    }
}

/// <summary>
/// Step 3: SuccessfulPersistedKasa tier'ında bulunan persisted Kasa'nın, o anda analiz edilebilir
/// kaynağa göre içerik-seviyesinde güncel olup olmadığını ifade eden açık sonuç. Resolver tier'ından
/// TÜRETİLMEZ — ayrı, dürüst bir sinyaldir. Unknown == 0 (default/fail-closed).
/// </summary>
public enum KasaSourceFreshness
{
    Unknown = 0,
    Current = 1,
    Stale = 2
}
