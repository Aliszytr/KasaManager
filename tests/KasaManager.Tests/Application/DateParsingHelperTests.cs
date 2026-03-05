using KasaManager.Application.Services.Draft.Helpers;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// DateParsingHelper unit testleri.
/// </summary>
public class DateParsingHelperTests
{
    // ── TryParseDateOnly ──

    [Theory]
    [InlineData("19.02.2026", 2026, 2, 19)]
    [InlineData("01.01.2025", 2025, 1, 1)]
    [InlineData("1.01.2025", 2025, 1, 1)]      // d.MM.yyyy
    [InlineData("2025-01-15", 2025, 1, 15)]     // yyyy-MM-dd
    public void TryParseDateOnly_ValidFormats_ReturnsTrue(string input, int year, int month, int day)
    {
        Assert.True(DateParsingHelper.TryParseDateOnly(input, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Fact]
    public void TryParseDateOnly_ExcelSerial_ReturnsTrue()
    {
        // Excel serial 45346 = 2024-02-20 (OLE Automation date)
        Assert.True(DateParsingHelper.TryParseDateOnly("45346", out var date));
        Assert.Equal(2024, date.Year);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    public void TryParseDateOnly_Invalid_ReturnsFalse(string? input)
    {
        Assert.False(DateParsingHelper.TryParseDateOnly(input, out _));
    }

    // ── TryParseDateTime ──

    [Fact]
    public void TryParseDateTime_ValidTR_ReturnsTrue()
    {
        Assert.True(DateParsingHelper.TryParseDateTime("19.02.2026 14:30", out var dt));
        Assert.Equal(2026, dt.Year);
        Assert.Equal(2, dt.Month);
        Assert.Equal(19, dt.Day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParseDateTime_NullOrEmpty_ReturnsFalse(string? input)
    {
        Assert.False(DateParsingHelper.TryParseDateTime(input, out _));
    }

    // ── RowMatchesDate ──

    [Fact]
    public void RowMatchesDate_MatchingDate_ReturnsTrue()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "19.02.2026" };
        Assert.True(DateParsingHelper.RowMatchesDate(row, "tarih", new DateOnly(2026, 2, 19)));
    }

    [Fact]
    public void RowMatchesDate_DifferentDate_ReturnsFalse()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "18.02.2026" };
        Assert.False(DateParsingHelper.RowMatchesDate(row, "tarih", new DateOnly(2026, 2, 19)));
    }

    [Fact]
    public void RowMatchesDate_MissingColumn_ReturnsFalse()
    {
        var row = new Dictionary<string, string?> { ["other"] = "19.02.2026" };
        Assert.False(DateParsingHelper.RowMatchesDate(row, "tarih", new DateOnly(2026, 2, 19)));
    }

    [Fact]
    public void RowMatchesDate_NullDateCol_ReturnsTrue()
    {
        // null dateCol means "no date filter" → always matches
        var row = new Dictionary<string, string?> { ["x"] = "abc" };
        Assert.True(DateParsingHelper.RowMatchesDate(row, null, new DateOnly(2026, 2, 19)));
    }

    // ── RowMatchesDateRange ──

    [Fact]
    public void RowMatchesDateRange_InRange_ReturnsTrue()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "15.02.2026" };
        Assert.True(DateParsingHelper.RowMatchesDateRange(
            row, "tarih", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void RowMatchesDateRange_OutOfRange_ReturnsFalse()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "15.03.2026" };
        Assert.False(DateParsingHelper.RowMatchesDateRange(
            row, "tarih", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void RowMatchesDateRange_EdgeStart_ReturnsTrue()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "01.02.2026" };
        Assert.True(DateParsingHelper.RowMatchesDateRange(
            row, "tarih", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void RowMatchesDateRange_EdgeEnd_ReturnsTrue()
    {
        var row = new Dictionary<string, string?> { ["tarih"] = "28.02.2026" };
        Assert.True(DateParsingHelper.RowMatchesDateRange(
            row, "tarih", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }
}
