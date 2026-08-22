using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Web.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KasaManager.Tests;

/// <summary>
/// Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE, Step 7: bu dosya "kanıt karşılaştırmasının
/// KENDİSİNİ" doğrudan test eder — mocked tier değerleri etrafında kurgulanmış sahte "freshness"
/// testleri DEĞİLDİR (Helpy'nin açıkça yasakladığı desen). Gerçek geçici klasörler + gerçek dosya
/// içerikleri kullanılır; KasaSourceFingerprintHelper.CaptureAsync/CheckFreshnessAsync doğrudan
/// çağrılır, hiçbir controller/DI/Moq katmanı araya girmez.
/// </summary>
public sealed class KasaSourceFingerprintHelperTests : IDisposable
{
    private readonly string _uploadFolder;

    public KasaSourceFingerprintHelperTests()
    {
        _uploadFolder = Path.Combine(Path.GetTempPath(), "KasaSourceFingerprintHelperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_uploadFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_uploadFolder)) Directory.Delete(_uploadFolder, recursive: true); }
        catch { /* best-effort temizlik */ }
    }

    private void WriteExcelLikeFile(string fileName, string content)
        => File.WriteAllText(Path.Combine(_uploadFolder, fileName), content);

    // ─── 1. Aynı kaynak → Current ───

    [Fact]
    public async Task CheckFreshnessAsync_PersistedEvidenceMatchesCurrentSource_ReturnsCurrent()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "AAA-original-content");
        var date = new DateOnly(2026, 8, 15);

        var persisted = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);
        Assert.NotNull(persisted);
        var persistedJson = KasaSourceFingerprintHelper.SerializeEvidence(persisted!);

        // Kaynak hiç değişmedi — SaveAsync anında yakalanan kanıtla şu anki kaynak birebir aynı.
        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            persistedJson, _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Current, freshness);
    }

    // ─── 2. Kaynak değişti → Stale ───

    [Fact]
    public async Task CheckFreshnessAsync_SourceFileContentChangedAfterPersist_ReturnsStale()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "AAA-original-content");
        var date = new DateOnly(2026, 8, 15);

        var persisted = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);
        var persistedJson = KasaSourceFingerprintHelper.SerializeEvidence(persisted!);

        // Kullanıcı yeni bir Excel yükledi — aynı dosya adı, FARKLI içerik.
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "BBB-updated-content-after-persist");

        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            persistedJson, _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Stale, freshness);
    }

    // ─── 3. Legacy/null SourceEvidenceJson → Unknown (Stale DEĞİL) ───

    [Fact]
    public async Task CheckFreshnessAsync_NullPersistedEvidence_ReturnsUnknown_NotStale()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "irrelevant-current-content");
        var date = new DateOnly(2026, 8, 15);

        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            persistedSourceEvidenceJson: null,
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Unknown, freshness);
        Assert.NotEqual(KasaSourceFreshness.Stale, freshness);
        Assert.NotEqual(KasaSourceFreshness.Current, freshness);
    }

    // ─── 4a. Bozuk/parse edilemeyen JSON → Unknown ───

    [Fact]
    public async Task CheckFreshnessAsync_MalformedPersistedEvidenceJson_ReturnsUnknown()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "content");
        var date = new DateOnly(2026, 8, 15);

        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            persistedSourceEvidenceJson: "{ this is not valid json",
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Unknown, freshness);
    }

    // ─── 4b. Şema uymayan (eski TryAutoProvisionGenelSnapshotAsync JSON şekli) → Unknown, ASLA Stale sanılmaz ───

    [Fact]
    public async Task CheckFreshnessAsync_LegacyIncompatibleEvidenceShape_ReturnsUnknown_NeverMisreadAsStale()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "content");
        var date = new DateOnly(2026, 8, 15);

        // TryAutoProvisionGenelSnapshotAsync'in yazdığı GERÇEK (farklı) JSON şekli — Fingerprint alanı
        // yok. STJ bunu KasaDraftSourceContext'e sessizce (hatasız) deserialize edebilir ama Fingerprint
        // boş kalır — TryDeserializeEvidence bunu şema-geçersiz sayıp null döndürmeli, yoksa
        // string.Equals(null, "...") => false => yanlışlıkla "Stale" üretilirdi.
        var legacyShapeJson = "{\"Source\":\"Excel\",\"File\":\"x.xlsx\",\"FileDate\":\"2026-08-15\",\"TargetDate\":\"2026-08-15\",\"RowCount\":42}";

        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            legacyShapeJson, _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Unknown, freshness);
    }

    // ─── 5. Kaynak artık hiç fingerprint'lenemiyor (dosyalar silinmiş) → Unknown ───

    [Fact]
    public async Task CheckFreshnessAsync_CurrentSourceNoLongerFingerprintable_ReturnsUnknown()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "content");
        var date = new DateOnly(2026, 8, 15);

        var persisted = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);
        var persistedJson = KasaSourceFingerprintHelper.SerializeEvidence(persisted!);

        // Yükleme klasörü artık boş (ör. tüm dosyalar taşındı/silindi) — CaptureAsync null döner.
        File.Delete(Path.Combine(_uploadFolder, "MasrafveReddiyat.xlsx"));

        var freshness = await KasaSourceFingerprintHelper.CheckFreshnessAsync(
            persistedJson, _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(KasaSourceFreshness.Unknown, freshness);
    }

    // ─── 6. Serialize/Deserialize round-trip, fingerprint korunur ───

    [Fact]
    public async Task SerializeEvidence_TryDeserializeEvidence_RoundTripPreservesFingerprint()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "roundtrip-content");
        var date = new DateOnly(2026, 8, 15);

        var captured = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);
        Assert.NotNull(captured);

        var json = KasaSourceFingerprintHelper.SerializeEvidence(captured!);
        var roundTripped = KasaSourceFingerprintHelper.TryDeserializeEvidence(json, NullLogger.Instance);

        Assert.NotNull(roundTripped);
        Assert.Equal(captured!.Fingerprint, roundTripped!.Fingerprint);
        Assert.Equal(captured.Version, roundTripped.Version);
    }

    // ─── 7. kasaScope hash'e dahil değil (mevcut CaptureKasaDraftSourceContextAsync davranışı) ───

    [Fact]
    public async Task CaptureAsync_FingerprintIsIndependentOfKasaScope_SameUploadFolderIsShared()
    {
        WriteExcelLikeFile("MasrafveReddiyat.xlsx", "shared-folder-content");
        var date = new DateOnly(2026, 8, 15);

        var genel = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Genel", NullLogger.Instance, CancellationToken.None);
        var aksam = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, date, "Aksam", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(genel!.Fingerprint, aksam!.Fingerprint);
    }
}
