#nullable enable
using System.Globalization;
using System.Text;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// CSV çıktı üreteci. UTF-8 BOM ile Türkçe karakter desteği.
/// Ayracı noktalı virgül (;) — Türk Excel'de doğru açılır.
/// </summary>
public static class GenericCsvExporter
{
    private const string Sep = ";";
    private static readonly CultureInfo TR = new("tr-TR");

    // ═══════════════════════════════════════════════
    // Genel Rapor CSV
    // ═══════════════════════════════════════════════
    public static ExportResult ExportGenelRapor(KasaRaporData data)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"KASA GENEL RAPOR{Sep}{data.Tarih:dd.MM.yyyy}{Sep}{data.KasaTuru}");
        sb.AppendLine();
        sb.AppendLine($"Alan{Sep}Değer");

        AppendField(sb, "Dünden Devreden Kasa", data.DundenDevredenKasa);
        AppendField(sb, "Genel Kasa", data.GenelKasa);
        sb.AppendLine();

        AppendField(sb, "Online Reddiyat", data.OnlineReddiyat);
        AppendField(sb, "Bankadan Çıkan", data.BankadanCikan);
        AppendField(sb, "Toplam Stopaj", data.ToplamStopaj);
        sb.AppendLine($"Stopaj Kontrolü{Sep}{(data.StopajKontrolOk ? "OK" : $"FARK: {data.StopajKontrolFark.ToString("N2", TR)}")}");
        sb.AppendLine();

        AppendField(sb, "Bankaya Tahsilat", data.BankayaTahsilat);
        AppendField(sb, "Bankaya Harç", data.BankayaHarc);
        AppendField(sb, "Bankaya Stopaj", data.BankayaStopaj);
        AppendField(sb, "BANKAYA TOPLAM", data.BankayaToplam);
        AppendField(sb, "NAKİT TOPLAMI", data.NakitToplam);
        sb.AppendLine();

        AppendField(sb, "Kasadaki Nakit", data.KasadakiNakit);
        AppendField(sb, "Dünden Devreden Banka", data.DundenDevredenBanka);
        AppendField(sb, "Yarına Devredecek Banka", data.YarinaDevredecekBanka);
        sb.AppendLine();

        AppendField(sb, "Vergiden Gelen", data.VergidenGelen);
        AppendField(sb, "Vergi Kasa", data.VergiKasa);
        AppendField(sb, "Vergide Biriken Kasa", data.VergideBirikenKasa);
        sb.AppendLine();

        AppendField(sb, "EFT Otomatik İade", data.EftOtomatikIade);
        AppendField(sb, "Gelen Havale", data.GelenHavale);
        AppendField(sb, "İade Kelimesi Giriş", data.IadeKelimesiGiris);

        if (data.IsSabahKasa)
        {
            sb.AppendLine();
            AppendField(sb, "Güne Ait Eksik/Fazla Tahsilat", data.GuneAitEksikFazlaTahsilat);
            AppendField(sb, "Dünden Eksik/Fazla Tahsilat", data.DundenEksikFazlaTahsilat);
            AppendField(sb, "Dünden Eksik/Fazla Gelen Tahsilat", data.DundenEksikFazlaGelenTahsilat);
            AppendField(sb, "Güne Ait Eksik/Fazla Harç", data.GuneAitEksikFazlaHarc);
            AppendField(sb, "Dünden Eksik/Fazla Harç", data.DundenEksikFazlaHarc);
            AppendField(sb, "Dünden Eksik/Fazla Gelen Harç", data.DundenEksikFazlaGelenHarc);
        }

        // UTF-8 with BOM for Turkish Excel
        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);

        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimeCsv,
            FileName = $"kasa_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.csv"
        };
    }

    // ═══════════════════════════════════════════════
    // KasaÜstRapor CSV
    // ═══════════════════════════════════════════════
    public static ExportResult ExportUstRapor(KasaRaporData data)
    {
        var sb = new StringBuilder();

        // Header
        sb.Append("VEZNEDAR");
        foreach (var col in data.UstRaporKolonlar)
            sb.Append($"{Sep}{col}");
        sb.AppendLine();

        // Data rows
        foreach (var satir in data.UstRaporSatirlar)
        {
            sb.Append(EscapeCsv(satir.VeznedarAdi));
            foreach (var col in data.UstRaporKolonlar)
            {
                satir.Degerler.TryGetValue(col, out var val);
                sb.Append($"{Sep}{EscapeCsv(val ?? "")}");
            }
            sb.AppendLine();
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);

        return new ExportResult
        {
            FileBytes = bytes,
            ContentType = ExportResult.MimeCsv,
            FileName = $"kasa_ust_rapor_{data.Tarih:yyyy-MM-dd}_{data.KasaTuru.ToLowerInvariant()}.csv"
        };
    }

    // ═══════════════════════════════════════════════
    private static void AppendField(StringBuilder sb, string label, decimal value) =>
        sb.AppendLine($"{label}{Sep}{value.ToString("N2", TR)}");

    private static string EscapeCsv(string val) =>
        val.Contains(Sep) || val.Contains('"') || val.Contains('\n')
            ? $"\"{val.Replace("\"", "\"\"")}\""
            : val;
}
