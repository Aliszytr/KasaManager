using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// Genel Kasa Raporu — A4 Portrait PDF.
/// İki tarih arası kasa durumunu profesyonel kartlarla gösterir.
/// Sabah/Akşam kasa ile alakası yok.
/// </summary>
public sealed class GenelKasaDokumaniPdf : IDocument
{
    private readonly GenelKasaRaporData _d;
    private static readonly CultureInfo Tr = new("tr-TR");

    public GenelKasaDokumaniPdf(GenelKasaRaporData data) => _d = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Genel Kasa Raporu — {_d.BaslangicTarihi:dd.MM.yyyy} / {_d.BitisTarihi:dd.MM.yyyy}",
        Author = "KasaManager",
        Subject = "Genel Kasa Dönem Raporu",
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

    // ── HEADER ──────────────────────────────────────────────────────
    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("GENEL KASA RAPORU")
                .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(9));
                text.Span("Dönem: ").SemiBold();
                text.Span(F(_d.BaslangicTarihi)).FontColor(Colors.Blue.Darken2).SemiBold();
                text.Span(" — ").FontColor(Colors.Grey.Darken1);
                text.Span(F(_d.BitisTarihi)).FontColor(Colors.Blue.Darken2).SemiBold();
                text.Span("  |  İki Tarih Arası Kasa Durumu").FontColor(Colors.Grey.Darken1);
            });

            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken3);
        });
    }

    // ── CONTENT ─────────────────────────────────────────────────────
    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(8).Column(col =>
        {
            // ═══ 1) TARİH BİLGİLERİ ═══
            col.Item().Element(ComposeTarihKartlari);

            // ═══ 2) ANA METRİKLER ═══
            col.Item().PaddingTop(8).Element(ComposeAnaMetrikler);

            // ═══ 3) DETAY PANELLERİ ═══
            col.Item().PaddingTop(8).Element(ComposeDetayPaneller);

            // ═══ 4) SONUÇ HERO KARTLARI ═══
            col.Item().PaddingTop(10).Element(ComposeSonucHero);

            // ═══ 5) UYARILAR ═══
            if (_d.Issues.Count > 0)
            {
                col.Item().PaddingTop(8).Element(ComposeUyarilar);
            }
        });
    }

    // ── TARİH KARTLARI ──────────────────────────────────────────────
    private void ComposeTarihKartlari(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => DateCard(c, "DÖNEM BAŞLANGICI", _d.BaslangicTarihi, "#1E40AF", "#EFF6FF"));
            row.ConstantItem(6);
            row.RelativeItem().Element(c => DateCard(c, "DÖNEM BİTİŞİ", _d.BitisTarihi, "#1E40AF", "#EFF6FF"));
            row.ConstantItem(6);
            row.RelativeItem().Element(c => DateCard(c, "DEVREDEN SON TARİHİ", _d.DevredenSonTarihi, "#7C3AED", "#F5F3FF"));
        });
    }

    // ── ANA METRİKLER ───────────────────────────────────────────────
    private void ComposeAnaMetrikler(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => MetricCard(c, "DEVREDEN", _d.Devreden, "#059669", "#ECFDF5"));
            row.ConstantItem(5);
            row.RelativeItem().Element(c => MetricCard(c, "TOPLAM TAHSİLAT", _d.ToplamTahsilat, "#0891B2", "#ECFEFF"));
            row.ConstantItem(5);
            row.RelativeItem().Element(c => MetricCard(c, "TOPLAM REDDİYAT", _d.ToplamReddiyat, "#D97706", "#FFFBEB"));
            row.ConstantItem(5);
            row.RelativeItem().Element(c => MetricCard(c, "TAH. − RED. FARK", _d.TahsilatReddiyatFark, "#7C3AED", "#F5F3FF"));
        });
    }

    // ── DETAY PANELLERİ ─────────────────────────────────────────────
    private void ComposeDetayPaneller(IContainer container)
    {
        container.Row(row =>
        {
            // Sol: Banka & Devir
            row.RelativeItem().Element(c => DetailPanel(c, "BANKA & DEVİR BİLGİLERİ", "#0891B2", "#F0F9FF",
                new[] {
                    ("Banka Bakiye", _d.BankaBakiye),
                    ("Kayden Tahsilat", _d.KaydenTahsilat),
                    ("Sonraya Devredecek", _d.SonrayaDevredecek),
                }));

            row.ConstantItem(8);

            // Sağ: Kasa Detayları
            row.RelativeItem().Element(c => DetailPanel(c, "KASA DETAYLARI", "#D97706", "#FFFBEB",
                new[] {
                    ("Kasa Nakit", _d.KasaNakit),
                    ("Eksik/Fazla Makbuz", _d.EksikYadaFazla),
                    ("Gelmeyen D.", _d.Gelmeyen),
                }));
        });
    }

    // ── SONUÇ HERO ──────────────────────────────────────────────────
    private void ComposeSonucHero(IContainer container)
    {
        container.Row(row =>
        {
            // Genel Kasa
            var gkColor = _d.GenelKasa >= 0 ? "#059669" : "#DC2626";
            var gkBg = _d.GenelKasa >= 0 ? "#ECFDF5" : "#FEF2F2";
            row.RelativeItem().Element(c => HeroCard(c, "GENEL KASA", _d.GenelKasa, gkColor, gkBg,
                $"Dönem: {F(_d.BaslangicTarihi)} — {F(_d.BitisTarihi)}"));

            row.ConstantItem(8);

            // Mutabakat Farkı
            var mfOk = Math.Abs(_d.MutabakatFarki) < 0.01m;
            var mfColor = mfOk ? "#059669" : "#DC2626";
            var mfBg = mfOk ? "#ECFDF5" : "#FEF2F2";
            var mfNote = mfOk
                ? "✓ Mutabakat tamam — fark yok"
                : "⚠ Mutabakat farkı mevcut — kontrol gerekli";
            row.RelativeItem().Element(c => HeroCard(c, "MUTABAKAT FARKI (Banka − Beklenen)", _d.MutabakatFarki, mfColor, mfBg, mfNote));
        });
    }

    // ── UYARILAR ─────────────────────────────────────────────────────
    private void ComposeUyarilar(IContainer container)
    {
        container.Border(1).BorderColor("#F59E0B").Column(col =>
        {
            col.Item().Background("#FFFBEB").Padding(4)
                .Text("⚠ UYARILAR").FontSize(8).Bold().FontColor("#92400E");
            foreach (var issue in _d.Issues)
            {
                col.Item().PaddingHorizontal(6).PaddingVertical(2)
                    .Text($"• {issue}").FontSize(7).FontColor("#78350F");
            }
        });
    }

    // ── FOOTER ──────────────────────────────────────────────────────
    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(sigCol =>
                {
                    sigCol.Item().Text("Hazırlayan:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(_d.Hazirlayan ?? "_______________").FontSize(7).SemiBold();
                });
                row.RelativeItem().AlignCenter().Column(sigCol =>
                {
                    sigCol.Item().Text("İmza:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(10).Text("_______________").FontSize(7);
                });
                row.RelativeItem().AlignRight().Column(sigCol =>
                {
                    sigCol.Item().Text("Oluşturulma:").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sigCol.Item().PaddingTop(2).Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", Tr)).FontSize(6);
                });
            });
        });
    }

    // ═══════════════════════════════════════════════════════
    // COMPONENT HELPERS
    // ═══════════════════════════════════════════════════════

    private static string M(decimal v) => v.ToString("N2", Tr) + " ₺";
    private static string F(DateOnly d) => d == default ? "—" : d.ToString("dd.MM.yyyy");

    private static void DateCard(IContainer container, string label, DateOnly date, string borderColor, string bg)
    {
        container.Border(1.5f).BorderColor(borderColor).Background(bg).Padding(6).Column(col =>
        {
            col.Item().Text(label).FontSize(6.5f).Bold().FontColor(Colors.Grey.Darken1)
                .LetterSpacing(0.5f);
            col.Item().PaddingTop(3).Text(F(date)).FontSize(14).Bold().FontColor(borderColor);
        });
    }

    private static void MetricCard(IContainer container, string label, decimal value, string borderColor, string bg)
    {
        container.Border(1).BorderColor(borderColor).Background(bg).Padding(5).Column(col =>
        {
            col.Item().Text(label).FontSize(6).Bold().FontColor(Colors.Grey.Darken1)
                .LetterSpacing(0.5f);
            col.Item().PaddingTop(2).Text(M(value)).FontSize(11).Bold().FontColor(borderColor);
        });
    }

    private static void DetailPanel(IContainer container, string title, string accentColor, string bg,
        (string Label, decimal Value)[] items)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
        {
            // Header
            col.Item().Background(bg).Padding(5).Row(hRow =>
            {
                hRow.RelativeItem().Text(title).FontSize(8).Bold().FontColor(accentColor);
            });

            // Rows
            for (int i = 0; i < items.Length; i++)
            {
                var (label, value) = items[i];
                var isLast = i == items.Length - 1;
                col.Item()
                    .BorderBottom(isLast ? 0 : 0.5f).BorderColor(Colors.Grey.Lighten3)
                    .Padding(5).Row(dRow =>
                    {
                        dRow.RelativeItem().AlignMiddle()
                            .Text(label).FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                        dRow.AutoItem().AlignMiddle().AlignRight()
                            .Text(M(value)).FontSize(9).Bold();
                    });
            }
        });
    }

    private static void HeroCard(IContainer container, string label, decimal value, string color, string bg, string subtitle)
    {
        container.Border(2).BorderColor(color).Background(bg).Padding(10).Column(col =>
        {
            col.Item().Text(label).FontSize(7).Bold().FontColor(Colors.Grey.Darken1)
                .LetterSpacing(0.8f);
            col.Item().PaddingTop(4).Text(M(value)).FontSize(20).Bold().FontColor(color);
            col.Item().PaddingTop(4).Text(subtitle).FontSize(7).FontColor(Colors.Grey.Darken2);
        });
    }
}
