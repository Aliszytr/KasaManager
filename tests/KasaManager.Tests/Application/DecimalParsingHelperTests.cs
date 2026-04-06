using KasaManager.Application.Services.Draft.Helpers;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// DecimalParsingHelper unit testleri.
/// </summary>
public class DecimalParsingHelperTests
{
    // ── TryParseDecimal ──

    [Theory]
    [InlineData("1.234,56", 1234.56)]  // TR format
    [InlineData("1234,56", 1234.56)]
    [InlineData("100", 100)]
    [InlineData("  50,00  ", 50.00)]    // whitespace
    public void TryParseDecimal_ValidTR_ReturnsTrue(string input, decimal expected)
    {
        Assert.True(DecimalParsingHelper.TryParseDecimal(input, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryParseDecimal_ValidEN_WithThousands_ReturnsTrue()
    {
        // "1,234.56" → invariant culture parses as 1234.56
        Assert.True(DecimalParsingHelper.TryParseDecimal("1,234.56", out var value));
        Assert.Equal(1234.56m, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void TryParseDecimal_Invalid_ReturnsFalse(string? input)
    {
        Assert.False(DecimalParsingHelper.TryParseDecimal(input, out var value));
        Assert.Equal(0m, value);
    }

    // ── ApplyDebitCreditSign ──

    [Theory]
    [InlineData(100, "BORÇ", -100)]
    [InlineData(100, "BORC", -100)]
    [InlineData(100, "Borç", -100)]
    [InlineData(200, "borc", -200)]
    public void ApplyDebitCreditSign_Borc_ReturnsNegative(decimal amount, string direction, decimal expected)
    {
        Assert.Equal(expected, DecimalParsingHelper.ApplyDebitCreditSign(amount, direction));
    }

    [Theory]
    [InlineData(100, "ALACAK", 100)]
    [InlineData(100, "Alacak", 100)]
    public void ApplyDebitCreditSign_Alacak_ReturnsPositive(decimal amount, string direction, decimal expected)
    {
        Assert.Equal(expected, DecimalParsingHelper.ApplyDebitCreditSign(amount, direction));
    }

    [Theory]
    [InlineData(100, null, 100)]
    [InlineData(100, "", 100)]
    [InlineData(100, "  ", 100)]
    public void ApplyDebitCreditSign_NullOrEmpty_ReturnsOriginal(decimal amount, string? direction, decimal expected)
    {
        Assert.Equal(expected, DecimalParsingHelper.ApplyDebitCreditSign(amount, direction));
    }

    [Theory]
    [InlineData(-1000, null, -1000)]
    [InlineData(1000, "B", -1000)]
    [InlineData(-1000, "A", 1000)]
    [InlineData(1000, null, 1000)]
    public void ApplyDebitCreditSign_SpecificRequirements_CorrectlyAppliesSign(decimal amount, string? direction, decimal expected)
    {
        Assert.Equal(expected, DecimalParsingHelper.ApplyDebitCreditSign(amount, direction));
    }

    // ── FormatDecimal ──

    [Fact]
    public void FormatDecimal_Default_ReturnsN2()
    {
        Assert.Equal("1,234.56", DecimalParsingHelper.FormatDecimal(1234.56m));
    }

    [Fact]
    public void FormatDecimal_CustomFormat_Works()
    {
        // InvariantCulture uses comma as thousands separator
        Assert.Equal("1,234.6", DecimalParsingHelper.FormatDecimal(1234.5678m, "N1"));
    }
}
