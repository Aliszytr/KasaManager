using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services;

// GenelKasa: BuildGenelKasaR10DraftAsync, ComputeBankaBalances, ComputeMasrafReddiyatTotals, TryReadDevredenFromSnapshot, ComputeEksikFazlaChainAsync, Create*, Resolve*, Import*
public sealed partial class KasaDraftService
{
    // =============================
    // R10.8 FIX PACK – Genel Kasa (True Source)
    // =============================
    private async Task<(Dictionary<string, string> fields, string rawJson)> BuildGenelKasaR10DraftAsync(
        DateOnly raporTarihi,
        KasaDraftFinalizeInputs? finalizeInputs,
        string uploadFolderAbsolute,
        List<string> issues,
        CancellationToken ct)
    {
        // R10 (True Source) mantığı: aralık tabanlı mutabakat.
        // Bu ekrandaki "Genel" snapshot, KasaÜstRapor seçim/satır snapshot'ıdır. "Genel Kasa" snapshot'ı değildir.
        // O yüzden startDate'i snapshot geçmişinden türetmek burada yanlış sonuç üretir.
        // KİLİTLİ (R10.8): Aralık = ayın 1'i .. raporTarihi.
        var endDate = raporTarihi;
        DateOnly startDate = endDate; // R10.9: aralık kaynağa göre çözülecek

        // Defaults (seed / varsayılan nakit)
        var defaults = await _globalDefaults.GetAsync(ct);

        // BankaTahsilat.xlsx ve MasrafveReddiyat.xlsx (true sources)
        ImportedTable? bt = null;
        ImportedTable? mr = null;

        var btPath = ResolveExistingFile(uploadFolderAbsolute, "BankaTahsilat.xlsx");
        if (btPath is not null)
        {
            _log.LogInformation("[BankaTahsilat Source] resolved path: {Path}", btPath);
            var res = _import.ImportTrueSource(btPath, ImportFileKind.BankaTahsilat);
            if (res.Ok && res.Value is not null) bt = res.Value;
            else 
            {
                issues.Add($"BankaTahsilat okunamadı: {res.Error}");
                _log.LogWarning("[BankaTahsilat Source] okuma/çözümleme hatası: {Error}", res.Error);
            }
        }
        else
        {
            issues.Add("KRİTİK UYARI: BankaTahsilat.xlsx dosyasının hesaplanmak istenen güncel versiyonunu sisteme ekleyin. Dosya veya veri bulunamadığı için Banka Bakiye 0 kalacaktır.");
            _log.LogWarning("[BankaTahsilat Source] missing file. Aranan klasör: {Folder}", uploadFolderAbsolute);
        }

        var mrPath = ResolveExistingFile(uploadFolderAbsolute, "MasrafveReddiyat.xlsx");
        if (mrPath is not null)
        {
            var res = _import.ImportTrueSource(mrPath, ImportFileKind.MasrafVeReddiyat);
            if (res.Ok && res.Value is not null) mr = res.Value;
            else issues.Add($"MasrafveReddiyat okunamadı: {res.Error}");
        }
        else
        {
            issues.Add("MasrafveReddiyat.xlsx bulunamadı.");
        }

        

        // SSOT Kuralı: Genel Kasa Devreden Kararı (R10)
        var ssot = await DetermineGenelKasaDevredenSsotAsync(endDate, ct);
        
        startDate = ssot.RangeStart;
        if (startDate > endDate) startDate = endDate;
        
        DateOnly devSonTarih = ssot.DevredenSonTarihi;
        decimal devreden = ssot.Devreden;
        var devredenSource = ssot.Source;
        var startDateSource = ssot.IsSnapshotActive ? "snapshot" : (ssot.Source == "seed" ? "seed" : "fallback");
        var lastGenelKasaSnapId = ssot.LastSnapshotId;

        decimal bankaDevreden = 0m;
        decimal bankaBakiye = 0m;

        if (bt is not null)
        {
            var bRes = ComputeBankaBalances(bt, startDate, endDate, issues, btPath ?? "Belirsiz.xlsx");
            bankaDevreden = bRes.devreden;
            bankaBakiye = bRes.endBalance;

            if (bRes.mismatch == BankaMismatchType.SourceDateOlderThanRequested)
            {
                issues.Add($"Uyarı: BankaTahsilat işlemleri son tarihi ({bRes.diag.SelectedBalanceDate?.ToString("dd.MM.yyyy")}), rapor tarihinden ({endDate:dd.MM.yyyy}) daha eski.");
            }
        }

        decimal toplamTahsilat = 0m;
        decimal toplamReddiyat = 0m;
        decimal kaydenTahsilat = 0m;
        decimal gelmeyenD = finalizeInputs?.GelmeyenD ?? 0m; // UI'da var ama bu fazda DB'ye yazılmaz

        string? masrafDebug = null;
        if (mr is not null)
        {
            var t = ComputeMasrafReddiyatTotals(mr, startDate, endDate, issues);
            toplamTahsilat = t.masraf;
            toplamReddiyat = t.reddiyat;
            kaydenTahsilat = t.kayden;
            if (kaydenTahsilat == 0m)
            {
                _log.LogInformation("[KaydenTahsilat İzleme] MasrafVeReddiyat.xlsx içerisinde 'KAYDEN' içeren tip bulunamadığı için hesaplama 0'da kaldı.");
                issues.Add("Bilgi: Yüklenen MasrafVeReddiyat raporunda 'Kayden' ibareli işlem türü bulunamadığı için tahsilat 0 kabul edildi.");
            }
            masrafDebug = t.rawJson;
        }

        // R12.6: FORMÜL TEKLEŞTİRME (Kart + ModelDoldurma aynı sonucu göstermeli)
        // DÜZELTME: Doğru formül kullanıcı tarafından onaylandı:
        // GenelKasa = Devreden + EksikFazla + TahRedFark - BankaBakiye - KasaNakit - GelmeyenD
        // Amaç: İki tarih arasında kasada bulunan nakit paraya göre kasanın eksik/fazla miktarını tespit etmek
        var eksikFazla = defaults.DefaultKasaEksikFazla ?? 0m;
        // K.Nakit: Varsayılan Nakit + Varsayılan Bozuk Para (R10 kilit)
        var kasaNakit = (defaults.DefaultNakitPara ?? 0m) + (defaults.DefaultBozukPara ?? 0m);

        var tahRedFark = toplamTahsilat - toplamReddiyat;
        // Sonraya devredecek hesabı (legacy uyumluluk için korunuyor)
        var sonrayaDevredecek = devreden + tahRedFark - gelmeyenD;
        var beklenenBanka = sonrayaDevredecek + eksikFazla;
        // DÜZELTME: Yeni Genel Kasa formülü
        var genelKasa = devreden + eksikFazla + tahRedFark - bankaBakiye - kasaNakit - gelmeyenD;
        // Mutabakat farkı (legacy uyumluluk için korunuyor, UI'da gösterilir)
        var mutabakatFarki = bankaBakiye - beklenenBanka;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Sol kart (Genel Kasa Raporu)
            ["D.Baş.Tarihi"] = startDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["Devreden"] = devreden.ToString("N2", CultureInfo.InvariantCulture),
            ["Banka Bakiye"] = bankaBakiye.ToString("N2", CultureInfo.InvariantCulture),
            ["Toplam Tahsilat"] = toplamTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["Tah.Red Fark"] = tahRedFark.ToString("N2", CultureInfo.InvariantCulture),
            ["R.Sn.Tarih"] = endDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["T.Ek.Faz.Mk."] = eksikFazla.ToString("N2", CultureInfo.InvariantCulture),
            ["Sn.Dn.Devredecek"] = sonrayaDevredecek.ToString("N2", CultureInfo.InvariantCulture),
            ["Beklenen Banka"] = beklenenBanka.ToString("N2", CultureInfo.InvariantCulture),
            ["Genel Kasa"] = genelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["Mutabakat Farkı"] = mutabakatFarki.ToString("N2", CultureInfo.InvariantCulture),

            // Sağ kart (Diğer Alanlar)
            ["D.Bit.Tarihi"] = endDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["Dev.Son.Tarih"] = devSonTarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["Kayden Tah."] = kaydenTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            // Bitiş bakiyesi aynı zamanda "Sn.Dn.Devredecek" olarak taşınıyor (UI parity).
            // NOT: Sol kartta ayrıca "Sn.Dn.Devredecek" hesap değeri (sonrayaDevredecek) gösterilir.
            ["Sn.Dn.Devredecek (Banka)"] = bankaBakiye.ToString("N2", CultureInfo.InvariantCulture),
            ["Toplam Reddiyat"] = toplamReddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["Gelmeyen D."] = gelmeyenD.ToString("N2", CultureInfo.InvariantCulture),
            ["K.Nakit"] = kasaNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["Tah.Tarihi"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        };

        // Debug json
        // NOTE: 'raw' is also used in out-var parsing blocks above; reusing the same name
        // here triggers CS0136 (cannot declare 'raw' in this scope). Keep this name unique.
        var debugPayload = new
        {
            range = new { startDate, endDate, startDateSource },
            devredenInfo = new { devreden, devredenSource },
            defaults = new
            {
                defaults.DefaultGenelKasaDevredenSeed,
                defaults.DefaultKasaEksikFazla,
                defaults.DefaultNakitPara,
                defaults.DefaultBozukPara
            },
            snapshot = new { lastGenelKasaSnapId = lastGenelKasaSnapId },
            banka = new { bankaDevreden, bankaBakiye, devSonTarih, file = btPath },
            masrafveReddiyat = new { toplamTahsilat, toplamReddiyat, kaydenTahsilat, file = mrPath, debug = masrafDebug },
            inputs = new { gelmeyenD },
            formula = new
            {
                version = "R12.6-GenelKasaFormulaV2",
                tekFazIncludedCount = 1,
                notes = "Eksik/Fazla sadece BeklenenBanka'ya 1 kez eklenir; GenelKasa=Mutabakat+K.Nakit"
            },
            calc = new { tahRedFark, sonrayaDevredecek, beklenenBanka, genelKasa, mutabakatFarki }
        };

        var rawJson = JsonSerializer.Serialize(debugPayload, new JsonSerializerOptions { WriteIndented = true });
        return (fields, rawJson);
    }

    private static (decimal devreden, decimal endBalance, DateOnly devSonTarih, BankaBakiyeDiagnosticInfo diag, BankaMismatchType mismatch)
        ComputeBankaBalances(ImportedTable table, DateOnly start, DateOnly end, List<string> issues, string filePath)
    {
        var tarihCol = FindCanonical(table, "islem_tarihi") ?? FindCanonical(table, "tarih");
        var tutarCol = FindCanonical(table, "islem_tutari") ?? FindCanonical(table, "tutar") ?? FindCanonical(table, "miktar");
        var bakiyeCol = FindCanonical(table, "islem_sonrasi_bakiye") ?? FindCanonical(table, "bakiye");
        var borcAlacakCol = FindCanonical(table, "borc_alacak") ?? FindCanonical(table, "borc_alacak_durumu");

        if (tarihCol is null) issues.Add("BankaTahsilat: 'İşlem Tarihi' kolonu bulunamadı.");
        if (tutarCol is null) issues.Add("BankaTahsilat: 'İşlem Tutarı' kolonu bulunamadı.");
        if (bakiyeCol is null) issues.Add("BankaTahsilat: 'İşlem Sonrası Bakiye' kolonu bulunamadı.");
        if (borcAlacakCol is null) issues.Add("BankaTahsilat: 'Borç/Alacak' kolonu bulunamadı.");

        var rows = new List<(DateTime dt, Dictionary<string, string?> row)>();
        foreach (var row in table.Rows)
        {
            if (row is null) continue;
            if (tarihCol is null || !row.TryGetValue(tarihCol, out var rawT) || string.IsNullOrWhiteSpace(rawT)) continue;
            if (!TryParseDateTime(rawT, out var dt)) continue;
            rows.Add((dt, row));
        }

        rows.Sort((a, b) => a.dt.CompareTo(b.dt));

        decimal endBalance = 0m;
        DateOnly endBalanceDate = end;

        var endRow = rows.LastOrDefault(x => DateOnly.FromDateTime(x.dt) <= end);
        DateOnly? actualEnd = null;

        var diag = new BankaBakiyeDiagnosticInfo
        {
            PathResolvedPath = filePath,
            FileExists = true,
            MatchedRowCount = rows.Count,
            RequestedEndDate = end
        };
        var mismatch = BankaMismatchType.None;

        if (endRow.row is not null && bakiyeCol is not null && endRow.row.TryGetValue(bakiyeCol, out var rawB) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawB, out var b))
        {
            endBalance = b;
            endBalanceDate = DateOnly.FromDateTime(endRow.dt);
            actualEnd = endBalanceDate;

            diag.SelectedBalanceDate = actualEnd;
            diag.LastAvailableBalanceDate = rows.Count > 0 ? DateOnly.FromDateTime(rows.Last().dt) : null;

            if (actualEnd.Value < end) mismatch = BankaMismatchType.SourceDateOlderThanRequested;
            else if (actualEnd.Value > end) mismatch = BankaMismatchType.SourceDateNewerThanRequested; // Though 'LastOrDefault(<= end)' prevents newer, but if the latest date overall is newer, we can check LastAvailableBalanceDate.
            
            if (diag.LastAvailableBalanceDate > end && actualEnd == end)
            {
               // This means we had a perfect match for `end`, but there's newer data. It's technically None since we found exactly what we asked for.
               mismatch = BankaMismatchType.None; 
            }
        }
        else
        {
            diag.SelectedBalanceDate = null;
            diag.LastAvailableBalanceDate = rows.Count > 0 ? DateOnly.FromDateTime(rows.Last().dt) : null;
            mismatch = rows.Count == 0 ? BankaMismatchType.NoEligibleRowFound : BankaMismatchType.SourceDateMissing;
        }

        decimal devreden = 0m;

        // start günündeki ilk işlem varsa => İşlemSonrasıBakiye ± İşlemTutarı
        var firstOnStart = rows.FirstOrDefault(x => DateOnly.FromDateTime(x.dt) == start);
        if (firstOnStart.row is not null && bakiyeCol is not null && tutarCol is not null)
        {
            decimal after = 0m;
            decimal amt = 0m;

            var okB = firstOnStart.row.TryGetValue(bakiyeCol, out var rawAfter) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawAfter, out after);
            var okT = firstOnStart.row.TryGetValue(tutarCol, out var rawAmt) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawAmt, out amt);

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
            if (prev.row is not null && bakiyeCol is not null && prev.row.TryGetValue(bakiyeCol, out var rawPrev) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawPrev, out var pb))
                devreden = pb;
            else
                issues.Add("BankaTahsilat: devreden bakiye bulunamadı, 0 kabul edildi.");
        }

        return (devreden, endBalance, endBalanceDate, diag, mismatch);
    }

    private static (decimal masraf, decimal reddiyat, decimal kayden, string? rawJson)
        ComputeMasrafReddiyatTotals(ImportedTable table, DateOnly start, DateOnly end, List<string> issues)
    {
        var tarihCol = FindCanonical(table, "tarih") ?? FindCanonical(table, "islem_tarihi");
        var tipCol = FindCanonical(table, "tip");
        var miktarCol = FindCanonical(table, "miktar") ?? FindCanonical(table, "islem_tutari") ?? FindCanonical(table, "tutar");

        if (tarihCol is null) issues.Add("MasrafveReddiyat: 'Tarih' kolonu bulunamadı.");
        if (tipCol is null) issues.Add("MasrafveReddiyat: 'Tip' kolonu bulunamadı.");
        if (miktarCol is null) issues.Add("MasrafveReddiyat: 'Miktar' kolonu bulunamadı.");

        decimal masraf = 0m;
        decimal reddiyat = 0m;
        decimal kayden = 0m;

        var sample = new List<Dictionary<string, string?>>();
        var take = 0;

        foreach (var row in table.Rows)
        {
            if (row is null) continue;

            if (tarihCol is not null)
            {
                if (!row.TryGetValue(tarihCol, out var rawT) || !TryParseDateOnly(rawT, out var d)) continue;
                if (d < start || d > end) continue;
            }

            if (miktarCol is null || !row.TryGetValue(miktarCol, out var rawM) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawM, out var m))
                continue;

            var tip = tipCol is not null ? (row.GetValueOrDefault(tipCol) ?? "") : "";
            var tipU = tip.Trim().ToUpperInvariant();

            if (tipU.Contains("MASRAF")) masraf += m;
            else if (tipU.Contains("REDD")) reddiyat += m;
            else if (tipU.Contains("KAYDEN")) kayden += m;

            if (take < 5)
            {
                sample.Add(row);
                take++;
            }
        }

        string? rawJson = null;
        try
        {
            rawJson = JsonSerializer.Serialize(new { start, end, masraf, reddiyat, kayden, sample }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            // REFACTOR: No silent swallow - capture error in rawJson for debugging
            rawJson = $"{{\"error\": \"JSON serialize failed: {ex.Message}\"}}";
        }

        return (masraf, reddiyat, kayden, rawJson);
    }



    // Snapshot okuma metodları P4.2 (Stateless Engine) mimarisi gereği tamamen silinmiştir.

private sealed record EksikFazlaChain(
    decimal OncekiTahsilat,
    decimal DundenTahsilat,
    decimal GuneTahsilat,
    decimal OncekiHarc,
    decimal DundenHarc,
    decimal GuneHarc);

/// <summary>

    // ==============================
    // FAZ-2 / Adım-1 helpers (GenelKasa UI-only)
    // ==============================

    
private static UnifiedPoolEntry CreateRawEntry(string key, decimal value, string sourceName, string? sourceFile, string? details, bool includeInCalculations = true, string? notes = null)
{
    return new UnifiedPoolEntry
    {
        CanonicalKey = key,
        Value = value.ToString("N2", CultureInfo.InvariantCulture),
        Type = UnifiedPoolValueType.Raw,
        IncludeInCalculations = includeInCalculations,
        SourceName = sourceName,
        SourceFile = sourceFile,
        SourceDetails = details,
        Notes = notes
    };
}

    private static UnifiedPoolEntry CreateOverrideEntry(string key, decimal value, string? notes)
    {
        return new UnifiedPoolEntry
        {
            CanonicalKey = key,
            Value = value.ToString("N2", CultureInfo.InvariantCulture),
            Type = UnifiedPoolValueType.Override,
            // Kullanıcı girişleri (UserInput) boş/0 olsa bile formül motoruna "mevcut" olarak girer.
            // Böylece "Missing variable" hatası oluşmaz; 0 değeri formülde doğal olarak etkisiz kalır.
            IncludeInCalculations = true,
            SourceName = "UserInput",
            SourceFile = null,
            SourceDetails = "GenelKasaR10 overrides",
            Notes = notes
        };
    }

    private Task<DateOnly?> ResolveMaxDateFromMasrafveReddiyatAsync(string uploadFolderAbsolute, List<string> issues, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.Combine(uploadFolderAbsolute, "MasrafveReddiyat.xlsx");
            var res = _import.ImportTrueSource(full, ImportFileKind.MasrafVeReddiyat);
            if (!res.Ok || res.Value is null)
            {
                issues.Add($"MasrafveReddiyat.xlsx okunamadı: {res.Error}");
                return Task.FromResult<DateOnly?>(null);
            }

            var table = res.Value;
            var tarihCol = FindCanonical(table, "tarih") ?? FindCanonical(table, "islem_tarihi");
            if (tarihCol is null)
            {
                issues.Add("MasrafveReddiyat.xlsx: tarih kolonu bulunamadı.");
                return Task.FromResult<DateOnly?>(null);
            }

            DateOnly? max = null;
            foreach (var row in table.Rows)
            {
                if (row is null) continue;
                if (!row.TryGetValue(tarihCol, out var rawT) || !TryParseDateOnly(rawT, out var d)) continue;
                if (max is null || d > max.Value) max = d;
            }

            return Task.FromResult(max);
        }
        catch (Exception ex)
        {
            issues.Add($"MasrafveReddiyat.xlsx max tarih okuma hatası: {ex.Message}");
            return Task.FromResult<DateOnly?>(null);
        }
    }

    /// <summary>
    /// BankaTahsilat.xlsx dosyasındaki en son işlem tarihini döner.
    /// Genel Kasa hesaplamasında bitiş tarihini belirleyen birincil kaynaktır.
    /// </summary>
    private Task<DateOnly?> ResolveMaxDateFromBankaTahsilatAsync(string uploadFolderAbsolute, List<string> issues, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var btPath = ResolveExistingFile(uploadFolderAbsolute, "BankaTahsilat.xlsx");
            if (btPath is null)
            {
                issues.Add("BankaTahsilat.xlsx bulunamadı; bitiş tarihi belirlenemedi.");
                return Task.FromResult<DateOnly?>(null);
            }

            var res = _import.ImportTrueSource(btPath, ImportFileKind.BankaTahsilat);
            if (!res.Ok || res.Value is null)
            {
                issues.Add($"BankaTahsilat.xlsx okunamadı: {res.Error}");
                return Task.FromResult<DateOnly?>(null);
            }

            var table = res.Value;
            var tarihCol = FindCanonical(table, "islem_tarihi") ?? FindCanonical(table, "tarih");
            if (tarihCol is null)
            {
                issues.Add("BankaTahsilat.xlsx: İşlem Tarihi kolonu bulunamadı.");
                return Task.FromResult<DateOnly?>(null);
            }

            DateOnly? max = null;
            foreach (var row in table.Rows)
            {
                if (row is null) continue;
                if (!row.TryGetValue(tarihCol, out var rawT) || string.IsNullOrWhiteSpace(rawT)) continue;
                if (!TryParseDateTime(rawT, out var dt)) continue;
                var d = DateOnly.FromDateTime(dt);
                if (max is null || d > max.Value) max = d;
            }

            _log.LogInformation("[BankaTahsilat MaxDate] Tespit edilen son işlem tarihi: {MaxDate}", max);
            return Task.FromResult(max);
        }
        catch (Exception ex)
        {
            issues.Add($"BankaTahsilat.xlsx max tarih okuma hatası: {ex.Message}");
            return Task.FromResult<DateOnly?>(null);
        }
    }


    private async Task<(DateOnly RangeStart, DateOnly DevredenSonTarihi, decimal Devreden, string Source, bool IsSnapshotActive, Guid? LastSnapshotId)> DetermineGenelKasaDevredenSsotAsync(DateOnly endDate, CancellationToken ct)
    {
        var res = await _carryoverResolver.ResolveAsync(endDate, CarryoverScope.GenelKasa, ct);
        
        return (
            res.RangeStart,
            res.SourceDate ?? endDate.AddDays(-1),
            res.Value,
            res.SourceCode,
            res.SourceCode == "CalculatedSnapshot" || res.SourceCode == "LegacySnapshotFallback",
            res.SourceId
        );
    }

    private async Task<(DateOnly rangeStart, DateOnly devredenSonTarihi, decimal devreden)> ResolveGenelKasaRangeAndDevredenAsync(DateOnly endDate, CancellationToken ct)
    {
        var ssot = await DetermineGenelKasaDevredenSsotAsync(endDate, ct);
        return (ssot.RangeStart, ssot.DevredenSonTarihi, ssot.Devreden);
    }

    // KasaDraftService Snapshot fallbacks cleared.

    private Task<(decimal masraf, decimal reddiyat, decimal kayden, string? rawJson, bool ok)> ImportMasrafveReddiyatAggAsync(
        string uploadFolderAbsolute,
        DateOnly start,
        DateOnly end,
        List<string> issues,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Use the new Unified logic for Range (General Kasa)
        string? rawJson;
        var agg = ReadMasrafReddiyatAggUnified(
            start, // raporTarihi unused in range mode, but we pass start
            uploadFolderAbsolute,
            issues,
            out rawJson,
            rangeStart: start,
            rangeEnd: end,
            fullExcelTotals: false
        );

        // Map Diger -> Kayden (legacy mapping)
        return Task.FromResult((agg.Masraf, agg.Reddiyat, agg.Diger, rawJson, true));
    }

    private Task<(decimal endBakiye, string? rawJson, BankaMismatchType mismatch, BankaBakiyeDiagnosticInfo diag)> ImportBankaBakiyeAsync(
        string uploadFolderAbsolute,
        DateOnly start,
        DateOnly end,
        List<string> issues,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
        
        var diag = new BankaBakiyeDiagnosticInfo
        {
            PathResolvedPath = full,
            FileExists = File.Exists(full),
            RequestedEndDate = end
        };

        var res = _import.ImportTrueSource(full, ImportFileKind.BankaTahsilat);
        if (!res.Ok || res.Value is null)
        {
            var mismatch = diag.FileExists ? BankaMismatchType.PathResolveFailed : BankaMismatchType.FileMissing;
            return Task.FromResult((0m, (string?)null, mismatch, diag));
        }

        var (devreden, endBalance, endBalanceDate, diagnostic, mismatchOut) = ComputeBankaBalances(res.Value, start, end, issues, full);
        string? rawJson = null;
        try
        {
            rawJson = JsonSerializer.Serialize(new { start, end, devreden, endBalance, endBalanceDate }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            // REFACTOR: No silent swallow - capture error in rawJson for debugging
            rawJson = $"{{\"error\": \"JSON serialize failed: {ex.Message}\"}}";
        }

        return Task.FromResult<(decimal endBakiye, string? rawJson, BankaMismatchType mismatch, BankaBakiyeDiagnosticInfo diag)>((endBalance, rawJson, mismatchOut, diagnostic));
    }

}
