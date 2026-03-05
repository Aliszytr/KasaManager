#nullable enable
using System.Globalization;
using ClosedXML.Excel;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// Genel Kasa Raporu → Excel (.xlsx) çıktısı.
/// Sayfalar: "Özet Bilgiler", "KasaÜstRapor", "Detay Veriler"
/// </summary>
public static class KasaRaporExcelExporter
{
    private static readonly CultureInfo TR = new("tr-TR");

    public static ExportResult Export(KasaRaporData data)
    {
        using var wb = new XLWorkbook();

        BuildOzetSheet(wb, data);
        BuildUstRaporSheet(wb, data);
        BuildDetaySheet(wb, data);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = ExportResult.MimeXlsx,
            FileName = $"kasa_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.xlsx"
        };
    }

    // ═══════════════════════════════════════════════
    // Sheet 1: Özet Bilgiler
    // ═══════════════════════════════════════════════
    private static void BuildOzetSheet(XLWorkbook wb, KasaRaporData data)
    {
        var ws = wb.Worksheets.Add("Özet Bilgiler");
        
        // Başlık
        ws.Cell(1, 1).Value = "KASA GENEL RAPOR";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, 4).Merge();

        ws.Cell(2, 1).Value = $"Tarih: {data.Tarih:dd.MM.yyyy} | Kasa Türü: {data.KasaTuru}";
        ws.Range(2, 1, 2, 4).Merge();

        int row = 4;

        // Meta
        AddSection(ws, ref row, "META BİLGİLER");
        AddRow(ws, ref row, "Tarih", data.Tarih.ToString("dd.MM.yyyy"));
        AddRow(ws, ref row, "Kasa Türü", data.KasaTuru);
        if (!string.IsNullOrWhiteSpace(data.KasayiYapan))
            AddRow(ws, ref row, "Hazırlayan", data.KasayiYapan);

        row++;
        AddSection(ws, ref row, "KASA DEĞERLERİ");
        AddMoneyRow(ws, ref row, "Dünden Devreden Kasa", data.DundenDevredenKasa);
        AddMoneyRow(ws, ref row, "Genel Kasa", data.GenelKasa);

        row++;
        AddSection(ws, ref row, "REDDİYAT & STOPAJ");
        AddMoneyRow(ws, ref row, "Online Reddiyat", data.OnlineReddiyat);
        AddMoneyRow(ws, ref row, "Bankadan Çıkan", data.BankadanCikan);
        AddMoneyRow(ws, ref row, "Toplam Stopaj", data.ToplamStopaj);
        AddRow(ws, ref row, "Stopaj Kontrolü", data.StopajKontrolOk ? "✓ OK" : $"✗ Fark: {data.StopajKontrolFark:N2}");

        row++;
        AddSection(ws, ref row, "BANKAYA GÖTÜRÜLECEK");
        AddMoneyRow(ws, ref row, "Stopaj", data.BankayaStopaj);
        if (!string.IsNullOrWhiteSpace(data.IbanStopaj))
            AddRow(ws, ref row, "  IBAN Stopaj", data.IbanStopaj);
        AddMoneyRow(ws, ref row, "Tahsilat (Masraf)", data.BankayaTahsilat);
        if (!string.IsNullOrWhiteSpace(data.IbanTahsilat))
            AddRow(ws, ref row, "  IBAN Tahsilat", data.IbanTahsilat);
        AddMoneyRow(ws, ref row, "Harç", data.BankayaHarc);
        if (!string.IsNullOrWhiteSpace(data.IbanHarc))
            AddRow(ws, ref row, "  IBAN Harç", data.IbanHarc);
        AddMoneyRow(ws, ref row, "BANKAYA TOPLAM", data.BankayaToplam);
        AddMoneyRow(ws, ref row, "NAKİT TOPLAMI", data.NakitToplam);

        row++;
        AddSection(ws, ref row, "KASA & BANKA DEVİR");
        AddMoneyRow(ws, ref row, "Kasadaki Nakit", data.KasadakiNakit);
        AddMoneyRow(ws, ref row, "Dünden Devreden Banka", data.DundenDevredenBanka);
        AddMoneyRow(ws, ref row, "Yarına Devredecek Banka", data.YarinaDevredecekBanka);

        row++;
        AddSection(ws, ref row, "VERGİ BİLGİLERİ");
        AddMoneyRow(ws, ref row, "Vergiden Gelen", data.VergidenGelen);
        AddMoneyRow(ws, ref row, "Vergi Kasa", data.VergiKasa);
        AddMoneyRow(ws, ref row, "Vergide Biriken Kasa", data.VergideBirikenKasa);

        row++;
        AddSection(ws, ref row, "BEKLENEN OLAĞAN GİRİŞLER");
        AddMoneyRow(ws, ref row, "EFT Otomatik İade", data.EftOtomatikIade);
        AddMoneyRow(ws, ref row, "Gelen Havale", data.GelenHavale);
        AddMoneyRow(ws, ref row, "İade Kelimesi Giriş", data.IadeKelimesiGiris);

        if (data.IsSabahKasa)
        {
            row++;
            AddSection(ws, ref row, "EKSİK/FAZLA (TAHSİLAT)");
            AddMoneyRow(ws, ref row, "Güne Ait", data.GuneAitEksikFazlaTahsilat);
            AddMoneyRow(ws, ref row, "Dünden", data.DundenEksikFazlaTahsilat);
            AddMoneyRow(ws, ref row, "Dünden Gelen", data.DundenEksikFazlaGelenTahsilat);

            row++;
            AddSection(ws, ref row, "EKSİK/FAZLA (HARÇ)");
            AddMoneyRow(ws, ref row, "Güne Ait", data.GuneAitEksikFazlaHarc);
            AddMoneyRow(ws, ref row, "Dünden", data.DundenEksikFazlaHarc);
            AddMoneyRow(ws, ref row, "Dünden Gelen", data.DundenEksikFazlaGelenHarc);
        }

        // Kolon genişlikleri
        ws.Column(1).Width = 35;
        ws.Column(2).Width = 25;
        ws.Columns(3, 4).Width = 15;
    }

    // ═══════════════════════════════════════════════
    // Sheet 2: KasaÜstRapor Tablosu
    // ═══════════════════════════════════════════════
    private static void BuildUstRaporSheet(XLWorkbook wb, KasaRaporData data)
    {
        if (data.UstRaporSatirlar.Count == 0) return;

        var ws = wb.Worksheets.Add("KasaÜstRapor");

        // Header
        ws.Cell(1, 1).Value = "VEZNEDAR";
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (int c = 0; c < data.UstRaporKolonlar.Count; c++)
        {
            ws.Cell(1, c + 2).Value = data.UstRaporKolonlar[c];
            ws.Cell(1, c + 2).Style.Font.Bold = true;
        }

        // Rows
        for (int r = 0; r < data.UstRaporSatirlar.Count; r++)
        {
            var satir = data.UstRaporSatirlar[r];
            ws.Cell(r + 2, 1).Value = satir.VeznedarAdi;

            for (int c = 0; c < data.UstRaporKolonlar.Count; c++)
            {
                var kolName = data.UstRaporKolonlar[c];
                if (satir.Degerler.TryGetValue(kolName, out var val) && !string.IsNullOrWhiteSpace(val))
                {
                    if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    {
                        ws.Cell(r + 2, c + 2).Value = d;
                        ws.Cell(r + 2, c + 2).Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        ws.Cell(r + 2, c + 2).Value = val;
                    }
                }
            }
        }

        // Styling
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
        ws.Row(1).Style.Font.FontColor = XLColor.White;
        ws.Columns().AdjustToContents();
    }

    // ═══════════════════════════════════════════════
    // Sheet 3: Detay Veriler (ham key-value)
    // ═══════════════════════════════════════════════
    private static void BuildDetaySheet(XLWorkbook wb, KasaRaporData data)
    {
        var ws = wb.Worksheets.Add("Detay");

        ws.Cell(1, 1).Value = "Alan";
        ws.Cell(1, 2).Value = "Değer";
        ws.Row(1).Style.Font.Bold = true;

        var fields = GetAllFields(data);
        int row = 2;
        foreach (var kv in fields)
        {
            ws.Cell(row, 1).Value = kv.Key;
            ws.Cell(row, 2).Value = kv.Value;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    // ═══════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════
    private static void AddSection(IXLWorksheet ws, ref int row, string title)
    {
        ws.Cell(row, 1).Value = title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f9ff");
        ws.Range(row, 1, row, 4).Merge();
        row++;
    }

    private static void AddRow(IXLWorksheet ws, ref int row, string label, string value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value;
        row++;
    }

    private static void AddMoneyRow(IXLWorksheet ws, ref int row, string label, decimal value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value;
        ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
        if (label.Contains("TOPLAM") || label.Contains("GENEL"))
        {
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.Bold = true;
        }
        row++;
    }

    private static List<KeyValuePair<string, decimal>> GetAllFields(KasaRaporData data) =>
    [
        new("Dünden Devreden Kasa", data.DundenDevredenKasa),
        new("Genel Kasa", data.GenelKasa),
        new("Online Reddiyat", data.OnlineReddiyat),
        new("Bankadan Çıkan", data.BankadanCikan),
        new("Toplam Stopaj", data.ToplamStopaj),
        new("Bankaya Stopaj", data.BankayaStopaj),
        new("Bankaya Tahsilat", data.BankayaTahsilat),
        new("Bankaya Harç", data.BankayaHarc),
        new("Bankaya Toplam", data.BankayaToplam),
        new("Nakit Toplam", data.NakitToplam),
        new("Kasadaki Nakit", data.KasadakiNakit),
        new("Dünden Devreden Banka", data.DundenDevredenBanka),
        new("Yarına Devredecek Banka", data.YarinaDevredecekBanka),
        new("Vergiden Gelen", data.VergidenGelen),
        new("Vergi Kasa", data.VergiKasa),
        new("Vergide Biriken Kasa", data.VergideBirikenKasa),
        new("EFT Otomatik İade", data.EftOtomatikIade),
        new("Gelen Havale", data.GelenHavale),
        new("İade Kelimesi Giriş", data.IadeKelimesiGiris),
    ];
}
