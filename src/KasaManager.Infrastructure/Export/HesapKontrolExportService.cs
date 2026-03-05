#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using QuestPDF.Fluent;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// Hesap Kontrol sonuçlarını PDF'e dönüştüren servis.
/// </summary>
public sealed class HesapKontrolExportService : IHesapKontrolExportService
{
    /// <inheritdoc />
    public Task<byte[]> ExportToPdfAsync(
        HesapKontrolDashboard dashboard,
        List<HesapKontrolKaydi> acikKayitlar,
        List<HesapKontrolKaydi> gecmisKayitlar,
        DateOnly raporTarihi,
        CancellationToken ct = default)
    {
        var document = new Pdf.HesapKontrolRaporDocument(
            dashboard, acikKayitlar, gecmisKayitlar, raporTarihi);

        var bytes = document.GeneratePdf();
        return Task.FromResult(bytes);
    }
}
