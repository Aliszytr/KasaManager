#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Services;
using Xunit;

namespace KasaManager.Tests.Infrastructure;

public sealed class CheckStopajFromAllVirmansTests
{
    [Fact]
    public void CheckStopajFromAllVirmans_ShouldReturnOkWithNote_WhenVirmanCancelledAndRedone()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var gecerliVirmans = new List<decimal> { 1000m }; // Doğrusu yapılmış
        var iptaller = new List<CancelledPair>
        {
            new CancelledPair(1, 2, 1000m, DateTime.Now, DateTime.Now, "Virman iptali", "VIRMAN")
        };

        // Act
        var result = BankaHesapKontrolService.CheckStopajFromAllVirmans(hedefStopaj, gecerliVirmans, iptaller);

        // Assert
        Assert.True(result.VirmanYapildiMi);
        Assert.Equal(StopajStatus.OkWithNote, result.Status);
        Assert.Contains("virman iptal edildi", result.Mesaj);
        Assert.Contains("yerine doğru tutarla yeniden yapıldı", result.Mesaj);
    }

    [Fact]
    public void CheckStopajFromAllVirmans_ShouldReturnWarningPending_WhenVirmanCancelledButNotRedone()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var gecerliVirmans = new List<decimal>(); // Yenisi yapılmamış
        var iptaller = new List<CancelledPair>
        {
            new CancelledPair(1, 2, 1000m, DateTime.Now, DateTime.Now, "Virman iptali", "VIRMAN")
        };

        // Act
        var result = BankaHesapKontrolService.CheckStopajFromAllVirmans(hedefStopaj, gecerliVirmans, iptaller);

        // Assert
        Assert.False(result.VirmanYapildiMi);
        Assert.Equal(StopajStatus.WarningPending, result.Status);
        Assert.Contains("Tüm virman işlemleri iptal edildi", result.Mesaj);
    }

    [Fact]
    public void CheckStopajFromAllVirmans_ShouldReturnError_WhenNoVirmanAndNoCancellation()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var gecerliVirmans = new List<decimal>(); // Hiç virman yok
        var iptaller = new List<CancelledPair>(); // İptal de yok

        // Act
        var result = BankaHesapKontrolService.CheckStopajFromAllVirmans(hedefStopaj, gecerliVirmans, iptaller);

        // Assert
        Assert.False(result.VirmanYapildiMi);
        Assert.Equal(StopajStatus.Error, result.Status);
        Assert.Contains("Virman yapılmadığını görüyorum", result.Mesaj);
    }

    [Fact]
    public void CheckStopajFromAllVirmans_ShouldSupportPartialVirman()
    {
        // Arrange
        decimal hedefStopaj = 16419.44m;
        var gecerliVirmans = new List<decimal> { 10000m, 6419.44m }; // Toplam: 16419.44
        var iptaller = new List<CancelledPair>();

        // Act
        var result = BankaHesapKontrolService.CheckStopajFromAllVirmans(hedefStopaj, gecerliVirmans, iptaller);

        // Assert
        Assert.True(result.VirmanYapildiMi);
        Assert.Equal(StopajStatus.Ok, result.Status);
        
        // Linux (en-US) vs Windows (tr-TR) culture farklılıklarından dolayı
        // string formatting ("16.419,44" vs "16,419.44") patlamaması için 
        // dinamik formatlayarak kontrol ediyoruz.
        string expectedFormatted = 16419.44m.ToString("N2");
        Assert.Contains(expectedFormatted, result.Mesaj);
    }

    [Fact]
    public void CheckStopajFromAllVirmans_ShouldIgnoreNonVirmanCancellations()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var gecerliVirmans = new List<decimal>(); // Virman yok
        var iptaller = new List<CancelledPair>
        {
            new CancelledPair(1, 2, 500m, DateTime.Now, DateTime.Now, "Tahsilat iptali", "TAHSILAT") // Virman değil
        };

        // Act
        var result = BankaHesapKontrolService.CheckStopajFromAllVirmans(hedefStopaj, gecerliVirmans, iptaller);

        // Assert
        Assert.False(result.VirmanYapildiMi);
        Assert.Equal(StopajStatus.Error, result.Status); // Tahsilat iptali dikkate alınmadığı için normal eksik uyarısı verir
        Assert.Contains("Virman yapılmadığını görüyorum", result.Mesaj);
    }
}
