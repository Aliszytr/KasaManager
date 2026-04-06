using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using Moq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KasaManager.Tests.Application;

/// <summary>
/// ImportOrchestrator birim testleri.
/// GuessKindFromFileName, Import ve ImportTrueSource davranışlarını test eder.
/// </summary>
public sealed class ImportOrchestratorTests
{
    private readonly Mock<IExcelTableReader> _excelMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<ILogger<ImportOrchestrator>> _loggerMock = new();
    private readonly ImportOrchestrator _sut;

    public ImportOrchestratorTests()
    {
        _sut = new ImportOrchestrator(_excelMock.Object, _scopeFactoryMock.Object, _loggerMock.Object);
    }

    // ───────────────────────────────────────────
    // GuessKindFromFileName
    // ───────────────────────────────────────────

    [Theory]
    [InlineData("BankaTahsilat.xlsx", ImportFileKind.BankaTahsilat)]
    [InlineData("BankaHarcama_2026.xlsx", ImportFileKind.BankaHarcama)]
    [InlineData("OnlineMasraf_Ocak.xlsx", ImportFileKind.MasrafVeReddiyat)]  // 'masraf' matches MasrafVeReddiyat before online check
    [InlineData("OnlineHarc.xlsx", ImportFileKind.OnlineHarcama)]
    [InlineData("OnlineReddiyat.xlsx", ImportFileKind.MasrafVeReddiyat)]  // 'reddiyat' matches MasrafVeReddiyat before online check
    [InlineData("MasrafveReddiyat.xlsx", ImportFileKind.MasrafVeReddiyat)]
    [InlineData("KasaUstRapor.xlsx", ImportFileKind.KasaUstRapor)]
    [InlineData("rapor_bilinmeyen.xlsx", ImportFileKind.Unknown)]
    public void GuessKindFromFileName_ReturnsExpected(string fileName, ImportFileKind expected)
    {
        var result = _sut.GuessKindFromFileName(fileName);
        Assert.Equal(expected, result);
    }

    // ───────────────────────────────────────────
    // Import / ImportTrueSource — dosya bulunamadı
    // ───────────────────────────────────────────

    [Fact]
    public void Import_EmptyPath_ReturnsFail()
    {
        var result = _sut.Import("", ImportFileKind.BankaTahsilat);
        Assert.False(result.Ok);
        Assert.Contains("boş", result.Error!);
    }

    [Fact]
    public void Import_NonExistentFile_ReturnsFail()
    {
        var result = _sut.Import(@"C:\olmayan_dosya_test_12345.xlsx", ImportFileKind.BankaTahsilat);
        Assert.False(result.Ok);
        Assert.Contains("bulunamadı", result.Error!);
    }

    [Fact]
    public void ImportTrueSource_EmptyPath_ReturnsFail()
    {
        var result = _sut.ImportTrueSource("", ImportFileKind.MasrafVeReddiyat);
        Assert.False(result.Ok);
        Assert.Contains("boş", result.Error!);
    }

    [Fact]
    public void ImportTrueSource_NonExistentFile_ReturnsFail()
    {
        var result = _sut.ImportTrueSource(@"C:\olmayan_truesource_test_12345.xlsx", ImportFileKind.MasrafVeReddiyat);
        Assert.False(result.Ok);
        Assert.Contains("bulunamadı", result.Error!);
    }
}
