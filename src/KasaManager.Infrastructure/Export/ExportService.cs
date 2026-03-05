#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using QuestPDF.Fluent;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// Strategy tabanlı çoklu format export servisi.
/// Her Format+Content kombinasyonu için uygun generator seçilir.
/// </summary>
public sealed class ExportService : IExportService
{
    public Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken _ = default)
    {
        var result = (request.Format, request.Content) switch
        {
            // ── PDF ──
            (ExportFormat.Pdf_A4_Portrait, ExportContent.GenelRapor) => GenerateGenelRaporPdf(request.Data),
            (ExportFormat.Pdf_A5, ExportContent.OzetRapor)          => GenerateOzetRaporPdf(request.Data),
            (ExportFormat.Pdf_A5, ExportContent.BankaFisi)          => GenerateBankaFisiPdf(request.Data),
            (ExportFormat.Pdf_A4_Landscape, ExportContent.KasaUstRapor) => GenerateUstRaporLandscapePdf(request.Data),
            (ExportFormat.Pdf_A4_Portrait, ExportContent.BankaYazisi)   => GenerateBankaYazisiPdf(request.Data, request.Template),
            
            // ── Excel ──
            (ExportFormat.Excel_Xlsx, ExportContent.GenelRapor)    => KasaRaporExcelExporter.Export(request.Data),
            (ExportFormat.Excel_Xlsx, ExportContent.KasaUstRapor)  => KasaUstRaporExcelExporter.Export(request.Data),
            
            // ── CSV ──
            (ExportFormat.Csv, ExportContent.GenelRapor)   => GenericCsvExporter.ExportGenelRapor(request.Data),
            (ExportFormat.Csv, ExportContent.KasaUstRapor) => GenericCsvExporter.ExportUstRapor(request.Data),

            _ => throw new NotSupportedException($"Format={request.Format}, Content={request.Content} kombinasyonu desteklenmiyor.")
        };

        return Task.FromResult(result);
    }

    public IReadOnlyList<ExportOption> GetAvailableOptions() =>
    [
        new(ExportFormat.Pdf_A4_Portrait,  ExportContent.GenelRapor,    "PDF Genel Rapor (A4)",       "fas fa-file-pdf"),
        new(ExportFormat.Pdf_A5,           ExportContent.OzetRapor,     "PDF Özet Rapor (A5)",        "fas fa-file-alt"),
        new(ExportFormat.Pdf_A4_Landscape, ExportContent.KasaUstRapor,  "PDF KasaÜstRapor (Yatay)",   "fas fa-table"),
        new(ExportFormat.Pdf_A5,           ExportContent.BankaFisi,     "PDF Banka Fişi (A5)",        "fas fa-receipt"),
        new(ExportFormat.Excel_Xlsx,       ExportContent.GenelRapor,    "Excel Genel Rapor (.xlsx)",   "fas fa-file-excel"),
        new(ExportFormat.Excel_Xlsx,       ExportContent.KasaUstRapor,  "Excel KasaÜstRapor (.xlsx)", "fas fa-file-excel"),
        new(ExportFormat.Csv,              ExportContent.GenelRapor,    "CSV Genel Rapor",             "fas fa-file-csv"),
        new(ExportFormat.Csv,              ExportContent.KasaUstRapor,  "CSV KasaÜstRapor",            "fas fa-file-csv"),
        new(ExportFormat.Pdf_A4_Portrait,  ExportContent.BankaYazisi,   "Banka Resmi Yazısı (A4)",    "fas fa-stamp"),
    ];

    // ═════════════════════════════════════════════════════
    // PDF Strategy Implementations
    // ═════════════════════════════════════════════════════

    private static ExportResult GenerateGenelRaporPdf(KasaRaporData data)
    {
        var doc = new Pdf.KasaGenelRaporDocument(data);
        var bytes = doc.GeneratePdf();
        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimePdf,
            FileName = $"kasa_genel_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.pdf"
        };
    }

    private static ExportResult GenerateOzetRaporPdf(KasaRaporData data)
    {
        var doc = new Pdf.KasaOzetRaporDocument(data);
        var bytes = doc.GeneratePdf();
        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimePdf,
            FileName = $"kasa_ozet_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.pdf"
        };
    }

    private static ExportResult GenerateBankaFisiPdf(KasaRaporData data)
    {
        var fisiData = new BankaFisiData
        {
            Tarih = data.Tarih,
            KasaTuru = data.KasaTuru,
            Hazirlayan = data.KasayiYapan,
            HesapAdiStopaj = data.HesapAdiStopaj,
            IbanStopaj = data.IbanStopaj,
            TutarStopaj = data.BankayaStopaj,
            HesapAdiMasraf = data.HesapAdiTahsilat,
            IbanMasraf = data.IbanTahsilat,
            TutarMasraf = data.BankayaTahsilat,
            HesapAdiHarc = data.HesapAdiHarc,
            IbanHarc = data.IbanHarc,
            TutarHarc = data.BankayaHarc,
            KasadakiNakit = data.KasadakiNakit,
            DundenDevredenBanka = data.DundenDevredenBanka,
            YarinaDevredecekBanka = data.YarinaDevredecekBanka
        };
        var doc = new Pdf.BankaFisiDocument(fisiData);
        var bytes = doc.GeneratePdf();
        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimePdf,
            FileName = $"banka_fisi_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.pdf"
        };
    }

    private static ExportResult GenerateUstRaporLandscapePdf(KasaRaporData data)
    {
        var doc = new Pdf.KasaUstRaporLandscapeDocument(data);
        var bytes = doc.GeneratePdf();
        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimePdf,
            FileName = $"kasa_ust_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.pdf"
        };
    }

    private static ExportResult GenerateBankaYazisiPdf(KasaRaporData data, DocumentTemplate? template)
    {
        var doc = new Pdf.BankaYazisiDocument(data, template);
        var bytes = doc.GeneratePdf();
        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimePdf,
            FileName = $"banka_yazisi_{data.Tarih:yyyy-MM-dd}.pdf"
        };
    }
}
