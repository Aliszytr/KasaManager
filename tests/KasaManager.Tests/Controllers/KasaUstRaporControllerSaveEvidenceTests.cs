using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Controllers;
using KasaManager.Web.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KasaManager.Tests.Controllers;

/// <summary>
/// Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE, Step 7: "gerçek normal Kasa kaydetme yolunun
/// (KasaUstRaporController.Save) artık SourceEvidenceJson'ı doldurduğunu kanıtla" gereksinimi.
/// Bu repoda WebApplicationFactory/TestServer yok — mevcut doğrudan Moq controller-instantiation
/// deseni (KasaPreviewAutoPostTests.cs) genişletildi. Upload klasörü GERÇEK bir geçici klasördür
/// (Save'in kendi CaptureAsync/VerifyAsync çağrıları gerçek dosya I/O yapar) — bu yüzden testler
/// "kanıt karşılaştırmasının kendisini" (Step 7'nin yasakladığı mocked-tier taklidini değil) sınar.
/// </summary>
public sealed class KasaUstRaporControllerSaveEvidenceTests : IDisposable
{
    private readonly string _uploadFolder;
    private const string KasaUstRaporFileName = "KasaUstRapor_test.xlsx";

    public KasaUstRaporControllerSaveEvidenceTests()
    {
        _uploadFolder = Path.Combine(Path.GetTempPath(), "KasaUstRaporSaveEvidenceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_uploadFolder);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_uploadFolder)) Directory.Delete(_uploadFolder, recursive: true); }
        catch { /* best-effort temizlik */ }
    }

    private (KasaUstRaporController controller, Mock<IKasaRaporSnapshotService> snapshots,
        Mock<IImportOrchestrator> orchestrator, Mock<IKasaReportDateRulesService> dateRules)
        CreateController()
    {
        // GetUploadFolderAbsolute(sub) = Path.Combine(WebRootPath, sub). WebRootPath'i _uploadFolder'in
        // PARENT'i, Upload:SubFolder'i de _uploadFolder'in kendi adi yaparak sonucun tam olarak
        // _uploadFolder'e esit olmasini garanti ediyoruz.
        var subFolderName = Path.GetFileName(_uploadFolder);
        var webRootPath = Directory.GetParent(_uploadFolder)!.FullName;
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(webRootPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = subFolderName
            })
            .Build();

        var storage = Mock.Of<IFileStorage>();
        var orchestrator = new Mock<IImportOrchestrator>();
        var dateRules = new Mock<IKasaReportDateRulesService>();
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation());
        var snapshots = new Mock<IKasaRaporSnapshotService>();
        snapshots
            .Setup(s => s.SaveAsync(It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KasaRaporSnapshot s, CancellationToken _) => s);
        var globalDefaults = Mock.Of<IKasaGlobalDefaultsService>();
        var exportService = Mock.Of<IExportService>();

        var controller = new KasaUstRaporController(
            storage, orchestrator.Object, dateRules.Object, snapshots.Object,
            globalDefaults, exportService, configuration);

        var httpContext = new DefaultHttpContext { RequestServices = new SingleServiceProvider(env.Object) };
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = tempData;
        // RedirectToAction(action, controller, routeValues) sets its result's UrlHelper from
        // ControllerBase.Url immediately (not only at IActionResult execution time), which otherwise
        // resolves IUrlHelperFactory from RequestServices — not registered by this minimal test harness.
        // Assigning Url directly bypasses that DI lookup; Save never calls anything on it.
        controller.Url = Mock.Of<IUrlHelper>();

        return (controller, snapshots, orchestrator, dateRules);
    }

    private void WriteRealUploadFile(string fileName, string content)
        => File.WriteAllText(Path.Combine(_uploadFolder, fileName), content);

    private static ImportedTable BuildImportedTable()
    {
        return new ImportedTable
        {
            SourceFileName = KasaUstRaporFileName,
            Kind = ImportFileKind.KasaUstRapor,
            ColumnMetas = new List<ImportedColumnMeta>
            {
                new() { Index = 0, CanonicalName = "veznedar", OriginalHeader = "Veznedar" },
                new() { Index = 1, CanonicalName = "bakiye", OriginalHeader = "Bakiye" }
            },
            Rows = new List<Dictionary<string, string?>>
            {
                new() { ["veznedar"] = "Ahmet", ["bakiye"] = "100,50" }
            }
        };
    }

    // ─── Gerçek "Kaydet" yolu artık SourceEvidenceJson doldurur ───

    [Fact]
    public async Task Save_SuccessfulSave_PopulatesSourceEvidenceJsonWithRealFingerprint()
    {
        WriteRealUploadFile(KasaUstRaporFileName, "kasaustrapor-content-v1");
        var (controller, snapshots, orchestrator, _) = CreateController();
        orchestrator
            .Setup(o => o.Import(It.IsAny<string>(), ImportFileKind.KasaUstRapor))
            .Returns(Result<ImportedTable>.Success(BuildImportedTable()));

        KasaRaporSnapshot? captured = null;
        snapshots
            .Setup(s => s.SaveAsync(It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<KasaRaporSnapshot, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync((KasaRaporSnapshot s, CancellationToken _) => s);

        var finalDate = new DateOnly(2026, 8, 15);
        await controller.Save(
            KasaUstRaporFileName, finalDate, "veznedar", "bakiye",
            new[] { 0 }, approve: true, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(string.IsNullOrWhiteSpace(captured!.SourceEvidenceJson));

        // Kanıtın GERÇEKTEN aynı klasör üzerinden bağımsızca hesaplanan fingerprint ile eşleştiğini
        // doğrula — yalnızca "null değil" demek yetmez, karşılaştırmanın kendisini kanıtla.
        var independentlyComputed = await KasaSourceFingerprintHelper.CaptureAsync(
            _uploadFolder, finalDate, "Genel", NullLogger.Instance, CancellationToken.None);
        var deserialized = KasaSourceFingerprintHelper.TryDeserializeEvidence(
            captured.SourceEvidenceJson, NullLogger.Instance);

        Assert.NotNull(deserialized);
        Assert.Equal(independentlyComputed!.Fingerprint, deserialized!.Fingerprint);
    }

    [Fact]
    public async Task Save_SourceChangesBetweenImportAndSave_LeavesSourceEvidenceJsonNull_StillSavesApprovedRows()
    {
        // Race-safety (Step 6): Import öncesi bir fingerprint yakalanır; SaveAsync'ten hemen önce
        // yeniden fingerprint'lenir. Bu testte, Import ÇAĞRILDIKTAN SONRA (orchestrator mock'unun
        // Callback'i içinde) dosya içeriğini değiştirerek "request ortasında kaynak değişti" senaryosunu
        // simüle ediyoruz — VerifyAsync bunu yakalamalı ve SourceEvidenceJson'ı null bırakmalı, ama
        // satırlar yine de (kullanıcı onayladığı için) kaydedilmeli.
        WriteRealUploadFile(KasaUstRaporFileName, "before-import-content");
        var (controller, snapshots, orchestrator, _) = CreateController();
        orchestrator
            .Setup(o => o.Import(It.IsAny<string>(), ImportFileKind.KasaUstRapor))
            .Callback(() => WriteRealUploadFile(KasaUstRaporFileName, "changed-mid-request-content"))
            .Returns(Result<ImportedTable>.Success(BuildImportedTable()));

        KasaRaporSnapshot? captured = null;
        snapshots
            .Setup(s => s.SaveAsync(It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<KasaRaporSnapshot, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync((KasaRaporSnapshot s, CancellationToken _) => s);

        var finalDate = new DateOnly(2026, 8, 15);
        await controller.Save(
            KasaUstRaporFileName, finalDate, "veznedar", "bakiye",
            new[] { 0 }, approve: true, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured!.SourceEvidenceJson);
        // Onaylanan satır yine de kaydedildi — freshness-evidence eksikliği kullanıcının onayladığı
        // Kaydet işlemini engellemedi (yalnızca kanıt alanı boş kaldı).
        Assert.Single(captured.Rows);
        snapshots.Verify(s => s.SaveAsync(It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Minimal IServiceProvider — yalnızca GetUploadFolderAbsolute'un aradığı IWebHostEnvironment'ı
    /// çözer. Microsoft.Extensions.DependencyInjection'a (ServiceCollection) yeni bir paket bağımlılığı
    /// eklememek için elle yazıldı.
    /// </summary>
    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly object _service;
        public SingleServiceProvider(object service) => _service = service;
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(_service) ? _service : null;
    }
}
