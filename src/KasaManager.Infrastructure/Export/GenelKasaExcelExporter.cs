#nullable enable
using System.Globalization;
using ClosedXML.Excel;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// Genel Kasa Raporu → Excel (.xlsx) çıktısı.
/// İki tarih arası kasa durumu kartlarını profesyonel sheet'ler halinde sunar.
/// </summary>
public static class GenelKasaExcelExporter
{
    private static readonly CultureInfo TR = new("tr-TR");

    public static ExportResult Export(GenelKasaRaporData data)
    {
        using var wb = new XLWorkbook();

        BuildOzetSheet(wb, data);
        BuildDetaySheet(wb, data);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = ExportResult.MimeXlsx,
            FileName = $"genel_kasa_{data.BaslangicTarihi:yyyy-MM-dd}_{data.BitisTarihi:yyyy-MM-dd}.xlsx"
        };
    }

    // ═══════════════════════════════════════════════
    // Sheet 1: Profesyonel Özet
    // ═══════════════════════════════════════════════
    private static void BuildOzetSheet(XLWorkbook wb, GenelKasaRaporData data)
    {
        var ws = wb.Worksheets.Add("Genel Kasa Raporu");

        // ── Başlık ──
        ws.Cell(1, 1).Value = "GENEL KASA RAPORU";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1E40AF");
        ws.Range(1, 1, 1, 4).Merge();

        ws.Cell(2, 1).Value = $"Dönem: {F(data.BaslangicTarihi)} — {F(data.BitisTarihi)} | İki Tarih Arası Kasa Durumu";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
        ws.Range(2, 1, 2, 4).Merge();

        int row = 4;

        // ── Tarih Bilgileri ──
        AddSection(ws, ref row, "TARİH BİLGİLERİ", "#EFF6FF", "#1E40AF");
        AddDateRow(ws, ref row, "Dönem Başlangıcı", data.BaslangicTarihi);
        AddDateRow(ws, ref row, "Dönem Bitişi", data.BitisTarihi);
        AddDateRow(ws, ref row, "Devreden Son Tarihi", data.DevredenSonTarihi);

        row++;

        // ── Ana Metrikler ──
        AddSection(ws, ref row, "ANA METRİKLER", "#ECFDF5", "#059669");
        AddMoneyRow(ws, ref row, "Devreden", data.Devreden);
        AddMoneyRow(ws, ref row, "Toplam Tahsilat", data.ToplamTahsilat);
        AddMoneyRow(ws, ref row, "Toplam Reddiyat", data.ToplamReddiyat);
        AddMoneyRow(ws, ref row, "Tahsilat − Reddiyat Fark", data.TahsilatReddiyatFark, bold: true);

        row++;

        // ── Banka & Devir ──
        AddSection(ws, ref row, "BANKA & DEVİR BİLGİLERİ", "#ECFEFF", "#0891B2");
        AddMoneyRow(ws, ref row, "Banka Bakiye", data.BankaBakiye);
        AddMoneyRow(ws, ref row, "Kayden Tahsilat", data.KaydenTahsilat);
        AddMoneyRow(ws, ref row, "Sonraya Devredecek", data.SonrayaDevredecek);

        row++;

        // ── Kasa Detayları ──
        AddSection(ws, ref row, "KASA DETAYLARI", "#FFFBEB", "#D97706");
        AddMoneyRow(ws, ref row, "Kasa Nakit", data.KasaNakit);
        AddMoneyRow(ws, ref row, "Eksik/Fazla Makbuz", data.EksikYadaFazla);
        AddMoneyRow(ws, ref row, "Gelmeyen D.", data.Gelmeyen);

        row++;

        // ── Sonuçlar ──
        AddSection(ws, ref row, "SONUÇLAR", "#F0FDF4", "#059669");
        AddMoneyRow(ws, ref row, "GENEL KASA", data.GenelKasa, bold: true, highlight: true);
        AddMoneyRow(ws, ref row, "MUTABAKAT FARKI (Banka − Beklenen)", data.MutabakatFarki, bold: true, highlight: true);

        var mfOk = Math.Abs(data.MutabakatFarki) < 0.01m;
        ws.Cell(row, 1).Value = mfOk ? "✓ Mutabakat tamam — fark yok" : "⚠ Mutabakat farkı mevcut — kontrol gerekli";
        ws.Cell(row, 1).Style.Font.FontColor = mfOk ? XLColor.FromHtml("#059669") : XLColor.FromHtml("#DC2626");
        ws.Cell(row, 1).Style.Font.Italic = true;
        row++;

        // ── Hazırlayan ──
        if (!string.IsNullOrWhiteSpace(data.Hazirlayan))
        {
            row++;
            ws.Cell(row, 1).Value = "Hazırlayan:";
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
            ws.Cell(row, 2).Value = data.Hazirlayan;
            ws.Cell(row, 2).Style.Font.Bold = true;
        }

        // Kolon genişlikleri
        ws.Column(1).Width = 38;
        ws.Column(2).Width = 28;
        ws.Columns(3, 4).Width = 15;
    }

    // ═══════════════════════════════════════════════
    // Sheet 2: Ham Detay (key-value)
    // ═══════════════════════════════════════════════
    private static void BuildDetaySheet(XLWorkbook wb, GenelKasaRaporData data)
    {
        var ws = wb.Worksheets.Add("Detay");

        ws.Cell(1, 1).Value = "Alan";
        ws.Cell(1, 2).Value = "Değer";
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
        ws.Row(1).Style.Font.FontColor = XLColor.White;

        var fields = new (string Key, decimal Value)[]
        {
            ("Devreden", data.Devreden),
            ("Toplam Tahsilat", data.ToplamTahsilat),
            ("Toplam Reddiyat", data.ToplamReddiyat),
            ("Tahsilat − Reddiyat Fark", data.TahsilatReddiyatFark),
            ("Banka Bakiye", data.BankaBakiye),
            ("Kayden Tahsilat", data.KaydenTahsilat),
            ("Sonraya Devredecek", data.SonrayaDevredecek),
            ("Kasa Nakit", data.KasaNakit),
            ("Eksik/Fazla Makbuz", data.EksikYadaFazla),
            ("Gelmeyen D.", data.Gelmeyen),
            ("Genel Kasa", data.GenelKasa),
            ("Mutabakat Farkı", data.MutabakatFarki),
        };

        int row = 2;
        foreach (var (key, value) in fields)
        {
            ws.Cell(row, 1).Value = key;
            ws.Cell(row, 2).Value = value;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    // ═══════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════
    private static string F(DateOnly d) => d == default ? "—" : d.ToString("dd.MM.yyyy");

    private static void AddSection(IXLWorksheet ws, ref int row, string title, string bg, string color)
    {
        ws.Cell(row, 1).Value = title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 11;
        ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(color);
        ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
        ws.Range(row, 1, row, 4).Merge();
        ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
        row++;
    }

    private static void AddDateRow(IXLWorksheet ws, ref int row, string label, DateOnly date)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#374151");
        ws.Cell(row, 2).Value = F(date);
        ws.Cell(row, 2).Style.Font.Bold = true;
        row++;
    }

    private static void AddMoneyRow(IXLWorksheet ws, ref int row, string label, decimal value,
        bool bold = false, bool highlight = false)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#374151");
        ws.Cell(row, 2).Value = value;
        ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";

        if (bold)
        {
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.Bold = true;
        }
        if (highlight)
        {
            ws.Cell(row, 2).Style.Font.FontSize = 12;
            ws.Cell(row, 2).Style.Font.FontColor = value >= 0
                ? XLColor.FromHtml("#059669")
                : XLColor.FromHtml("#DC2626");
        }
        row++;
    }
}
