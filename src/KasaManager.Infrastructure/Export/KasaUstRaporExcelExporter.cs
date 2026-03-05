#nullable enable
using System.Globalization;
using ClosedXML.Excel;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// KasaÜstRapor tablosunu bağımsız Excel dosyası olarak çıktılar.
/// Tüm sütunlar dahil — kırpma yok.
/// </summary>
public static class KasaUstRaporExcelExporter
{
    public static ExportResult Export(KasaRaporData data)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("KasaÜstRapor");

        if (data.UstRaporSatirlar.Count == 0)
        {
            ws.Cell(1, 1).Value = "KasaÜstRapor verisi bulunamadı.";
        }
        else
        {
            // Başlık satırı
            ws.Cell(1, 1).Value = $"KASA ÜST RAPOR — {data.Tarih:dd.MM.yyyy} — {data.KasaTuru}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, data.UstRaporKolonlar.Count + 1).Merge();

            // Header
            int headerRow = 3;
            ws.Cell(headerRow, 1).Value = "VEZNEDAR";
            for (int c = 0; c < data.UstRaporKolonlar.Count; c++)
                ws.Cell(headerRow, c + 2).Value = data.UstRaporKolonlar[c];

            // Header styling
            var headerRange = ws.Range(headerRow, 1, headerRow, data.UstRaporKolonlar.Count + 1);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Data rows
            for (int r = 0; r < data.UstRaporSatirlar.Count; r++)
            {
                var satir = data.UstRaporSatirlar[r];
                int dataRow = headerRow + 1 + r;

                ws.Cell(dataRow, 1).Value = satir.VeznedarAdi;

                // Toplam satırını vurgula
                bool isToplam = satir.VeznedarAdi.Contains("TOPLAM", StringComparison.OrdinalIgnoreCase);
                if (isToplam)
                {
                    ws.Row(dataRow).Style.Font.Bold = true;
                    ws.Row(dataRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#ecfdf5");
                }

                for (int c = 0; c < data.UstRaporKolonlar.Count; c++)
                {
                    var kolName = data.UstRaporKolonlar[c];
                    if (satir.Degerler.TryGetValue(kolName, out var val) && !string.IsNullOrWhiteSpace(val))
                    {
                        if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        {
                            ws.Cell(dataRow, c + 2).Value = d;
                            ws.Cell(dataRow, c + 2).Style.NumberFormat.Format = "#,##0.00";
                        }
                        else
                        {
                            ws.Cell(dataRow, c + 2).Value = val;
                        }
                    }
                }
            }

            // Border ve auto-fit
            var tableRange = ws.Range(headerRow, 1, headerRow + data.UstRaporSatirlar.Count, data.UstRaporKolonlar.Count + 1);
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = ExportResult.MimeXlsx,
            FileName = $"kasa_ust_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.xlsx"
        };
    }
}
