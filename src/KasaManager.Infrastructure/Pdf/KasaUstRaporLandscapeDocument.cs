#nullable enable
using System.Globalization;
using KasaManager.Domain.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// KasaÜstRapor tablosunu A4 Landscape formatında basan QuestPDF belgesi.
/// 14+ sütunu kırpmadan, tam genişlikte gösterir.
/// Sütun sayısına göre dinamik font boyutu: > 10 → 5pt, > 14 → 4pt.
/// </summary>
public sealed class KasaUstRaporLandscapeDocument : IDocument
{
    private readonly KasaRaporData _data;
    private float _fontSize;

    public KasaUstRaporLandscapeDocument(KasaRaporData data)
    {
        _data = data;
        // Dinamik font boyutu
        var colCount = data.UstRaporKolonlar.Count + 1; // +1 for Veznedar
        _fontSize = colCount switch
        {
            > 16 => 4f,
            > 13 => 4.5f,
            > 10 => 5f,
            > 7  => 5.5f,
            _    => 6f
        };
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.MarginHorizontal(15);
            page.MarginVertical(12);
            page.DefaultTextStyle(x => x.FontSize(_fontSize));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("KASA ÜST RAPOR").Bold().FontSize(12);
                    left.Item().Text($"Tarih: {_data.Tarih:dd MMMM yyyy, dddd}  |  Kasa Türü: {_data.KasaTuru}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Teal.Darken2);
        });
    }

    private void ComposeContent(IContainer container)
    {
        if (_data.UstRaporSatirlar.Count == 0)
        {
            container.PaddingTop(20).AlignCenter().Text("KasaÜstRapor verisi bulunamadı.")
                .FontSize(10).FontColor(Colors.Red.Medium);
            return;
        }

        container.Table(table =>
        {
            // Kolon tanımları
            var colCount = _data.UstRaporKolonlar.Count + 1;
            table.ColumnsDefinition(columns =>
            {
                // Veznedar kolonu daha geniş
                columns.RelativeColumn(2.5f);
                for (int i = 0; i < _data.UstRaporKolonlar.Count; i++)
                    columns.RelativeColumn(1);
            });

            // ── Header ──
            table.Header(header =>
            {
                header.Cell()
                    .Background(Colors.Teal.Darken2)
                    .Padding(3).AlignCenter()
                    .Text("VEZNEDAR").FontSize(_fontSize).Bold().FontColor(Colors.White);

                foreach (var col in _data.UstRaporKolonlar)
                {
                    header.Cell()
                        .Background(Colors.Teal.Darken2)
                        .Padding(3).AlignCenter()
                        .Text(col).FontSize(_fontSize).Bold().FontColor(Colors.White);
                }
            });

            // ── Data Rows ──
            for (int r = 0; r < _data.UstRaporSatirlar.Count; r++)
            {
                var satir = _data.UstRaporSatirlar[r];
                bool isToplam = satir.VeznedarAdi.Contains("TOPLAM", StringComparison.OrdinalIgnoreCase);
                string bgColor = isToplam
                    ? Colors.Teal.Lighten5
                    : (r % 2 == 0 ? Colors.White : Colors.Grey.Lighten5);

                // Veznedar
                var vezCell = table.Cell()
                    .Background(bgColor)
                    .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                    .Padding(3)
                    .Text(satir.VeznedarAdi)
                    .FontSize(_fontSize);
                if (isToplam) vezCell.Bold();

                // Value columns
                foreach (var col in _data.UstRaporKolonlar)
                {
                    satir.Degerler.TryGetValue(col, out var val);
                    var displayVal = FormatValue(val);

                    var valCell = table.Cell()
                        .Background(bgColor)
                        .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                        .Padding(3)
                        .AlignRight()
                        .Text(displayVal)
                        .FontSize(_fontSize);
                    if (isToplam) valCell.Bold();
                }
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(6).FontColor(Colors.Grey.Medium));
            text.Span($"Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm}  —  Sayfa ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    // ═════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════

    private static string FormatValue(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return "";
        if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d.ToString("#,##0.##", new CultureInfo("tr-TR"));
        return val;
    }
}
