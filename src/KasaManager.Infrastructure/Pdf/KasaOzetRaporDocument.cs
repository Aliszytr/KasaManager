using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// A5 Özet Rapor — Kasa Üst Rapor hariç, vurgulu kart değerleri.
/// Tek sayfaya sığacak şekilde kompakt ama okunaklı.
/// </summary>
public sealed class KasaOzetRaporDocument : IDocument
{
    private readonly KasaRaporData _d;
    private static readonly CultureInfo Tr = new("tr-TR");

    public KasaOzetRaporDocument(KasaRaporData data) => _d = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Kasa Özet Rapor - {_d.Tarih:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Kasa Özet Rapor",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.MarginHorizontal(20);
            page.MarginVertical(15);
            page.DefaultTextStyle(x => x.FontSize(7.5f));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    // ── HEADER ──────────────────────────────────────────────────────
    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("KASA ÖZET RAPOR")
                .FontSize(14).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(3).AlignCenter().Text(text =>
            {
                text.Span("Tarih: ").SemiBold();
                text.Span(_d.Tarih.ToString("dd MMMM yyyy, dddd", Tr));
            });
            col.Item().AlignCenter().Text(text =>
            {
                text.Span("Kasa Türü: ").SemiBold();
                text.Span(_d.KasaTuru);
                if (!string.IsNullOrEmpty(_d.KasayiYapan))
                {
                    text.Span("  |  Kasayı Yapan: ").SemiBold();
                    text.Span(_d.KasayiYapan);
                }
            });

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    // ── CONTENT ─────────────────────────────────────────────────────
    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(6).Column(col =>
        {
            // 1) DÜNDEN DEVREDEN + GENEL KASA
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => CardBox(c, "Dünden Devreden Kasa", _d.DundenDevredenKasa, "#E3F2FD"));
                row.ConstantItem(6);
                row.RelativeItem().Element(c => CardBox(c, "Güne Ait Genel Kasa", _d.GenelKasa, "#E8F5E9"));
            });

            // 2) BANKAYA GÖTÜRÜLECEK
            col.Item().PaddingTop(6).Element(ComposeBankayaGoturulecek);

            // 3) VERGİ BİLGİLERİ
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Element(c => CardBox(c, "Vergiden Gelen", _d.VergidenGelen, "#FFFDE7"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => CardBox(c, "Vergi Kasa", _d.VergiKasa, "#FFFDE7"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => CardBox(c, "Vergide Biriken", _d.VergideBirikenKasa, "#FFFDE7"));
            });

            // 4) BANKA DEVİR
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Element(c => CardBox(c, "Önceki Günden Devreden (Banka)", _d.DundenDevredenBanka, "#E3F2FD"));
                row.ConstantItem(6);
                row.RelativeItem().Element(c => CardBox(c, "Yarına Devredecek (Banka)", _d.YarinaDevredecekBanka, "#E3F2FD"));
            });

            // 5) EKSİK/FAZLA (sadece Sabah)
            if (_d.IsSabahKasa)
            {
                col.Item().PaddingTop(6).Element(ComposeEksikFazla);
            }

            // 6) GÜNLÜK KASA NOTU (sadece içerik varsa)
            if (!string.IsNullOrWhiteSpace(_d.GunlukNot))
            {
                col.Item().PaddingTop(6).Row(row =>
                {
                    // Sol amber şerit
                    row.ConstantItem(3).Background("#F59E0B").ExtendVertical();
                    row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background("#FFFBEB").Padding(8).Column(noteCol =>
                        {
                            noteCol.Item().Text("📝 Günlük Kasa Notu").FontSize(7f).SemiBold().FontColor("#92400E");
                            noteCol.Item().PaddingTop(3).Text(_d.GunlukNot).FontSize(7f).FontColor("#78350F");
                        });
                });
            }
        });
    }

    // ── BANKAYA GÖTÜRÜLECEK ─────────────────────────────────────────
    private void ComposeBankayaGoturulecek(IContainer container)
    {
        container.Border(1.5f).BorderColor(Colors.Green.Darken1).Column(col =>
        {
            col.Item().Background("#E8F5E9").Padding(5)
                .Text("BANKAYA GÖTÜRÜLECEK").FontSize(9).Bold().FontColor(Colors.Green.Darken3);

            // Kalem satırları
            col.Item().Element(c => BankaKalem(c, "Stopaj", _d.BankayaStopaj));
            col.Item().Element(c => BankaKalem(c, "Tahsilat", _d.BankayaTahsilat));
            col.Item().Element(c => BankaKalem(c, "Harç", _d.BankayaHarc));

            // TOPLAM
            col.Item().Background(Colors.Amber.Lighten5).BorderTop(1.5f).BorderColor(Colors.Amber.Darken1)
                .Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("BANKAYA TOPLAM").FontSize(10).Bold();
                    row.RelativeItem().AlignRight()
                        .Text(M(_d.BankayaToplam)).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                });

            // Stopaj Virman & Nakit Toplam
            if (_d.BankayaStopaj != 0)
            {
                col.Item().PaddingHorizontal(6).PaddingVertical(2).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Stopaj Hesaptan Virman ").FontSize(7.5f).FontColor(Colors.Grey.Darken2);
                        text.Span("(banka hesabından)").FontSize(6).Italic().FontColor(Colors.Grey.Medium);
                    });
                    row.ConstantItem(80).AlignRight()
                        .Text($"(-) {M(_d.BankayaStopaj)}").FontSize(9).SemiBold().FontColor(Colors.Red.Darken2);
                });

                col.Item().Background("#E8F5E9").Border(1.5f).BorderColor(Colors.Green.Darken2)
                    .Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("NAKİT TOPLAMI").FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                            c.Item().Text("Bankaya elden götürülecek tutar").FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                        });
                        row.RelativeItem().AlignRight().AlignMiddle()
                            .Text(M(_d.NakitToplam)).FontSize(16).Bold().FontColor(Colors.Green.Darken3);
                    });
            }
        });
    }

    // ── EKSİK/FAZLA ─────────────────────────────────────────────────
    private void ComposeEksikFazla(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Orange.Darken1).Column(col =>
        {
            col.Item().Background("#FFF3E0").Padding(3)
                .Text("OLAĞANDIŞI AKIŞLAR").FontSize(7).Bold().FontColor(Colors.Orange.Darken3);
            col.Item().Padding(4).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("TAHSİLAT").FontSize(6).Bold();
                    c.Item().Element(ct => EkFaSatir(ct, "Güne Ait", _d.GuneAitEksikFazlaTahsilat));
                    c.Item().Element(ct => EkFaSatir(ct, "Dünden", _d.DundenEksikFazlaTahsilat));
                    c.Item().Element(ct => EkFaSatir(ct, "Öncekinden", _d.DundenEksikFazlaGelenTahsilat));
                });
                row.ConstantItem(1).Background(Colors.Grey.Lighten3);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("HARÇ").FontSize(6).Bold();
                    c.Item().Element(ct => EkFaSatir(ct, "Güne Ait", _d.GuneAitEksikFazlaHarc));
                    c.Item().Element(ct => EkFaSatir(ct, "Dünden", _d.DundenEksikFazlaHarc));
                    c.Item().Element(ct => EkFaSatir(ct, "Öncekinden", _d.DundenEksikFazlaGelenHarc));
                });
            });
        });
    }

    // ── FOOTER ──────────────────────────────────────────────────────
    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(sigCol =>
                {
                    sigCol.Item().Text("Hazırlayan:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(_d.KasayiYapan ?? "_______________").FontSize(7).SemiBold();
                });
                row.RelativeItem().AlignCenter().Column(sigCol =>
                {
                    sigCol.Item().Text("İmza:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(12).Text("_______________").FontSize(7);
                });
                row.RelativeItem().AlignRight().Column(sigCol =>
                {
                    sigCol.Item().Text("Tarih/Saat:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", Tr)).FontSize(6);
                });
            });
        });
    }

    // ── HELPERS ──────────────────────────────────────────────────────
    private static string M(decimal v) => v.ToString("N2", Tr) + " ₺";

    private static void CardBox(IContainer container, string label, decimal value, string bg)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(bg).Padding(6)
            .Column(col =>
            {
                col.Item().Text(label).FontSize(7).SemiBold().FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(2).AlignRight().Text(M(value)).FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
            });
    }

    private static void BankaKalem(IContainer container, string label, decimal value)
    {
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5)
            .Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8).SemiBold();
                row.ConstantItem(80).AlignRight().Text(M(value)).FontSize(10).Bold().FontColor(Colors.Blue.Darken2);
            });
    }

    private static void EkFaSatir(IContainer container, string label, decimal value)
    {
        container.PaddingVertical(1).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(6).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(50).AlignRight().Text(M(value)).FontSize(7).SemiBold();
        });
    }
}
