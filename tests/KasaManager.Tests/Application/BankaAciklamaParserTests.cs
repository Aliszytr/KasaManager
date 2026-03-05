using KasaManager.Application.Services.Comparison;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// BankaAciklamaParser unit testleri.
/// </summary>
public class BankaAciklamaParserTests
{
    private readonly BankaAciklamaParser _parser = new();

    // ── Parse ──

    [Fact]
    public void Parse_NullOrEmpty_ReturnsZeroConfidence()
    {
        var result = _parser.Parse(null);
        Assert.Equal(0, result.Confidence);
        Assert.Null(result.Il);
        Assert.Null(result.Mahkeme);
        Assert.Null(result.EsasNo);
    }

    [Fact]
    public void Parse_WithAnkaraKeyword_ReturnsAnkara()
    {
        var aciklama = "Portal Tahsilatıdır. Ankara 5. Vergi Mahkemesi-2024/950";
        var result = _parser.Parse(aciklama);

        Assert.Equal("Ankara", result.Il);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public void Parse_WithEsasNo_ExtractsCorrectly()
    {
        var aciklama = "Bölge-Ankara 3. İdare Mahkemesi-2025/1234 İdare Dava Dosyası";
        var result = _parser.Parse(aciklama);

        Assert.Equal("Ankara", result.Il);
        Assert.NotNull(result.EsasNo);
        Assert.Contains("2025/1234", result.EsasNo);
    }

    [Fact]
    public void Parse_WithMahkeme_ExtractsMahkeme()
    {
        var aciklama = "-Ankara 12. Vergi Mahkemesi-2024/500";
        var result = _parser.Parse(aciklama);

        Assert.Equal("Ankara", result.Il);
        Assert.NotNull(result.Mahkeme);
        Assert.Contains("Vergi", result.Mahkeme);
    }

    [Fact]
    public void Parse_NoAnkaraKeyword_LowConfidence()
    {
        // No Ankara keyword, no mahkeme, no esas no → very low confidence
        var aciklama = "İstanbul Beşiktaş Şubesi transfer EFT";
        var result = _parser.Parse(aciklama);

        Assert.True(result.Confidence < 0.5, $"Ankara olmayan açıklama düşük confidence olmalı, actual: {result.Confidence}");
    }

    // ── IsAnkaraRelated ──

    [Theory]
    [InlineData("Portal Tahsilatıdır. Ankara 5. Vergi Mahkemesi-2024/950", true)]
    [InlineData("Bölge-Ankara 3. İdare Mahkemesi", true)]
    [InlineData("-Ankara 12. Vergi Mahkemesi", true)]
    [InlineData("İstanbul 5. Vergi Mahkemesi-2024/100", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAnkaraRelated_ReturnsExpected(string? aciklama, bool expected)
    {
        Assert.Equal(expected, _parser.IsAnkaraRelated(aciklama));
    }

    // ── ParseReddiyatGonderici ──

    [Fact]
    public void ParseReddiyatGonderici_ValidFormat_ExtractsGonderenBilgileri()
    {
        var aciklama = "Ankara 5. Vergi Mahkemesi-2024/950 Vergi-Ankara 1. Vergi Dava Dairesi-2025/441";
        var result = _parser.ParseReddiyatGonderici(aciklama);

        Assert.NotNull(result.GonderenMahkeme);
        Assert.Contains("Vergi", result.GonderenMahkeme);
        Assert.Equal("2024/950", result.GonderenEsasNo);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public void ParseReddiyatGonderici_NullInput_ReturnsEmpty()
    {
        var result = _parser.ParseReddiyatGonderici(null);
        Assert.Equal(0, result.Confidence);
        Assert.Null(result.GonderenMahkeme);
    }
}
