#nullable enable
using KasaManager.Application.Services.Comparison;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// ComparisonService.DetermineStopajDurum birim testleri (Commit 4).
/// Mantık, BankaHesapKontrolService.Helpers.CheckStopajFromAllVirmans ile aynı karar ağacını uygular.
/// </summary>
public class DetermineStopajDurumTests
{
    /// <summary>
    /// Senaryo: İptal yok, virman tutarı stopaj tutarına eşit veya fazla → Ok
    /// </summary>
    [Fact]
    public void VirmanYeterli_IptalYok_ReturnsOk()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var virmanlar = new List<decimal> { 1000m };
        List<CancelledPair>? iptalKayitlari = null;

        // Act
        var (status, mesaj) = ComparisonService.DetermineStopajDurum(hedefStopaj, virmanlar, iptalKayitlari);

        // Assert
        Assert.Equal(StopajStatus.Ok, status);
        Assert.Contains("✅", mesaj);
    }

    /// <summary>
    /// Senaryo: Virman iptal edildi + yerine doğru tutarla yenisi yapıldı → OkWithNote
    /// </summary>
    [Fact]
    public void IptalVar_YenisiYapilmis_ReturnsOkWithNote()
    {
        // Arrange
        decimal hedefStopaj = 1000m;
        var virmanlar = new List<decimal> { 1000m }; // Geçerli virman var
        var iptalKayitlari = new List<CancelledPair>
        {
            new CancelledPair(
                OrijinalRowIndex: 1,
                IptalRowIndex: 2,
                Tutar: 1000m,
                OrijinalTarih: new DateTime(2026, 4, 1, 10, 0, 0),
                IptalTarihi: new DateTime(2026, 4, 1, 10, 5, 0),
                Aciklama: "1.000,00 ₺ virman yapıldı, 5 dk sonra iptal edildi",
                Tur: "VIRMAN"
            )
        };

        // Act
        var (status, mesaj) = ComparisonService.DetermineStopajDurum(hedefStopaj, virmanlar, iptalKayitlari);

        // Assert
        Assert.Equal(StopajStatus.OkWithNote, status);
        Assert.Contains("ℹ️", mesaj);
        Assert.Contains("iptal edildi", mesaj);
    }

    /// <summary>
    /// Senaryo: Virman iptal edildi + yerine yenisi YAPILMAMIŞ → WarningPending
    /// </summary>
    [Fact]
    public void IptalVar_YenisiYapilmamis_ReturnsWarningPending()
    {
        // Arrange
        decimal hedefStopaj = 18940.88m;
        var virmanlar = new List<decimal>(); // Geçerli virman YOK
        var iptalKayitlari = new List<CancelledPair>
        {
            new CancelledPair(
                OrijinalRowIndex: 5,
                IptalRowIndex: 8,
                Tutar: 18940.88m,
                OrijinalTarih: new DateTime(2026, 4, 1, 14, 0, 0),
                IptalTarihi: new DateTime(2026, 4, 1, 14, 4, 0),
                Aciklama: "18.940,88 ₺ virman yapıldı, 4 dk sonra iptal edildi",
                Tur: "VIRMAN"
            )
        };

        // Act
        var (status, mesaj) = ComparisonService.DetermineStopajDurum(hedefStopaj, virmanlar, iptalKayitlari);

        // Assert
        Assert.Equal(StopajStatus.WarningPending, status);
        Assert.Contains("⚠️", mesaj);
    }
}
