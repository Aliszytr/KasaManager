using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// Genel Kasa Raporu — A5 Özet PDF.
/// Kompakt: sadece tarih + sonuç metrikleri.
/// </summary>
public sealed class GenelKasaOzetPdf : IDocument
{
    private readonly GenelKasaRaporData _d;
    private static readonly CultureInfo Tr = new("tr-TR");

    public GenelKasaOzetPdf(GenelKasaRaporData data) => _d = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Genel Kasa Özet — {_d.BaslangicTarihi:dd.MM.yyyy} / {_d.BitisTarihi:dd.MM.yyyy}",
        Author = "KasaManager",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.MarginHorizontal(15);
            page.MarginVertical(12);
            page.DefaultTextStyle(x => x.FontSize(7));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("GENEL KASA RAPORU — ÖZET")
                .FontSize(13).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(3).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span(F(_d.BaslangicTarihi)).SemiBold().FontColor(Colors.Blue.Darken2);
                text.Span(" — ").FontColor(Colors.Grey.Darken1);
                text.Span(F(_d.BitisTarihi)).SemiBold().FontColor(Colors.Blue.Darken2);
            });

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(6).Column(col =>
        {
            // Tablo şeklinde tüm metrikler
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3); // Label
                    c.RelativeColumn(2); // Value
                });

                AddGroupHeader(table, "TARİH BİLGİLERİ");
                AddDateRow(table, "Dönem Başlangıcı", _d.BaslangicTarihi);
                AddDateRow(table, "Dönem Bitişi", _d.BitisTarihi);
                AddDateRow(table, "Devreden Son Tarihi", _d.DevredenSonTarihi);

                AddGroupHeader(table, "ANA METRİKLER");
                AddMoneyRow(table, "Devreden", _d.Devreden);
                AddMoneyRow(table, "Toplam Tahsilat", _d.ToplamTahsilat);
                AddMoneyRow(table, "Toplam Reddiyat", _d.ToplamReddiyat);
                AddMoneyRow(table, "Tahsilat − Reddiyat Fark", _d.TahsilatReddiyatFark, true);

                AddGroupHeader(table, "BANKA & DEVİR");
                AddMoneyRow(table, "Banka Bakiye", _d.BankaBakiye);
                AddMoneyRow(table, "Kayden Tahsilat", _d.KaydenTahsilat);
                AddMoneyRow(table, "Sonraya Devredecek", _d.SonrayaDevredecek);

                AddGroupHeader(table, "KASA DETAYLARI");
                AddMoneyRow(table, "Kasa Nakit", _d.KasaNakit);
                AddMoneyRow(table, "Eksik/Fazla Makbuz", _d.EksikYadaFazla);
                AddMoneyRow(table, "Gelmeyen D.", _d.Gelmeyen);
            });

            // Hero kutular
            col.Item().PaddingTop(8).Row(row =>
            {
                var gkColor = _d.GenelKasa >= 0 ? "#059669" : "#DC2626";
                row.RelativeItem().Border(1.5f).BorderColor(gkColor)
                    .Background(_d.GenelKasa >= 0 ? "#ECFDF5" : "#FEF2F2")
                    .Padding(6).Column(c =>
                    {
                        c.Item().Text("GENEL KASA").FontSize(6).Bold().FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(2).Text(M(_d.GenelKasa)).FontSize(14).Bold().FontColor(gkColor);
                    });

                row.ConstantItem(5);

                var mfOk = Math.Abs(_d.MutabakatFarki) < 0.01m;
                var mfColor = mfOk ? "#059669" : "#DC2626";
                row.RelativeItem().Border(1.5f).BorderColor(mfColor)
                    .Background(mfOk ? "#ECFDF5" : "#FEF2F2")
                    .Padding(6).Column(c =>
                    {
                        c.Item().Text("MUTABAKAT FARKI").FontSize(6).Bold().FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(2).Text(M(_d.MutabakatFarki)).FontSize(14).Bold().FontColor(mfColor);
                        c.Item().PaddingTop(1).Text(mfOk ? "✓ Fark yok" : "⚠ Kontrol gerekli")
                            .FontSize(5.5f).FontColor(mfColor);
                    });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(_d.Hazirlayan ?? "").FontSize(5.5f);
            row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", Tr)).FontSize(5.5f).FontColor(Colors.Grey.Darken1);
        });
    }

    // ═══ Helpers ═══
    private static string M(decimal v) => v.ToString("N2", Tr) + " ₺";
    private static string F(DateOnly d) => d == default ? "—" : d.ToString("dd.MM.yyyy");

    private static void AddGroupHeader(TableDescriptor table, string title)
    {
        table.Cell().ColumnSpan(2)
            .Background("#EFF6FF").Padding(3)
            .Text(title).FontSize(7).Bold().FontColor(Colors.Blue.Darken2);
    }

    private static void AddDateRow(TableDescriptor table, string label, DateOnly date)
    {
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3)
            .Text(label).FontSize(7).FontColor(Colors.Grey.Darken2);
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3)
            .AlignRight().Text(F(date)).FontSize(7.5f).SemiBold();
    }

    private static void AddMoneyRow(TableDescriptor table, string label, decimal value, bool bold = false)
    {
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3)
            .Text(label).FontSize(7).FontColor(Colors.Grey.Darken2);

        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3)
            .AlignRight();
        if (bold)
            cell.Text(M(value)).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
        else
            cell.Text(M(value)).FontSize(7.5f).SemiBold();
    }
}
