using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KasaManager.Tests.Controllers;

public class KasaSettingsControllerTests
{
    [Fact]
    public async Task Index_ThrowsException_IfTryCatchIsMissing_WaitNo_VerifyItCatches_AndPopulatesErrors()
    {
        // 1. Arrange
        var mockDefaults = new Mock<IKasaGlobalDefaultsService>();
        // Veritabanı bağlantısı koptuğunda fırlatılacak gerçekçi exception
        mockDefaults.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("An error occurred while accessing the database. Connection timeout."));

        var mockSnapshots = new Mock<IKasaRaporSnapshotService>();
        var mockTemplates = new Mock<IDocumentTemplateService>();
        var mockLog = new Mock<ILogger<KasaSettingsController>>();

        var controller = new KasaSettingsController(
            mockDefaults.Object,
            mockTemplates.Object,
            mockLog.Object
        );

        // 2. Act
        Exception? capturedEx = null;
        IActionResult? result = null;
        try
        {
            result = await controller.Index(CancellationToken.None);
        }
        catch (Exception ex)
        {
            capturedEx = ex;
        }

        // 3. Assert & Print Evidences
        if (capturedEx != null)
        {
            // Eski hali: 500 fırlatırdı
            Console.WriteLine("--- ESKİ HALİ (Düzeltmeden Önce) ---");
            Console.WriteLine($"Exception: {capturedEx.GetType().Name} - {capturedEx.Message}");
            Console.WriteLine($"Stack Trace:\n{capturedEx.StackTrace}");
            Assert.Fail("Controller exceptions should have been catched!");
        }
        else
        {
            // Yeni hali: vm.Errors dolmalı
            var viewResult = Assert.IsType<ViewResult>(result);
            var vm = Assert.IsType<KasaSettingsViewModel>(viewResult.Model);

            Console.WriteLine("--- YENİ HALİ (Düzeltmeden Sonra) ---");
            Console.WriteLine($"Kullanıcıya Gösterilen Hata Sayısı: {vm.Errors.Count}");
            Console.WriteLine($"Ekranda Gösterilecek Mesaj: {vm.Errors[0]}");
            
            Assert.Single(vm.Errors);
            Assert.Contains("Ayarlar veritabanından okunamadı: An error occurred while accessing the database", vm.Errors[0]);
        }
    }
}
