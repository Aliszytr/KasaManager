using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// A5 boyutunda Banka Yatırma Fişi PDF belgesi.
/// QuestPDF IDocument arayüzü ile oluşturulur.
/// </summary>
public sealed class BankaFisiDocument : IDocument
{
    private readonly BankaFisiData _data;
    private static readonly CultureInfo TrCulture = new("tr-TR");

    public BankaFisiDocument(BankaFisiData data) => _data = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Banka Fişi - {_data.Tarih:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Banka Yatırma Fişi",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.MarginHorizontal(25);
            page.MarginVertical(20);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("BANKA YATIRMA FİŞİ")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(6).AlignCenter().Text(text =>
            {
                text.Span("Tarih: ").SemiBold();
                text.Span(_data.Tarih.ToString("dd MMMM yyyy, dddd", TrCulture));
            });

            col.Item().AlignCenter().Text(text =>
            {
                text.Span("Kasa Türü: ").SemiBold();
                text.Span(_data.KasaTuru);
            });

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(10).Column(col =>
        {
            // ── Hesap Blokları ──
            if (_data.TutarStopaj != 0 || !string.IsNullOrEmpty(_data.IbanStopaj))
                col.Item().Element(c => ComposeAccountBlock(c, "STOPAJ", _data.HesapAdiStopaj, _data.IbanStopaj, _data.TutarStopaj, "#FFF3E0"));

            if (_data.TutarMasraf != 0 || !string.IsNullOrEmpty(_data.IbanMasraf))
            {
                col.Item().PaddingTop(6);
                col.Item().Element(c => ComposeAccountBlock(c, "TAHSİLAT", _data.HesapAdiMasraf, _data.IbanMasraf, _data.TutarMasraf, "#E3F2FD"));
            }

            if (_data.TutarHarc != 0 || !string.IsNullOrEmpty(_data.IbanHarc))
            {
                col.Item().PaddingTop(6);
                col.Item().Element(c => ComposeAccountBlock(c, "HARÇ", _data.HesapAdiHarc, _data.IbanHarc, _data.TutarHarc, "#E8F5E9"));
            }

            // ── TOPLAM ──
            col.Item().PaddingTop(12)
                .Border(2).BorderColor(Colors.Amber.Darken1)
                .Background(Colors.Amber.Lighten5)
                .Padding(10)
                .Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text("BANKAYA TOPLAM").FontSize(10).Bold();
                    row.RelativeItem().AlignRight().Text(FormatMoney(_data.BankayaToplam)).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                });

            // ── Stopaj Virman & Nakit Toplam ──
            if (_data.TutarStopaj != 0)
            {
                // Stopaj hesaptan virman olarak düşülecek
                col.Item().PaddingTop(4).PaddingHorizontal(4)
                    .Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Stopaj Hesaptan Virman").FontSize(9).FontColor(Colors.Grey.Darken2);
                            text.Span("  (banka hesabından otomatik)").FontSize(7).Italic().FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(100).AlignRight()
                            .Text($"(-) {FormatMoney(_data.TutarStopaj)}").FontSize(10).SemiBold().FontColor(Colors.Red.Darken2);
                    });

                // NAKİT TOPLAMI — vurgulu
                var nakitToplam = _data.BankayaToplam - _data.TutarStopaj;
                col.Item().PaddingTop(6)
                    .Border(2).BorderColor(Colors.Green.Darken2)
                    .Background("#E8F5E9")
                    .Padding(10)
                    .Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Column(c =>
                        {
                            c.Item().Text("NAKİT TOPLAMI").FontSize(14).Bold().FontColor(Colors.Green.Darken3);
                            c.Item().Text("Bankaya elden götürülecek tutar").FontSize(7).FontColor(Colors.Grey.Darken1);
                        });
                        row.RelativeItem().AlignRight().AlignMiddle()
                            .Text(FormatMoney(nakitToplam)).FontSize(18).Bold().FontColor(Colors.Green.Darken3);
                    });
            }

            // ── Kasa & Banka Devir Bilgileri ──
            col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Kasadaki Nakit: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span(FormatMoney(_data.KasadakiNakit)).FontSize(8).SemiBold();
                });
            });
            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Dünden Devreden Banka: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span(FormatMoney(_data.DundenDevredenBanka)).FontSize(8).SemiBold();
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Yarına Devredecek Banka: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span(FormatMoney(_data.YarinaDevredecekBanka)).FontSize(8).SemiBold();
                });
            });
        });
    }

    private static void ComposeAccountBlock(IContainer container, string title, string? accountName, string? iban, decimal amount, string bgColor)
    {
        container
            .Border(1).BorderColor(Colors.Grey.Lighten2)
            .Background(bgColor)
            .Padding(8)
            .Column(col =>
            {
                col.Item().Text(title).FontSize(10).Bold().FontColor(Colors.Grey.Darken3);

                if (!string.IsNullOrEmpty(accountName))
                {
                    col.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("Hesap: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.Span(accountName).FontSize(8).SemiBold();
                    });
                }

                if (!string.IsNullOrEmpty(iban))
                {
                    col.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("IBAN: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.Span(FormatIban(iban)).FontSize(8).SemiBold().FontFamily("Courier New");
                    });
                }

                col.Item().PaddingTop(4).AlignRight()
                    .Text(FormatMoney(amount)).FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
            });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(6).Row(row =>
            {
                // İmza blokları
                row.RelativeItem().Column(sigCol =>
                {
                    sigCol.Item().Text("Hazırlayan:").FontSize(7).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(_data.Hazirlayan ?? "_______________").FontSize(8).SemiBold();
                });
                row.RelativeItem().AlignCenter().Column(sigCol =>
                {
                    sigCol.Item().Text("İmza:").FontSize(7).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(16).Text("_______________").FontSize(8);
                });
                row.RelativeItem().AlignRight().Column(sigCol =>
                {
                    sigCol.Item().Text("Tarih/Saat:").FontSize(7).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", TrCulture)).FontSize(7);
                });
            });
        });
    }

    // ── Helpers ──
    private static string FormatMoney(decimal value)
        => value.ToString("N2", TrCulture) + " ₺";

    private static string FormatIban(string iban)
    {
        var clean = iban.Replace(" ", "").Replace("-", "");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < clean.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(clean[i]);
        }
        return sb.ToString();
    }
}
