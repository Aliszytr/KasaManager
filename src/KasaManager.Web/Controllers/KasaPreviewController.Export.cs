using System.Text;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;

namespace KasaManager.Web.Controllers;

// ─────────────────────────────────────────────────────────────
// KasaPreviewController — Export (Download / PDF / Excel / CSV)
// ─────────────────────────────────────────────────────────────
public sealed partial class KasaPreviewController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadJson(KasaPreviewViewModel model, CancellationToken ct)
    {
        var dto = model.ToDto();
        await _orchestrator.LoadPreviewAsync(dto, ResolveUploadFolderAbsolute(), ct);
        model.UpdateFromDto(dto);

        if (model.Drafts == null)
            return BadRequest(string.Join(", ", model.Errors));

        var json = JsonSerializer.Serialize(model.Drafts, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"kasa-draft-{model.SelectedDate:yyyy-MM-dd}.json";
        return File(bytes, "application/json; charset=utf-8", fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadBankaFisi(KasaPreviewViewModel model, CancellationToken ct)
    {
        decimal ParseForm(string name)
            => decimal.TryParse(Request.Form[name], System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        var defaults = await _globalDefaults.GetAsync(ct);
        var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";

        var data = new KasaManager.Domain.Reports.BankaFisiData
        {
            Tarih = tarih,
            KasaTuru = effectiveKasaType,
            Hazirlayan = model.KasayiYapan,
            HesapAdiStopaj = defaults.HesapAdiStopaj,
            IbanStopaj = defaults.IbanStopaj,
            TutarStopaj = ParseForm("PdfStopaj"),
            HesapAdiMasraf = defaults.HesapAdiMasraf,
            IbanMasraf = defaults.IbanMasraf,
            TutarMasraf = ParseForm("PdfTahsilat"),
            HesapAdiHarc = defaults.HesapAdiHarc,
            IbanHarc = defaults.IbanHarc,
            TutarHarc = ParseForm("PdfHarc"),
            KasadakiNakit = ParseForm("PdfKasadakiNakit"),
            DundenDevredenBanka = ParseForm("PdfDevredenBanka"),
            YarinaDevredecekBanka = ParseForm("PdfDevredecekBanka"),
        };

        var document = new KasaManager.Infrastructure.Pdf.BankaFisiDocument(data);
        var pdfBytes = document.GeneratePdf();
        var pdfName = $"banka-fisi-{tarih:yyyy-MM-dd}-{effectiveKasaType.ToLowerInvariant()}.pdf";

        return File(pdfBytes, "application/pdf", pdfName);
    }

    /// <summary>
    /// Banka Yazısı PDF indirme: Para Çekme / Virman / Özel Talimat.
    /// YaziTipi: "ParaCekme", "Virman", "OzelTalimat"
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadBankaYazisi(KasaPreviewViewModel model, string yaziTipi, CancellationToken ct)
    {
        var data = await BuildKasaRaporDataAsync(model, includeUstRapor: false, ct);

        // Muhabere Numarası — kullanıcı serbest formatta girebilir (ör: 2026/0001, MUH/2026-15)
        var muhabereNo = Request.Form["MuhabereNo"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(muhabereNo))
            data.MuhabereNo = muhabereNo.Trim();

        // Şablon: kategori bazlı ilk aktif şablonu bul, yoksa varsayılan kullan
        DocumentTemplate? template = null;
        var category = yaziTipi ?? "BankaYazisi";
        var templates = await _templateService.GetByCategoryAsync(category, ct);
        template = templates.FirstOrDefault(t => t.IsActive);

        // Varsayılan gövde metni (şablon yoksa)
        if (template == null)
        {
            template = new DocumentTemplate
            {
                Name = category,
                Category = category,
                BodyTemplate = category switch
                {
                    "ParaCekme" =>
                        """                        
                        Müdürlüğümüz veznesinde {{Tarih}} tarihinde yapılacak ödemeler için
                        kasada yeterli nakit bulunmadığından, bankamızdan {{BankadanCekilen}} tutarında
                        nakit çekilmesi gerekmektedir.

                        Gereğini arz ederim.
                        """,
                    "Virman" =>
                        """
                        Müdürlüğümüz veznesinde {{Tarih}} tarihinde tahsil edilen stopaj tutarlarının
                        aşağıda belirtilen hesaba virman yapılması gerekmektedir.

                        Toplam Stopaj Virman Tutarı: {{ToplamStopajVirman}}

                        Gereğini arz ederim.
                        """,
                    _ =>
                        """
                        Müdürlüğümüz veznesinde {{Tarih}} tarihinde aşağıdaki işlemin
                        gerçekleştirilmesi gerekmektedir.

                        Gereğini arz ederim.
                        """
                }
            };
        }

        var document = new KasaManager.Infrastructure.Pdf.BankaYazisiDocument(data, template);
        var pdfBytes = document.GeneratePdf();
        var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        var pdfName = $"banka-{category.ToLowerInvariant()}-{tarih:yyyy-MM-dd}.pdf";

        return File(pdfBytes, "application/pdf", pdfName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadGenelRapor(KasaPreviewViewModel model, CancellationToken ct)
    {
        var data = await BuildKasaRaporDataAsync(model, includeUstRapor: true, ct);
        var document = new KasaManager.Infrastructure.Pdf.KasaGenelRaporDocument(data);
        var pdfBytes = document.GeneratePdf();
        var pdfName = $"kasa-genel-rapor-{data.Tarih:yyyy-MM-dd}-{data.KasaTuru.ToLowerInvariant()}.pdf";
        return File(pdfBytes, "application/pdf", pdfName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadOzetRapor(KasaPreviewViewModel model, CancellationToken ct)
    {
        var data = await BuildKasaRaporDataAsync(model, includeUstRapor: false, ct);
        var document = new KasaManager.Infrastructure.Pdf.KasaOzetRaporDocument(data);
        var pdfBytes = document.GeneratePdf();
        var pdfName = $"kasa-ozet-rapor-{data.Tarih:yyyy-MM-dd}-{data.KasaTuru.ToLowerInvariant()}.pdf";
        return File(pdfBytes, "application/pdf", pdfName);
    }

    /// <summary>
    /// Unified Export endpoint — Excel (.xlsx) ve CSV (.csv) çıktıları.
    /// Format ve Content enum değerleri form hidden input'larından gelir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(
        KasaPreviewViewModel model,
        [FromForm] int format,
        [FromForm] int content,
        CancellationToken ct)
    {
        var exportFormat = (ExportFormat)format;
        var exportContent = (ExportContent)content;

        var includeUst = exportContent is ExportContent.GenelRapor or ExportContent.KasaUstRapor;
        var data = await BuildKasaRaporDataAsync(model, includeUstRapor: includeUst, ct);

        var request = new ExportRequest
        {
            Data = data,
            Format = exportFormat,
            Content = exportContent
        };

        var result = await _exportService.ExportAsync(request, ct);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }
}
