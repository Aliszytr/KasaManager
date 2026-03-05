#nullable enable
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.Calculators;

/// <summary>
/// Banka bakiye hesaplama işlemleri.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public static class BankaBalanceCalculator
{
    /// <summary>
    /// Banka bakiye hesaplama sonucu.
    /// </summary>
    public sealed record BankaBalanceResult(
        decimal Devreden,
        decimal EndBalance,
        DateOnly DevSonTarih);

    /// <summary>
    /// BankaTahsilat tablosundan devreden ve bitiş bakiyesini hesaplar.
    /// </summary>
    public static BankaBalanceResult ComputeBankaBalances(
        ImportedTable table,
        DateOnly start,
        DateOnly end,
        List<string> issues)
    {
        var tarihCol = CanonicalKeyHelper.FindCanonical(table, "islem_tarihi") 
            ?? CanonicalKeyHelper.FindCanonical(table, "tarih");
        var tutarCol = CanonicalKeyHelper.FindCanonical(table, "islem_tutari") 
            ?? CanonicalKeyHelper.FindCanonical(table, "tutar") 
            ?? CanonicalKeyHelper.FindCanonical(table, "miktar");
        var bakiyeCol = CanonicalKeyHelper.FindCanonical(table, "islem_sonrasi_bakiye") 
            ?? CanonicalKeyHelper.FindCanonical(table, "bakiye");
        var borcAlacakCol = CanonicalKeyHelper.FindCanonical(table, "borc_alacak") 
            ?? CanonicalKeyHelper.FindCanonical(table, "borc_alacak_durumu");

        if (tarihCol is null) issues.Add("BankaTahsilat: 'İşlem Tarihi' kolonu bulunamadı.");
        if (tutarCol is null) issues.Add("BankaTahsilat: 'İşlem Tutarı' kolonu bulunamadı.");
        if (bakiyeCol is null) issues.Add("BankaTahsilat: 'İşlem Sonrası Bakiye' kolonu bulunamadı.");
        if (borcAlacakCol is null) issues.Add("BankaTahsilat: 'Borç/Alacak' kolonu bulunamadı.");

        var rows = new List<(DateTime dt, Dictionary<string, string?> row)>();
        foreach (var row in table.Rows)
        {
            if (row is null) continue;
            if (tarihCol is null || !row.TryGetValue(tarihCol, out var rawT) || string.IsNullOrWhiteSpace(rawT)) continue;
            if (!DateParsingHelper.TryParseDateTime(rawT, out var dt)) continue;
            rows.Add((dt, row));
        }

        rows.Sort((a, b) => a.dt.CompareTo(b.dt));

        decimal endBalance = 0m;
        DateOnly endBalanceDate = end;

        var endRow = rows.LastOrDefault(x => DateOnly.FromDateTime(x.dt) <= end);
        if (endRow.row is not null && bakiyeCol is not null && 
            endRow.row.TryGetValue(bakiyeCol, out var rawB) && 
            DecimalParsingHelper.TryParseDecimal(rawB, out var b))
        {
            endBalance = b;
            endBalanceDate = DateOnly.FromDateTime(endRow.dt);
        }
        else
        {
            issues.Add("BankaTahsilat: bitiş bakiyesi bulunamadı, 0 kabul edildi.");
        }

        decimal devreden = 0m;

        // start günündeki ilk işlem varsa => İşlemSonrasıBakiye ± İşlemTutarı
        var firstOnStart = rows.FirstOrDefault(x => DateOnly.FromDateTime(x.dt) == start);
        if (firstOnStart.row is not null && bakiyeCol is not null && tutarCol is not null)
        {
            decimal after = 0m;
            decimal amt = 0m;

            var okB = firstOnStart.row.TryGetValue(bakiyeCol, out var rawAfter) && 
                DecimalParsingHelper.TryParseDecimal(rawAfter, out after);
            var okT = firstOnStart.row.TryGetValue(tutarCol, out var rawAmt) && 
                DecimalParsingHelper.TryParseDecimal(rawAmt, out amt);

            if (okB && okT)
            {
                var dir = borcAlacakCol is not null ? (firstOnStart.row.GetValueOrDefault(borcAlacakCol) ?? "") : "";
                var dirU = dir.Trim().ToUpperInvariant();

                // KİLİTLİ:
                // Alacak -> önce = sonra - tutar
                // Borç  -> önce = sonra + tutar
                if (dirU.Contains("ALACAK"))
                    devreden = after - amt;
                else if (dirU.Contains("BORÇ") || dirU.Contains("BORC"))
                    devreden = after + amt;
                else
                    devreden = after;
            }
        }
        else
        {
            // start öncesi son bakiye
            var prev = rows.LastOrDefault(x => DateOnly.FromDateTime(x.dt) < start);
            if (prev.row is not null && bakiyeCol is not null && 
                prev.row.TryGetValue(bakiyeCol, out var rawPrev) && 
                DecimalParsingHelper.TryParseDecimal(rawPrev, out var pb))
                devreden = pb;
            else
                issues.Add("BankaTahsilat: devreden bakiye bulunamadı, 0 kabul edildi.");
        }

        return new BankaBalanceResult(devreden, endBalance, endBalanceDate);
    }
}
