#nullable enable
using System.Globalization;
using KasaManager.Domain.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KasaManager.Infrastructure.Pdf;

/// <summary>
/// Banka resmi yazıları için QuestPDF belge üreteci.
/// DocumentTemplate'teki TÜM bölümler serbest düzenlenebilir — kısıtlama yoktur.
/// 
/// Belge yapısı (sırasıyla):
///   1. Üst Başlık (HeaderText) — çok satırlı, ortalı, bold
///   2. Tarih — sağa yaslı
///   3. Sayı/Konu (SayiKonuText) — sola yaslı
///   4. Muhatap (MuhatapText) — ortalı, bold
///   5. Gövde (BodyTemplate) — blok paragraflar + girinti destekli + opsiyonel tablo
///   6. İmza Bloğu (ImzaBlokuText) — sağa yaslı
///   7. Footer (FooterText) — ortalı, küçük
/// 
/// Desteklenen placeholder'lar (TÜM alanlarda):
///   Genel:    {{Tarih}}, {{TarihSlash}}, {{GunYazi}}, {{KasaTuru}}, {{Hazirlayan}}, {{Veznedar}}
///   Tutarlar: {{BankayaToplam}}, {{NakitToplam}}, {{BankayaStopaj}}, {{BankayaTahsilat}}, {{BankayaHarc}}
///             {{DundenDevredenBanka}}, {{YarinaDevredecekBanka}}, {{BankadanCekilen}}, {{ToplamStopajVirman}}
///             {{KasadakiNakit}}
///   IBAN:     {{IbanStopaj}}, {{IbanTahsilat}}, {{IbanHarc}}
///   Hesap:    {{HesapAdiStopaj}}, {{HesapAdiTahsilat}}, {{HesapAdiHarc}}
///
/// Paragraf girintisi:
///   - Satır başında 4+ boşluk veya TAB → ilk satır girintisi (28pt)
///   - {{TAB}} → satır içinde girinti oluşturur
///
/// Tablo: Gövde metninde {{TABLO}} geçerse o noktaya yatırım tablosu yerleştirilir.
///        {{TABLO}} yoksa tablo eklenmez.
/// </summary>
public sealed class BankaYazisiDocument : IDocument
{
    private readonly KasaRaporData _data;
    private readonly DocumentTemplate? _template;
    private static readonly CultureInfo TR = new("tr-TR");

    // Paragraf ilk satır girintisi (pt cinsinden)
    private const float FirstLineIndent = 28f;

    public BankaYazisiDocument(KasaRaporData data, DocumentTemplate? template)
    {
        _data = data;
        _template = template;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(60);
            page.MarginVertical(50);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    // ═══════════════════════════════════════════════════════
    // HEADER: Kurum başlığı + Tarih
    // ═══════════════════════════════════════════════════════
    private void ComposeHeader(IContainer container)
    {
        var headerText = _template?.HeaderText ?? "T.C.\nADALET BAKANLIĞI";
        var resolvedHeader = ResolvePlaceholders(headerText);
        
        container.Column(col =>
        {
            // Kurum başlığı — çok satırlı, ortalı, bold
            col.Item().AlignCenter().Text(text =>
            {
                foreach (var line in resolvedHeader.Split('\n'))
                {
                    text.Line(line.Trim()).Bold().FontSize(13);
                }
            });

            // Tarih — sağa yaslı
            col.Item().PaddingTop(8).AlignRight().Text(text =>
            {
                text.Span(_data.Tarih.ToString("dd.MM.yyyy")).FontSize(11);
            });

            col.Item().PaddingVertical(4);
        });
    }

    // ═══════════════════════════════════════════════════════
    // CONTENT: Sayı/Konu → Muhatap → Gövde → [Tablo] → İmza
    // ═══════════════════════════════════════════════════════
    private void ComposeContent(IContainer container)
    {
        var body = _template?.BodyTemplate ?? GetDefaultTemplate();
        var resolvedBody = ResolvePlaceholders(body);

        // {{TABLO}} yer tutucu kontrolü
        bool hasTablePlaceholder = resolvedBody.Contains("{{TABLO}}", StringComparison.OrdinalIgnoreCase);

        container.Column(col =>
        {
            // ── Sayı/Konu bloğu ──
            ComposeSayiKonu(col);

            // ── Muhatap bloğu ──
            ComposeMuhatap(col);

            if (hasTablePlaceholder)
            {
                // Gövdeyi {{TABLO}} etrafında böl
                var parts = resolvedBody.Split(new[] { "{{TABLO}}", "{{tablo}}" }, StringSplitOptions.None);
                
                // Tablo öncesi metin
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                    RenderParagraphs(col, parts[0]);

                // Tablo
                col.Item().PaddingVertical(10).Element(ComposeYatirimTable);

                // Tablo sonrası metin
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    RenderParagraphs(col, parts[1]);
            }
            else
            {
                // {{TABLO}} yok → sadece gövde metni, tablo ekleme
                RenderParagraphs(col, resolvedBody);
            }

            // ── İmza bloğu ──
            ComposeImzaBloku(col);
        });
    }

    /// <summary>
    /// Paragrafları blok yazı (iki yana yaslı / justify) stili ile render eder.
    /// Çift satır sonu (\n\n) paragraf ayırıcıdır.
    /// Her paragrafın ilk satırı girintili olur (resmi yazı formatı).
    /// Girinti: PaddingLeft(30) ile ilk satır girintisi sağlanır.
    /// </summary>
    private static void RenderParagraphs(ColumnDescriptor col, string text)
    {
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paragraphs)
        {
            var trimmed = p.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Resmi yazı stili: ilk satır girintisi + iki yana yaslı (justify)
            // "      " (6 normal boşluk yerine) görünür tab boşluğu üretmek için
            // Unicode FIGURE SPACE (\u2007) kullanıyoruz — sabit genişlikli, trim'e dayanıklı.
            var indentedText = "\u2007\u2007\u2007\u2007\u2007\u2007\u2007\u2007\u2007\u2007" + trimmed;
            col.Item().PaddingBottom(6)
                .Text(t =>
                {
                    t.Justify();
                    t.DefaultTextStyle(x => x.FontSize(11).LineHeight(1.4f));
                    t.Span(indentedText);
                });
        }
    }

    // ═══════════════════════════════════════════════════════
    // SAYI / KONU — body ile aynı font boyutu
    // ═══════════════════════════════════════════════════════
    private void ComposeSayiKonu(ColumnDescriptor col)
    {
        var muhNo = _data.MuhabereNo;
        var sayiKonu = _template?.SayiKonuText;

        if (string.IsNullOrWhiteSpace(sayiKonu))
        {
            // Default Sayı/Konu — MuhabereNo varsa göster
            var sayiText = string.IsNullOrWhiteSpace(muhNo)
                ? "Sayı  : "
                : $"Sayı  : {muhNo}";
            col.Item().PaddingBottom(16).Column(inner =>
            {
                inner.Item().Text(sayiText).FontSize(10);
                inner.Item().Text("Konu : ").FontSize(10);
            });
        }
        else
        {
            var resolved = ResolvePlaceholders(sayiKonu);

            // MuhabereNo girilmişse, Sayı satırını kullanıcı değeriyle değiştir
            if (!string.IsNullOrWhiteSpace(muhNo))
            {
                resolved = System.Text.RegularExpressions.Regex.Replace(
                    resolved,
                    @"^(Sayı\s*:\s*).*$",
                    $"${{1}}{muhNo}",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            col.Item().PaddingBottom(16).Column(inner =>
            {
                foreach (var line in resolved.Split('\n'))
                {
                    inner.Item().Text(line.Trim()).FontSize(10);
                }
            });
        }
    }

    // ═══════════════════════════════════════════════════════
    // MUHATAP (banka/kurum adı + adres)
    // ═══════════════════════════════════════════════════════
    private void ComposeMuhatap(ColumnDescriptor col)
    {
        var muhatap = _template?.MuhatapText;
        if (string.IsNullOrWhiteSpace(muhatap))
            return; // Muhatap yoksa atla

        var resolved = ResolvePlaceholders(muhatap);
        col.Item().PaddingBottom(20).AlignCenter().Text(text =>
        {
            foreach (var line in resolved.Split('\n'))
            {
                text.Line(line.Trim()).Bold().FontSize(12);
            }
        });
    }

    // ═══════════════════════════════════════════════════════
    // İMZA BLOĞU
    // ═══════════════════════════════════════════════════════
    private void ComposeImzaBloku(ColumnDescriptor col)
    {
        var imzaBloku = _template?.ImzaBlokuText;

        if (string.IsNullOrWhiteSpace(imzaBloku))
        {
            // Default imza
            col.Item().PaddingTop(40).AlignRight().Column(sign =>
            {
                sign.Item().AlignRight().Text(_data.KasayiYapan ?? "Veznedar").Bold().FontSize(11);
                sign.Item().AlignRight().Text("Vezne Memuru").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }
        else
        {
            var resolved = ResolvePlaceholders(imzaBloku);
            col.Item().PaddingTop(40).AlignRight().Column(sign =>
            {
                var lines = resolved.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    // Noktalı çizgi oluştur
                    if (line == "---" || line == "…………………")
                    {
                        sign.Item().AlignRight().Text("…………………………").FontSize(9).FontColor(Colors.Grey.Darken1);
                        continue;
                    }
                    if (i == 0)
                        sign.Item().AlignRight().Text(line).Bold().FontSize(11);
                    else
                        sign.Item().AlignRight().Text(line).FontSize(9).FontColor(Colors.Grey.Darken1);
                }
            });
        }
    }

    // ═══════════════════════════════════════════════════════
    // YATIRIM TABLOSU (Stopaj, Tahsilat, Harç) — opsiyonel
    // ═══════════════════════════════════════════════════════
    private void ComposeYatirimTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3); // Kalem
                columns.RelativeColumn(3); // IBAN
                columns.RelativeColumn(2); // Tutar
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Background(Colors.Teal.Darken2).Padding(5)
                    .Text("Yatırım Kalemi").Bold().FontSize(9).FontColor(Colors.White);
                header.Cell().Background(Colors.Teal.Darken2).Padding(5)
                    .Text("IBAN").Bold().FontSize(9).FontColor(Colors.White);
                header.Cell().Background(Colors.Teal.Darken2).Padding(5)
                    .Text("Tutar (₺)").Bold().FontSize(9).FontColor(Colors.White);
            });

            // Stopaj
            DataRow(table, "Stopaj", _data.IbanStopaj, _data.BankayaStopaj, false);
            DataRow(table, "Tahsilat (Masraf)", _data.IbanTahsilat, _data.BankayaTahsilat, true);
            DataRow(table, "Harç", _data.IbanHarc, _data.BankayaHarc, false);

            // Toplam
            table.Cell()
                .Background(Colors.Teal.Lighten5)
                .BorderBottom(1).BorderColor(Colors.Teal.Darken2)
                .Padding(5)
                .Text("TOPLAM").Bold().FontSize(10);
            table.Cell()
                .Background(Colors.Teal.Lighten5)
                .BorderBottom(1).BorderColor(Colors.Teal.Darken2)
                .Padding(5)
                .Text("");
            table.Cell()
                .Background(Colors.Teal.Lighten5)
                .BorderBottom(1).BorderColor(Colors.Teal.Darken2)
                .Padding(5)
                .AlignRight()
                .Text(FormatMoney(_data.BankayaToplam)).Bold().FontSize(10);
        });
    }

    // ═══════════════════════════════════════════════════════
    // FOOTER
    // ═══════════════════════════════════════════════════════
    private void ComposeFooter(IContainer container)
    {
        var footerText = _template?.FooterText ?? "Bu belge elektronik ortamda üretilmiştir.";
        var resolved = ResolvePlaceholders(footerText);

        container.AlignCenter().Column(col =>
        {
            foreach (var line in resolved.Split('\n'))
            {
                col.Item().AlignCenter().Text(line.Trim()).FontSize(7).FontColor(Colors.Grey.Medium);
            }
        });
    }

    // ═════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════

    private static void DataRow(TableDescriptor table, string kalem, string? iban, decimal tutar, bool alt)
    {
        var bg = alt ? Colors.Grey.Lighten5 : Colors.White;

        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5)
            .Text(kalem).FontSize(9);
        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5)
            .Text(iban ?? "-").FontSize(8);
        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5)
            .AlignRight().Text(FormatMoney(tutar)).FontSize(9);
    }

    private static string FormatMoney(decimal val) => val.ToString("#,##0.00", TR) + " ₺";

    /// <summary>
    /// Tüm alanlarda kullanılabilen placeholder çözücü.
    /// </summary>
    private string ResolvePlaceholders(string template)
    {
        var result = template;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Genel
            ["{{Tarih}}"] = _data.Tarih.ToString("dd.MM.yyyy"),
            ["{{TarihSlash}}"] = _data.Tarih.ToString("dd/MM/yyyy"),
            ["{{GunYazi}}"] = _data.Tarih.ToString("dd MMMM yyyy, dddd", TR),
            ["{{KasaTuru}}"] = _data.KasaTuru,
            ["{{Hazirlayan}}"] = _data.KasayiYapan ?? "",
            ["{{Veznedar}}"] = _data.KasayiYapan ?? "",
            ["{{MuhabereNo}}"] = _data.MuhabereNo ?? "",

            // Tutarlar
            ["{{BankayaToplam}}"] = FormatMoney(_data.BankayaToplam),
            ["{{NakitToplam}}"] = FormatMoney(_data.NakitToplam),
            ["{{BankayaStopaj}}"] = FormatMoney(_data.BankayaStopaj),
            ["{{BankayaTahsilat}}"] = FormatMoney(_data.BankayaTahsilat),
            ["{{BankayaHarc}}"] = FormatMoney(_data.BankayaHarc),
            ["{{BankadanCekilen}}"] = FormatMoney(_data.BankadanCekilen),
            ["{{ToplamStopajVirman}}"] = FormatMoney(_data.ToplamStopaj),
            ["{{DundenDevredenBanka}}"] = FormatMoney(_data.DundenDevredenBanka),
            ["{{YarinaDevredecekBanka}}"] = FormatMoney(_data.YarinaDevredecekBanka),
            ["{{KasadakiNakit}}"] = FormatMoney(_data.KasadakiNakit),

            // IBAN'lar
            ["{{IbanStopaj}}"] = _data.IbanStopaj ?? "",
            ["{{IbanTahsilat}}"] = _data.IbanTahsilat ?? "",
            ["{{IbanHarc}}"] = _data.IbanHarc ?? "",

            // Hesap Adları
            ["{{HesapAdiStopaj}}"] = _data.HesapAdiStopaj ?? "",
            ["{{HesapAdiTahsilat}}"] = _data.HesapAdiTahsilat ?? "",
            ["{{HesapAdiHarc}}"] = _data.HesapAdiHarc ?? "",
        };

        foreach (var kv in replacements)
            result = result.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static string GetDefaultTemplate() =>
        """
        Müdürlüğümüz veznesinde {{Tarih}} tarihinde tahsil edilen tutarların aşağıda belirtilen hesaplara yatırılması gerekmektedir.

        Toplam yatırılacak tutar {{BankayaToplam}} olup, kalem bazında dağılımı aşağıdaki tabloda gösterilmiştir.

        Gereğini arz ederim.
        """;
}
