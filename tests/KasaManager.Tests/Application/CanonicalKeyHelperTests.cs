using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// CanonicalKeyHelper unit testleri.
/// </summary>
public class CanonicalKeyHelperTests
{
    private static ImportedTable CreateTable(params (string canonical, string header)[] columns)
    {
        return new ImportedTable
        {
            SourceFileName = "test.xlsx",
            Kind = ImportFileKind.KasaUstRapor,
            ColumnMetas = columns.Select((c, i) => new ImportedColumnMeta
            {
                CanonicalName = c.canonical,
                OriginalHeader = c.header,
                Index = i
            }).ToList(),
            Rows = new List<Dictionary<string, string?>>()
        };
    }

    // ── FindCanonical ──

    [Fact]
    public void FindCanonical_ExactMatch_ReturnsKey()
    {
        var table = CreateTable(("islem_tutari", "İşlem Tutarı"), ("tarih", "Tarih"));
        Assert.Equal("islem_tutari", CanonicalKeyHelper.FindCanonical(table, "islem_tutari"));
    }

    [Fact]
    public void FindCanonical_TokenFallback_ReturnsKey()
    {
        var table = CreateTable(("odenecek_miktar", "Ödenecek Miktar"), ("tarih", "Tarih"));
        Assert.Equal("odenecek_miktar", CanonicalKeyHelper.FindCanonical(table, "miktar"));
    }

    [Fact]
    public void FindCanonical_NotFound_ReturnsNull()
    {
        var table = CreateTable(("islem_tutari", "İşlem Tutarı"));
        Assert.Null(CanonicalKeyHelper.FindCanonical(table, "nonexistent"));
    }

    [Fact]
    public void FindCanonical_NullMetas_ReturnsNull()
    {
        var table = new ImportedTable { SourceFileName = "x", Kind = ImportFileKind.KasaUstRapor, ColumnMetas = null!, Rows = new() };
        Assert.Null(CanonicalKeyHelper.FindCanonical(table, "any"));
    }

    // ── FindCanonicalByHeaderContains ──

    [Fact]
    public void FindCanonicalByHeaderContains_Match_ReturnsKey()
    {
        var table = CreateTable(("borc_alacak", "Borç/Alacak"), ("tutar", "Tutar"));
        Assert.Equal("borc_alacak", CanonicalKeyHelper.FindCanonicalByHeaderContains(table, "Borç"));
    }

    [Fact]
    public void FindCanonicalByHeaderContains_NoMatch_ReturnsNull()
    {
        var table = CreateTable(("tutar", "Tutar"));
        Assert.Null(CanonicalKeyHelper.FindCanonicalByHeaderContains(table, "Borç"));
    }

    // ── FindDateCanonical ──

    [Fact]
    public void FindDateCanonical_ExactTarih_ReturnsKey()
    {
        var table = CreateTable(("tarih", "Tarih"), ("tutar", "Tutar"));
        Assert.Equal("tarih", CanonicalKeyHelper.FindDateCanonical(table));
    }

    [Fact]
    public void FindDateCanonical_IslemTarihi_ReturnsKey()
    {
        var table = CreateTable(("islem_tarihi", "İşlem Tarihi"), ("tutar", "Tutar"));
        Assert.Equal("islem_tarihi", CanonicalKeyHelper.FindDateCanonical(table));
    }

    [Fact]
    public void FindDateCanonical_ContainsTarih_ReturnsKey()
    {
        var table = CreateTable(("odeme_tarihi", "Ödeme Tarihi"), ("tutar", "Tutar"));
        Assert.Equal("odeme_tarihi", CanonicalKeyHelper.FindDateCanonical(table));
    }

    [Fact]
    public void FindDateCanonical_NullMetas_ReturnsNull()
    {
        var table = new ImportedTable { SourceFileName = "x", Kind = ImportFileKind.KasaUstRapor, ColumnMetas = null!, Rows = new() };
        Assert.Null(CanonicalKeyHelper.FindDateCanonical(table));
    }
}
