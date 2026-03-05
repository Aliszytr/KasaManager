using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Comparison;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using Moq;

namespace KasaManager.Tests.Application;

/// <summary>
/// ComparisonService birim testleri.
/// Dosya bulunamadı senaryolarını ve temel hata davranışlarını test eder.
/// </summary>
public sealed class ComparisonServiceTests
{
    private readonly Mock<IImportOrchestrator> _importMock = new();
    private readonly BankaAciklamaParser _parser = new();
    private readonly ComparisonService _sut;

    public ComparisonServiceTests()
    {
        _sut = new ComparisonService(_importMock.Object, _parser);
    }

    [Fact]
    public async Task CompareTahsilatMasrafAsync_NoFiles_ReturnsFail()
    {
        // Import her dosya için Fail döner
        _importMock
            .Setup(i => i.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Dosya bulunamadı"));

        var result = await _sut.CompareTahsilatMasrafAsync(@"C:\olmayan_klasor_test", ct: CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task CompareHarcamaHarcAsync_NoFiles_ReturnsFail()
    {
        _importMock
            .Setup(i => i.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Dosya bulunamadı"));

        var result = await _sut.CompareHarcamaHarcAsync(@"C:\olmayan_klasor_test", ct: CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task CompareReddiyatCikisAsync_NoFiles_ReturnsFail()
    {
        _importMock
            .Setup(i => i.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Dosya bulunamadı"));

        var result = await _sut.CompareReddiyatCikisAsync(@"C:\olmayan_klasor_test", ct: CancellationToken.None);

        Assert.False(result.Ok);
    }
}
