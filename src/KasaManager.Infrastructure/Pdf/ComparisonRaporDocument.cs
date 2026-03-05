#nullable enable
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// Karşılaştırma sonuçları PDF belgesi.
/// Fazla/Eksik kayıtları olağan/olağandışı kategorilere ayırarak raporlar.
/// Bölüm yapısı: Header → Özet İstatistikler → Fazla Gelenler → Gelmeyenler → Tutar Dengesi
/// </summary>
public sealed class ComparisonRaporDocument : IDocument
{
    private readonly ComparisonPdfData _d;
    private static readonly CultureInfo Tr = new("tr-TR");

    public ComparisonRaporDocument(ComparisonPdfData data) => _d = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Karşılaştırma Raporu - {TypeLabel(_d.Type)} - {_d.GeneratedAt:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Karşılaştırma Sonuçları",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(25);
            page.MarginVertical(20);
            page.DefaultTextStyle(x => x.FontSize(8));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // HEADER
    // ═══════════════════════════════════════════════════════════════

    private void ComposeHeader(IContainer container)
    {
        var color = TypeColor(_d.Type);

        container.Column(col =>
        {
            col.Item().AlignCenter().Text("KARŞILAŞTIRMA RAPORU")
                .FontSize(16).Bold().FontColor(color);

            col.Item().PaddingTop(3).AlignCenter().Text(TypeLabel(_d.Type))
                .FontSize(11).SemiBold().FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Tarih: ").SemiBold();
                text.Span(_d.ReportDate.HasValue
                    ? _d.ReportDate.Value.ToString("dd MMMM yyyy", Tr)
                    : "Tüm tarihler");
                text.Span("  |  Oluşturma: ").SemiBold();
                text.Span(_d.GeneratedAt.ToString("dd.MM.yyyy HH:mm"));
            });

            // Kullanıcı kararları varsa belirt
            if (_d.ApprovedCount > 0 || _d.RejectedCount > 0)
            {
                col.Item().PaddingTop(3).AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1));
                    text.Span("Kullanıcı Kararları: ").SemiBold();
                    if (_d.ApprovedCount > 0) text.Span($"{_d.ApprovedCount} onay").FontColor(Colors.Green.Darken2);
                    if (_d.ApprovedCount > 0 && _d.RejectedCount > 0) text.Span(" | ");
                    if (_d.RejectedCount > 0) text.Span($"{_d.RejectedCount} ret").FontColor(Colors.Red.Darken2);
                });
            }

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(color);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // CONTENT
    // ═══════════════════════════════════════════════════════════════

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(5).Column(col =>
        {
            // 1) Özet İstatistikler
            col.Item().Element(ComposeOzet);

            // 2) Reddiyat Stopaj Analizi (sadece ReddiyatCikis türü)
            if (_d.Type == ComparisonType.ReddiyatCikis)
            {
                col.Item().PaddingTop(8).Element(ComposeStopajAnalizi);
            }

            // 3) Olağandışı Fazla Gelenler
            if (_d.OlagandisiFazlaGelenler.Count > 0)
            {
                col.Item().PaddingTop(8).Element(c => ComposeFazlaGelenler(c,
                    "⚠ OLAĞANDIŞI FAZLA GELENLER", _d.OlagandisiFazlaGelenler, "#FFF3E0", Colors.Orange.Darken2));
            }

            // 4) Olağan Fazla Gelenler
            if (_d.OlaganFazlaGelenler.Count > 0)
            {
                col.Item().PaddingTop(8).Element(c => ComposeFazlaGelenler(c,
                    "OLAĞAN FAZLA GELENLER (EFT, İade, Havale, Virman)", _d.OlaganFazlaGelenler, "#E8F5E9", Colors.Green.Darken2));
            }

            // 5) Gelmeyenler
            if (_d.Gelmeyenler.Count > 0 || _d.ReddedilenEslesmeler.Count > 0)
            {
                col.Item().PaddingTop(8).Element(ComposeGelmeyenler);
            }

            // 6) Tutar Dengesi
            col.Item().PaddingTop(8).Element(ComposeTutarDengesi);
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Özet İstatistikler
    // ─────────────────────────────────────────────────────────────

    private void ComposeOzet(IContainer container)
    {
        var matchRate = _d.TotalOnlineRecords > 0
            ? (_d.MatchedCount * 100) / _d.TotalOnlineRecords
            : 0;

        container.Column(col =>
        {
            col.Item().Text("ÖZET İSTATİSTİKLER").FontSize(10).Bold().FontColor(Colors.Blue.Darken3);
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Online Kayıt", _d.TotalOnlineRecords.ToString(), "#E3F2FD"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Banka Kayıt", _d.TotalBankaAlacakRecords.ToString(), "#E8F5E9"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Eşleşme Oranı", $"%{matchRate}",
                    matchRate >= 80 ? "#E8F5E9" : matchRate >= 50 ? "#FFFDE7" : "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Tam Eşleşme", _d.MatchedCount.ToString(), "#E8F5E9"));
            });

            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Kısmi Eşleşme", _d.PartialMatchCount.ToString(), "#FFFDE7"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Eşleşmeyen", _d.NotFoundCount.ToString(), "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Online Tutar", Money(_d.TotalOnlineAmount), "#E3F2FD"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Eşleşen Tutar", Money(_d.TotalMatchedAmount), "#E8F5E9"));
            });
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Stopaj Analizi (Reddiyat)
    // ─────────────────────────────────────────────────────────────

    private void ComposeStopajAnalizi(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("STOPAJ VE FARK ANALİZİ").FontSize(10).Bold().FontColor(Colors.Orange.Darken2);
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Brüt Toplam", Money(_d.TotalOdenecekMiktar), "#E3F2FD"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Net Ödenecek", Money(_d.TotalNetOdenecek), "#E8F5E9"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Banka Çıkış", Money(_d.TotalBankaCikis), "#FFF3E0"));
            });

            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Gelir Vergisi", Money(_d.TotalGelirVergisi), "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Damga Vergisi", Money(_d.TotalDamgaVergisi), "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Toplam Stopaj", Money(_d.TotalStopaj), "#FFEBEE"));
            });

            if (!string.IsNullOrEmpty(_d.StopajStatus))
            {
                col.Item().PaddingTop(4).Padding(6).Background("#FFFDE7")
                    .Text(_d.StopajStatus).FontSize(7).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Fazla Gelenler tablosu (Olağan veya Olağandışı)
    // ─────────────────────────────────────────────────────────────

    private void ComposeFazlaGelenler(IContainer container, string title,
        List<UnmatchedBankaRecord> records, string bgHex, string titleColor)
    {
        var totalTutar = records.Sum(r => r.Tutar);

        container.Column(col =>
        {
            col.Item().Text(text =>
            {
                text.Span(title).FontSize(10).Bold().FontColor(titleColor);
                text.Span($"  ({records.Count} adet — {Money(totalTutar)})").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(25);   // #
                    cols.RelativeColumn(1.2f);  // Tür
                    cols.RelativeColumn(1);     // Tutar
                    cols.RelativeColumn(1);     // Tarih
                    cols.RelativeColumn(1.2f);  // Esas No
                    cols.RelativeColumn(2);     // Mahkeme / Açıklama
                });

                // Başlık
                table.Header(header =>
                {
                    header.Cell().Padding(3).Background("#ECEFF1").Text("#").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Tür").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").AlignRight().Text("Tutar").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Tarih").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Esas No").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Açıklama").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                });

                // Satırlar
                var idx = 0;
                foreach (var r in records.OrderByDescending(x => x.Tutar))
                {
                    idx++;
                    var bg = idx % 2 == 0 ? "#F5F5F5" : "#FFFFFF";
                    DataCell(table, idx.ToString(), bg);
                    DataCell(table, r.DetectedType ?? "-", bg);
                    DataCell(table, Money(r.Tutar), bg, true);
                    DataCell(table, r.Tarih?.ToString("dd.MM.yyyy") ?? "-", bg);
                    DataCell(table, r.ParsedEsasNo ?? "-", bg);
                    DataCell(table, TruncateText(r.ParsedMahkeme ?? r.Aciklama ?? "-", 40), bg);
                }
            });
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Gelmeyenler
    // ─────────────────────────────────────────────────────────────

    private void ComposeGelmeyenler(IContainer container)
    {
        var totalGelmeyen = _d.Gelmeyenler.Sum(r => r.Miktar);
        var totalReddedilen = _d.ReddedilenEslesmeler.Sum(r => r.OnlineMiktar);
        var toplamAdet = _d.Gelmeyenler.Count + _d.ReddedilenEslesmeler.Count;

        container.Column(col =>
        {
            col.Item().Text(text =>
            {
                text.Span("❌ BANKAYA GELMEYENLER").FontSize(10).Bold().FontColor(Colors.Red.Darken2);
                text.Span($"  ({toplamAdet} adet — {Money(totalGelmeyen + totalReddedilen)})")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            // Ana gelmeyenler tablosu
            if (_d.Gelmeyenler.Count > 0)
            {
                col.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(25);   // #
                        cols.RelativeColumn(1.2f);  // Dosya No
                        cols.RelativeColumn(2);     // Birim
                        cols.RelativeColumn(1);     // Tutar
                        cols.RelativeColumn(1);     // Tarih
                        cols.RelativeColumn(1.5f);  // Durum
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(3).Background("#ECEFF1").Text("#").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        header.Cell().Padding(3).Background("#ECEFF1").Text("Dosya No").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        header.Cell().Padding(3).Background("#ECEFF1").Text("Birim").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        header.Cell().Padding(3).Background("#ECEFF1").AlignRight().Text("Tutar").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        header.Cell().Padding(3).Background("#ECEFF1").Text("Tarih").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        header.Cell().Padding(3).Background("#ECEFF1").Text("Durum").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    });

                    var idx = 0;
                    foreach (var r in _d.Gelmeyenler.OrderByDescending(x => x.Miktar))
                    {
                        idx++;
                        var bg = idx % 2 == 0 ? "#F5F5F5" : "#FFFFFF";
                        DataCell(table, idx.ToString(), bg);
                        DataCell(table, r.DosyaNo ?? "-", bg);
                        DataCell(table, TruncateText(r.BirimAdi ?? "-", 35), bg);
                        DataCell(table, Money(r.Miktar), bg, true);
                        DataCell(table, r.Tarih?.ToString("dd.MM.yyyy") ?? "-", bg);
                        DataCell(table, r.Reason ?? "-", bg);
                    }
                });
            }

            // Reddedilen eşleşmeler (kullanıcı kararıyla eklenen)
            if (_d.ReddedilenEslesmeler.Count > 0)
            {
                col.Item().PaddingTop(5).Padding(6).Background("#FFF3E0").Column(inner =>
                {
                    inner.Item().Text("Kullanıcı Kararı ile Reddedilenler")
                        .FontSize(8).SemiBold().FontColor(Colors.Orange.Darken2);

                    inner.Item().PaddingTop(3).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(25);
                            cols.RelativeColumn(1.2f);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Padding(3).Background("#ECEFF1").Text("#").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                            header.Cell().Padding(3).Background("#ECEFF1").Text("Dosya No").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                            header.Cell().Padding(3).Background("#ECEFF1").Text("Birim").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                            header.Cell().Padding(3).Background("#ECEFF1").AlignRight().Text("Tutar").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                            header.Cell().Padding(3).Background("#ECEFF1").Text("Güven").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                        });

                        var idx = 0;
                        foreach (var r in _d.ReddedilenEslesmeler)
                        {
                            idx++;
                            var bg = idx % 2 == 0 ? "#F5F5F5" : "#FFFFFF";
                            DataCell(table, idx.ToString(), bg);
                            DataCell(table, r.OnlineDosyaNo ?? "-", bg);
                            DataCell(table, TruncateText(r.OnlineBirimAdi ?? "-", 35), bg);
                            DataCell(table, Money(r.OnlineMiktar), bg, true);
                            DataCell(table, $"%{(int)(r.ConfidenceScore * 100)}", bg);
                        }
                    });
                });
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Tutar Dengesi
    // ─────────────────────────────────────────────────────────────

    private void ComposeTutarDengesi(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("TUTAR DENGESİ").FontSize(10).Bold().FontColor(Colors.Blue.Darken3);
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Online Toplam", Money(_d.TotalOnlineAmount), "#E3F2FD"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Fazla Gelen",
                    $"+{Money(_d.SurplusAmount)}", "#FFF3E0"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Gelmeyen",
                    $"-{Money(_d.MissingAmount)}", "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Net Fark",
                    $"{(_d.NetAmountDifference >= 0 ? "+" : "")}{Money(_d.NetAmountDifference)}",
                    _d.NetAmountDifference >= 0 ? "#E8F5E9" : "#FFEBEE"));
            });

            if (!string.IsNullOrEmpty(_d.BalanceSummary))
            {
                col.Item().PaddingTop(4).Padding(6).Background("#F5F5F5")
                    .Text(_d.BalanceSummary).FontSize(7).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // FOOTER
    // ═══════════════════════════════════════════════════════════════

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(6).FontColor(Colors.Grey.Medium));
                    text.Span("KasaManager — ");
                    text.Span(TypeLabel(_d.Type));
                    text.Span($" — {_d.GeneratedAt:dd.MM.yyyy HH:mm}");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(6).FontColor(Colors.Grey.Medium));
                    text.Span("Sayfa ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Yardımcı metodlar
    // ═══════════════════════════════════════════════════════════════

    private static string TypeLabel(ComparisonType t) => t switch
    {
        ComparisonType.TahsilatMasraf => "Tahsilat-Masraf Karşılaştırma",
        ComparisonType.HarcamaHarc => "Harcama-Harç Karşılaştırma",
        ComparisonType.ReddiyatCikis => "Reddiyat-Çıkış Karşılaştırma",
        _ => "Karşılaştırma"
    };

    private static string TypeColor(ComparisonType t) => t switch
    {
        ComparisonType.TahsilatMasraf => Colors.Green.Darken2,
        ComparisonType.HarcamaHarc => Colors.Blue.Darken2,
        ComparisonType.ReddiyatCikis => Colors.Orange.Darken2,
        _ => Colors.Grey.Darken2
    };

    private static string Money(decimal v) => v.ToString("N2", Tr) + " ₺";

    private static string TruncateText(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";

    // ── Tablo yardımcıları ───────────────────────────────────────

    private static void DataCell(TableDescriptor table, string value, string bgHex,
        bool alignRight = false)
    {
        var cell = table.Cell().Padding(3).Background(bgHex);
        if (alignRight)
            cell.AlignRight().Text(value).FontSize(7);
        else
            cell.Text(value).FontSize(7);
    }

    private static void StatBox(IContainer container, string label, string value, string bgHex)
    {
        container.Background(bgHex).Padding(6).Column(col =>
        {
            col.Item().AlignCenter().Text(value).FontSize(10).Bold();
            col.Item().AlignCenter().Text(label).FontSize(6).FontColor(Colors.Grey.Darken1);
        });
    }
}
