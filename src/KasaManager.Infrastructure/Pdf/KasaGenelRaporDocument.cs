using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// A4 Genel Kasa Raporu — Kasa Üst Rapor tablosu + tüm sonuç kartları.
/// Tek sayfaya sığacak şekilde kompakt fontlar kullanır.
/// </summary>
public sealed class KasaGenelRaporDocument : IDocument
{
    private readonly KasaRaporData _d;
    private static readonly CultureInfo Tr = new("tr-TR");

    public KasaGenelRaporDocument(KasaRaporData data) => _d = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Kasa Genel Rapor - {_d.Tarih:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Kasa Genel Rapor",
    };

    public void Compose(IDocumentContainer container)
    {
        // Çok sütunlu ÜstRapor tablolarında otomatik Landscape'e geç
        var isLandscape = _d.UstRaporKolonlar.Count > 8;
        var baseFontSize = isLandscape ? 7f : 7f;

        container.Page(page =>
        {
            page.Size(isLandscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.MarginHorizontal(isLandscape ? 15 : 20);
            page.MarginVertical(isLandscape ? 10 : 15);
            page.DefaultTextStyle(x => x.FontSize(baseFontSize));

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
            col.Item().AlignCenter().Text("KASA GENEL RAPOR")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Tarih: ").SemiBold();
                text.Span(_d.Tarih.ToString("dd MMMM yyyy, dddd", Tr));
                text.Span("  |  Kasa Türü: ").SemiBold();
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
        container.PaddingTop(3).Column(col =>
        {
            // 1) KASA ÜST RAPOR TABLOSU
            if (_d.UstRaporSatirlar.Count > 0)
            {
                col.Item().Element(ComposeUstRaporTable);
                col.Item().PaddingTop(3).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
            }

            // 2) DÜNDEN DEVREDEN + GENEL KASA
            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => ValueBox(c, "Dünden Devreden Kasa", _d.DundenDevredenKasa, "#E3F2FD"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => ValueBox(c, "Güne Ait Genel Kasa", _d.GenelKasa, "#E8F5E9"));
            });

            // 3) REDDIYAT + BANKADAN ÇIKAN + STOPAJ
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Element(c => ValueBox(c, "Online Reddiyat", _d.OnlineReddiyat, "#FFF3E0"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => ValueBox(c, "Bankadan Çıkan", _d.BankadanCikan, "#FFF3E0"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => StopajKontrolBox(c));
            });

            // 4) BANKAYA GÖTÜRÜLECEK
            col.Item().PaddingTop(3).Element(ComposeBankayaGoturulecek);

            // 5) VERGİ BİLGİLERİ
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Element(c => ValueBox(c, "Vergiden Gelen", _d.VergidenGelen, "#FFFDE7"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => ValueBox(c, "Vergi Kasa", _d.VergiKasa, "#FFFDE7"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => ValueBox(c, "Vergide Biriken Kasa", _d.VergideBirikenKasa, "#FFFDE7"));
            });

            // 6) BEKLENEN GİRİŞLER
            col.Item().PaddingTop(2).Element(ComposeBeklenenGirisler);

            // 7) EKSİK/FAZLA (sadece Sabah Kasa)
            if (_d.IsSabahKasa)
            {
                col.Item().PaddingTop(2).Element(ComposeEksikFazla);
            }

            // 8) BANKA DEVİR + KASAYI YAPAN
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Element(c => ValueBox(c, "Önceki Günden Devreden (Banka)", _d.DundenDevredenBanka, "#E3F2FD"));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => ValueBox(c, "Yarına Devredecek (Banka)", _d.YarinaDevredecekBanka, "#E3F2FD"));
            });

            // 9) GÜNLÜK KASA NOTU (sadece içerik varsa)
            if (!string.IsNullOrWhiteSpace(_d.GunlukNot))
            {
                col.Item().PaddingTop(4).Row(row =>
                {
                    // Sol amber şerit
                    row.ConstantItem(3).Background("#F59E0B").ExtendVertical();
                    row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background("#FFFBEB").Padding(6).Column(noteCol =>
                        {
                            noteCol.Item().Text("📝 Günlük Kasa Notu").FontSize(6f).SemiBold().FontColor("#92400E");
                            noteCol.Item().PaddingTop(2).Text(_d.GunlukNot).FontSize(6f).FontColor("#78350F");
                        });
                });
            }
        });
    }

    // ── ÜST RAPOR TABLOSU ───────────────────────────────────────────
    private void ComposeUstRaporTable(IContainer container)
    {
        // Dinamik font boyutu: Landscape'te daha büyük, yatay sayfa geniş (297mm)
        var colCount = _d.UstRaporKolonlar.Count;
        var headerFontSize = colCount > 14 ? 5.5f : colCount > 10 ? 6f : 7f;
        var cellFontSize   = colCount > 14 ? 5.5f : colCount > 10 ? 6f : 7f;
        var vezFontSize    = colCount > 14 ? 6f   : colCount > 10 ? 6.5f : 7f;
        // Veznedar kolonu oranı: çok sütunda daha dar
        var vezRelative = colCount > 14 ? 1.5f : colCount > 10 ? 1.8f : 2f;
        var cellPadding = colCount > 14 ? 2f : 3f;

        container.Column(col =>
        {
            col.Item().Text("Kasa Üst Rapor").FontSize(9).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(3).Table(table =>
            {
                // Sütunlar: Veznedar + dinamik kolonlar
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(vezRelative); // Veznedar adı
                    for (int i = 0; i < colCount; i++)
                        c.RelativeColumn(1);
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(cellPadding)
                        .Text("VEZNEDAR").FontSize(headerFontSize).Bold().FontColor(Colors.White);
                    foreach (var kolAd in _d.UstRaporKolonlar)
                    {
                        header.Cell().Background(Colors.Blue.Darken3).Padding(cellPadding)
                            .AlignRight()
                            .Text(ColShort(kolAd, colCount)).FontSize(headerFontSize).Bold().FontColor(Colors.White);
                    }
                });

                // Satırlar
                bool alt = false;
                foreach (var satir in _d.UstRaporSatirlar)
                {
                    var bg = alt ? Colors.Grey.Lighten5 : Colors.White;
                    table.Cell().Background(bg).Padding(cellPadding)
                        .Text(satir.VeznedarAdi).FontSize(vezFontSize).SemiBold();
                    foreach (var kolAd in _d.UstRaporKolonlar)
                    {
                        satir.Degerler.TryGetValue(kolAd, out var val);
                        table.Cell().Background(bg).Padding(cellPadding)
                            .AlignRight()
                            .Text(val ?? "0").FontSize(cellFontSize);
                    }
                    alt = !alt;
                }
            });
        });
    }

    // ── BANKAYA GÖTÜRÜLECEK ─────────────────────────────────────
    private void ComposeBankayaGoturulecek(IContainer container)
    {
        container.Border(1).BorderColor(Colors.Green.Darken1).Column(col =>
        {
            // Başlık
            col.Item().Background("#E8F5E9").Padding(2)
                .Text("BANKAYA GÖTÜRÜLECEK").FontSize(7).Bold().FontColor(Colors.Green.Darken3);

            // Tahsilat → Harç → Stopaj (stopaj virman ile yapıldığı için en sonda)
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(2)
                .Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Bankaya Yatırılacak Tahsilat").FontSize(6.5f).SemiBold();
                        if (!string.IsNullOrEmpty(_d.IbanTahsilat))
                            c.Item().Text($"{_d.HesapAdiTahsilat} — {FormatIban(_d.IbanTahsilat)}").FontSize(5f).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(80).AlignRight().AlignMiddle()
                        .Text(M(_d.BankayaTahsilat)).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                });

            // Harç
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(2)
                .Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Bankaya Yatırılacak Harç").FontSize(6.5f).SemiBold();
                        if (!string.IsNullOrEmpty(_d.IbanHarc))
                            c.Item().Text($"{_d.HesapAdiHarc} — {FormatIban(_d.IbanHarc)}").FontSize(5f).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(80).AlignRight().AlignMiddle()
                        .Text(M(_d.BankayaHarc)).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                });

            // Stopaj
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(2)
                .Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Stopaj").FontSize(6.5f).SemiBold();
                        if (!string.IsNullOrEmpty(_d.IbanStopaj))
                            c.Item().Text($"{_d.HesapAdiStopaj} — {FormatIban(_d.IbanStopaj)}").FontSize(5f).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(80).AlignRight().AlignMiddle()
                        .Text(M(_d.BankayaStopaj)).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                });

            // TOPLAM
            col.Item().Background(Colors.Amber.Lighten5).BorderTop(1.5f).BorderColor(Colors.Amber.Darken1).Padding(3)
                .Row(row =>
                {
                    row.RelativeItem().Text("BANKAYA TOPLAM").FontSize(8).Bold();
                    row.ConstantItem(100).AlignRight()
                        .Text(M(_d.BankayaToplam)).FontSize(10).Bold().FontColor(Colors.Green.Darken3);
                });

            // Stopaj Virman & Nakit
            if (_d.BankayaStopaj != 0)
            {
                col.Item().PaddingHorizontal(4).PaddingVertical(1)
                    .Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Stopaj Hesaptan Virman ").FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                            text.Span("(banka hesabından)").FontSize(5f).Italic().FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(80).AlignRight()
                            .Text($"(-) {M(_d.BankayaStopaj)}").FontSize(7).SemiBold().FontColor(Colors.Red.Darken2);
                    });

                col.Item().Background("#E8F5E9").Border(1.5f).BorderColor(Colors.Green.Darken2).Padding(3)
                    .Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("NAKİT TOPLAMI").FontSize(8).Bold().FontColor(Colors.Green.Darken3);
                            c.Item().Text("Bankaya elden götürülecek tutar").FontSize(5f).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(100).AlignRight().AlignMiddle()
                            .Text(M(_d.NakitToplam)).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                    });
            }
        });
    }

    // ── BEKLENEN GİRİŞLER ───────────────────────────────────────────
    private void ComposeBeklenenGirisler(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.LightBlue.Darken1).Column(col =>
        {
            col.Item().Background("#E1F5FE").Padding(2)
                .Text("BANKAYA BEKLENEN OLAĞAN GİRİŞLER").FontSize(6.5f).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().Padding(2).Row(row =>
            {
                row.RelativeItem().Element(c => MiniValue(c, "EFT Otomatik İade", _d.EftOtomatikIade));
                row.RelativeItem().Element(c => MiniValue(c, "Gelen Havale", _d.GelenHavale));
                row.RelativeItem().Element(c => MiniValue(c, "İade Kelimesi Giriş", _d.IadeKelimesiGiris));
            });
        });
    }

    // ── EKSİK/FAZLA ─────────────────────────────────────────────────
    private void ComposeEksikFazla(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Orange.Darken1).Column(col =>
        {
            col.Item().Background("#FFF3E0").Padding(2)
                .Text("BANKAYA HESAPLARINA BEKLENMEYEN (-,+) OLAĞANDIŞI AKIŞLAR").FontSize(6.5f).Bold().FontColor(Colors.Orange.Darken3);
            col.Item().Padding(2).Row(row =>
            {
                // Tahsilat sütunu
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("TAHSİLAT").FontSize(5.5f).Bold().FontColor(Colors.Blue.Darken2);
                    c.Item().PaddingTop(1).Element(ct => EksikFazlaSatir(ct, "Güne Ait Eksik/Fazla", _d.GuneAitEksikFazlaTahsilat));
                    c.Item().Element(ct => EksikFazlaSatir(ct, "Dünden Eksik/Fazla", _d.DundenEksikFazlaTahsilat));
                    c.Item().Element(ct => EksikFazlaSatir(ct, "Önceki Günden Gelen", _d.DundenEksikFazlaGelenTahsilat));
                });
                row.ConstantItem(1).Background(Colors.Grey.Lighten3);
                // Harç sütunu
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("HARÇ").FontSize(5.5f).Bold().FontColor(Colors.Orange.Darken2);
                    c.Item().PaddingTop(1).Element(ct => EksikFazlaSatir(ct, "Güne Ait Eksik/Fazla", _d.GuneAitEksikFazlaHarc));
                    c.Item().Element(ct => EksikFazlaSatir(ct, "Dünden Eksik/Fazla", _d.DundenEksikFazlaHarc));
                    c.Item().Element(ct => EksikFazlaSatir(ct, "Önceki Günden Gelen", _d.DundenEksikFazlaGelenHarc));
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
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Column(sigCol =>
                {
                    sigCol.Item().Text("Hazırlayan:").FontSize(5.5f).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(1).Text(_d.KasayiYapan ?? "_______________").FontSize(6.5f).SemiBold();
                });
                row.RelativeItem().AlignCenter().Column(sigCol =>
                {
                    sigCol.Item().Text("İmza:").FontSize(5.5f).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(8).Text("_______________").FontSize(6.5f);
                });
                row.RelativeItem().AlignRight().Column(sigCol =>
                {
                    sigCol.Item().Text("Tarih/Saat:").FontSize(5.5f).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(1).Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", Tr)).FontSize(5.5f);
                });
            });
        });
    }

    // ── HELPERS ──────────────────────────────────────────────────────
    private static string M(decimal v) => v.ToString("N2", Tr) + " ₺";

    private static void ValueBox(IContainer container, string label, decimal value, string bg)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(bg).Padding(2).PaddingHorizontal(4)
            .Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(label).FontSize(6.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                row.AutoItem().AlignMiddle().AlignRight().Text(M(value)).FontSize(8.5f).Bold().FontColor(Colors.Blue.Darken2);
            });
    }

    private static void MiniValue(IContainer container, string label, decimal value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(6).FontColor(Colors.Grey.Darken1);
            c.Item().Text(M(value)).FontSize(8).SemiBold();
        });
    }

    private static void EksikFazlaSatir(IContainer container, string label, decimal value)
    {
        container.PaddingVertical(1).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(6).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(60).AlignRight().Text(M(value)).FontSize(7).SemiBold();
        });
    }

    private void StopajKontrolBox(IContainer container)
    {
        var bg = _d.StopajKontrolOk ? "#E8F5E9" : "#FFEBEE";
        var icon = _d.StopajKontrolOk ? "✓" : "⚠";
        var color = _d.StopajKontrolOk ? Colors.Green.Darken2 : Colors.Red.Darken2;
        var msg = _d.StopajKontrolOk
            ? "Stopaj Kontrolü ✓ — Reddiyat − Banka Çıkış = Stopaj"
            : $"Stopaj Uyuşmazlığı — Fark: {M(_d.StopajKontrolFark)}";

        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(bg).Padding(2).PaddingHorizontal(4)
            .Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(msg).FontSize(6.5f).SemiBold().FontColor(color);
            });
    }

    private static string ColShort(string col, int totalCols = 0)
    {
        // Çok sütunlu tablolarda daha agresif kısaltma
        var maxLen = totalCols > 12 ? 8 : totalCols > 8 ? 10 : 15;
        if (col.Length > maxLen) return col[..(maxLen - 2)] + "..";
        return col;
    }

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
