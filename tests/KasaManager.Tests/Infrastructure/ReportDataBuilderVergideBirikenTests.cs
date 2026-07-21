using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Settings;
using KasaManager.Infrastructure.Export;
using Moq;
using Xunit;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// Regresyon: ReportDataBuilder, Vergide Biriken için BAĞIMSIZ kümülatif/seed formülü
/// (seed + günlük VergiKasa − günlük VergidenGelen) ÜRETMEMELİDİR.
/// SSOT = IVergideBirikenLedgerService; canonical değer caller tarafından verilir.
/// Builder saf ve deterministik transformer olarak kalır.
/// </summary>
public sealed class ReportDataBuilderVergideBirikenTests
{
    private static ReportDataBuilder CreateBuilder(decimal? seed)
    {
        var defaultsMock = new Mock<IKasaGlobalDefaultsService>();
        var settings = new KasaGlobalDefaultsSettings
        {
            Id = 1,
            VergideBirikenSeed = seed
        };
        defaultsMock.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        defaultsMock.Setup(s => s.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        return new ReportDataBuilder(defaultsMock.Object);
    }

    private static CalculationRun RunWith(decimal vergiKasa, decimal vergidenGelen)
    {
        var run = new CalculationRun { ReportDate = new DateOnly(2070, 1, 2) };
        run.Outputs[KasaCanonicalKeys.VergiKasa] = vergiKasa;
        run.Outputs[KasaCanonicalKeys.VergiGelenKasa] = vergidenGelen;
        return run;
    }

    // Test 1 — Builder bağımsız kümülatif/seed hesabı YAPMIYOR.
    // Eski formül: seed(100) + VergiKasa(80) − VergidenGelen(50) = 130.
    // Tek-gün: 80 − 50 = 30. Yeni davranış: canonical verilmediyse güvenli default 0.
    [Fact]
    public async Task BuildAsync_WithoutCanonicalValue_DoesNotComputeCumulative_DefaultsToZero()
    {
        var builder = CreateBuilder(seed: 100m);
        var run = RunWith(vergiKasa: 80m, vergidenGelen: 50m);

        var data = await builder.BuildAsync(run, "Genel", ustRaporTable: null, CancellationToken.None);

        Assert.Equal(0m, data.VergideBirikenKasa);      // güvenli default
        Assert.NotEqual(130m, data.VergideBirikenKasa);  // eski seed+gün formülü
        Assert.NotEqual(30m, data.VergideBirikenKasa);   // tek-gün formülü
        // Ham vergi alanları hâlâ doğru taşınıyor (regresyon değil):
        Assert.Equal(80m, data.VergiKasa);
        Assert.Equal(50m, data.VergidenGelen);
    }

    // Test 2 — Caller canonical değer sağlarsa builder aynen taşır (yeniden hesaplamaz).
    [Fact]
    public async Task BuildAsync_WithCanonicalValue_CarriesThroughUnchanged()
    {
        var builder = CreateBuilder(seed: 100m);
        var run = RunWith(vergiKasa: 80m, vergidenGelen: 50m);

        var data = await builder.BuildAsync(
            run, "Genel", ustRaporTable: null, CancellationToken.None, vergideBirikenKasa: 130m);

        Assert.Equal(130m, data.VergideBirikenKasa); // aynen korunur, recompute yok
    }

    // Determinizm: canonical değer sabitken seed ve günlük vergi değerleri sonucu ETKİLEMEZ.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(99999, 12345, 6789)]
    public async Task BuildAsync_WithCanonicalValue_IsIndependentOfSeedAndDailyVergi(
        decimal seed, decimal vergiKasa, decimal vergidenGelen)
    {
        var builder = CreateBuilder(seed: seed);
        var run = RunWith(vergiKasa: vergiKasa, vergidenGelen: vergidenGelen);

        var data = await builder.BuildAsync(
            run, "Genel", ustRaporTable: null, CancellationToken.None, vergideBirikenKasa: 4242m);

        Assert.Equal(4242m, data.VergideBirikenKasa);
    }

    // Seed sızıntısı yok: büyük seed olsa bile, canonical verilmediyse alan 0 kalır
    // (builder artık defaults.VergideBirikenSeed'i bu alan için OKUMUYOR).
    [Fact]
    public async Task BuildAsync_WithoutCanonicalValue_DoesNotLeakSeed()
    {
        var builder = CreateBuilder(seed: 500000m);
        var run = RunWith(vergiKasa: 1000m, vergidenGelen: 0m);

        var data = await builder.BuildAsync(run, "Genel", ustRaporTable: null, CancellationToken.None);

        Assert.Equal(0m, data.VergideBirikenKasa);
    }
}
