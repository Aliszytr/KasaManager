#nullable enable
using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// Hesap Kontrol sonuçları PDF belgesi.
/// Bölüm yapısı: Header → Dashboard Özet → Açık Kayıtlar → Geçmiş Kayıtlar → Footer
/// </summary>
public sealed class HesapKontrolRaporDocument : IDocument
{
    private readonly HesapKontrolDashboard _dashboard;
    private readonly List<HesapKontrolKaydi> _acik;
    private readonly List<HesapKontrolKaydi> _gecmis;
    private readonly DateOnly _raporTarihi;

    private static readonly CultureInfo Tr = new("tr-TR");

    public HesapKontrolRaporDocument(
        HesapKontrolDashboard dashboard,
        List<HesapKontrolKaydi> acikKayitlar,
        List<HesapKontrolKaydi> gecmisKayitlar,
        DateOnly raporTarihi)
    {
        _dashboard = dashboard;
        _acik = acikKayitlar;
        _gecmis = gecmisKayitlar;
        _raporTarihi = raporTarihi;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Hesap Kontrol Raporu - {_raporTarihi:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Banka Hesap Kontrol Sonuçları",
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
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("HESAP KONTROL RAPORU")
                .FontSize(16).Bold().FontColor(Colors.Indigo.Darken2);

            col.Item().PaddingTop(3).AlignCenter().Text("Banka Hesap Kontrol Sonuçları")
                .FontSize(11).SemiBold().FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Rapor Tarihi: ").SemiBold();
                text.Span(_raporTarihi.ToString("dd MMMM yyyy", Tr));
                text.Span("  |  Oluşturma: ").SemiBold();
                text.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            });

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Indigo.Darken2);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // CONTENT
    // ═══════════════════════════════════════════════════════════════

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(5).Column(col =>
        {
            // 1) Dashboard Özet
            col.Item().Element(ComposeDashboard);

            // 2) Açık Kayıtlar
            if (_acik.Count > 0)
            {
                col.Item().PaddingTop(8).Element(c => ComposeKayitTablosu(c,
                    $"📋 AÇIK KAYITLAR ({_acik.Count} adet)",
                    _acik, Colors.Orange.Darken2));
            }
            else
            {
                col.Item().PaddingTop(8).Padding(8).Background("#E8F5E9")
                    .AlignCenter().Text("✅ Açık kayıt bulunmuyor — tüm farklar çözülmüş.")
                    .FontSize(9).FontColor(Colors.Green.Darken2);
            }

            // 3) Geçmiş Kayıtlar
            if (_gecmis.Count > 0)
            {
                col.Item().PaddingTop(8).Element(c => ComposeKayitTablosu(c,
                    $"📁 GEÇMİŞ KAYITLAR ({_gecmis.Count} adet)",
                    _gecmis, Colors.Blue.Darken2));
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Dashboard Özet
    // ─────────────────────────────────────────────────────────────

    private void ComposeDashboard(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("DASHBOARD ÖZET").FontSize(10).Bold().FontColor(Colors.Indigo.Darken2);
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Açık Kayıt",
                    _dashboard.AcikKayitSayisi.ToString(),
                    _dashboard.AcikKayitSayisi > 0 ? "#FFF3E0" : "#E8F5E9"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Beklenen",
                    _dashboard.BeklenenSayisi.ToString(), "#E3F2FD"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Bilinmeyen",
                    _dashboard.BilinmeyenSayisi.ToString(),
                    _dashboard.BilinmeyenSayisi > 0 ? "#FFEBEE" : "#E8F5E9"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Bugün Çözülen",
                    _dashboard.BugunCozulenSayisi.ToString(), "#E8F5E9"));
            });

            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Açık Eksik Toplam",
                    Money(_dashboard.AcikEksikToplam), "#FFEBEE"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Açık Fazla Toplam",
                    Money(_dashboard.AcikFazlaToplam), "#FFF3E0"));
                row.ConstantItem(5);
                row.RelativeItem().Element(c => StatBox(c, "Net Fark",
                    Money(_dashboard.AcikFazlaToplam - _dashboard.AcikEksikToplam),
                    "#E3F2FD"));
                row.ConstantItem(5);
                // Stopaj durumu
                row.RelativeItem().Element(c => StatBox(c, "Stopaj Virman",
                    _dashboard.LastStopajDurum?.VirmanYapildiMi == true ? "✓ Yapıldı" : "—",
                    _dashboard.LastStopajDurum?.VirmanYapildiMi == true ? "#E8F5E9" : "#F5F5F5"));
            });
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Bölüm: Kayıt Tablosu (Açık veya Geçmiş)
    // ─────────────────────────────────────────────────────────────

    private void ComposeKayitTablosu(IContainer container, string title,
        List<HesapKontrolKaydi> kayitlar, string titleColor)
    {
        var totalTutar = kayitlar.Sum(k => k.Tutar);

        container.Column(col =>
        {
            col.Item().Text(text =>
            {
                text.Span(title).FontSize(10).Bold().FontColor(titleColor);
                text.Span($"  — Toplam: {Money(totalTutar)}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(22);    // #
                    cols.RelativeColumn(0.8f);  // Tarih
                    cols.RelativeColumn(0.6f);  // Hesap Türü
                    cols.RelativeColumn(0.4f);  // Yön
                    cols.RelativeColumn(0.7f);  // Tutar
                    cols.RelativeColumn(0.5f);  // Sınıf
                    cols.RelativeColumn(0.5f);  // Durum
                    cols.RelativeColumn(0.9f);  // Tip
                    cols.RelativeColumn(1.5f);  // Açıklama
                });

                // Başlık
                table.Header(header =>
                {
                    header.Cell().Padding(3).Background("#ECEFF1").Text("#").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Tarih").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Hesap Türü").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Yön").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").AlignRight().Text("Tutar").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Sınıf").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Durum").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Tip").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                    header.Cell().Padding(3).Background("#ECEFF1").Text("Açıklama").FontSize(7).SemiBold().FontColor(Colors.Grey.Darken3);
                });

                // Satırlar
                var idx = 0;
                foreach (var k in kayitlar.OrderByDescending(x => x.AnalizTarihi).ThenByDescending(x => x.Tutar))
                {
                    idx++;
                    var bg = idx % 2 == 0 ? "#F5F5F5" : "#FFFFFF";

                    DataCell(table, idx.ToString(), bg);
                    DataCell(table, k.AnalizTarihi.ToString("dd.MM.yyyy"), bg);
                    DataCell(table, HesapTuruLabel(k.HesapTuru), bg);
                    DataCell(table, YonLabel(k.Yon), bg);
                    DataCell(table, Money(k.Tutar), bg, true);
                    DataCell(table, SinifLabel(k.Sinif), bg);
                    DataCell(table, DurumLabel(k.Durum), bg);
                    DataCell(table, TipLabel(k.TespitEdilenTip), bg);
                    DataCell(table, TruncateText(k.Aciklama ?? k.DosyaNo ?? k.BirimAdi ?? "—", 40), bg);
                }
            });
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
                    text.Span("KasaManager — Hesap Kontrol Raporu");
                    text.Span($" — {DateTime.Now:dd.MM.yyyy HH:mm}");
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

    private static string HesapTuruLabel(BankaHesapTuru t) => t switch
    {
        BankaHesapTuru.Tahsilat => "Tahsilat",
        BankaHesapTuru.Harc => "Harç",
        BankaHesapTuru.Stopaj => "Stopaj",
        _ => t.ToString()
    };

    private static string YonLabel(KayitYonu y) => y switch
    {
        KayitYonu.Eksik => "Eksik",
        KayitYonu.Fazla => "Fazla",
        _ => y.ToString()
    };

    private static string SinifLabel(FarkSinifi s) => s switch
    {
        FarkSinifi.Beklenen => "Beklenen",
        FarkSinifi.Askida => "Askıda",
        FarkSinifi.Bilinmeyen => "Bilinmeyen",
        _ => s.ToString()
    };

    private static string DurumLabel(KayitDurumu d) => d switch
    {
        KayitDurumu.Acik => "Açık",
        KayitDurumu.Cozuldu => "Çözüldü",
        KayitDurumu.Onaylandi => "Onaylandı",
        KayitDurumu.Iptal => "İptal",
        _ => d.ToString()
    };

    private static string TipLabel(string? tip) => tip switch
    {
        "EFT_OTOMATIK_IADE" => "EFT Otomatik İade",
        "GELEN_HAVALE" => "Gelen Havale",
        "MEVDUAT_YATIRMA" => "Mevduata Yatırma",
        "VIRMAN" => "Virman",
        "MASRAF" => "Masraf",
        "REDDIYAT" => "Reddiyat",
        "IADE" => "İade",
        null or "" => "—",
        _ => tip
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
