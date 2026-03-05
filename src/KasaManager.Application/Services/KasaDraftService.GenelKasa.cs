using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

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
            var res = _import.ImportTrueSource(btPath, ImportFileKind.BankaTahsilat);
            if (res.Ok && res.Value is not null) bt = res.Value;
            else issues.Add($"BankaTahsilat okunamadı: {res.Error}");
        }
        else
        {
            issues.Add("BankaTahsilat.xlsx bulunamadı.");
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

        

        // R12.5: Aralık çözümleme (Genel Kasa / mutabakat için) - SNAPSHOT ÖNCELİKLİ
        // 1) DB'de "Genel Kasa" (hesaplanmış) snapshot varsa: start = son + 1 gün
        // 2) yoksa seed başlangıç tarihi (Ayarlar)
        // 3) yoksa fallback: start = end
        var startDateSource = "fallback";
        var devredenSource = "fallback";

        var lastGenelKasaSnap = await _snapshots.GetLastGenelKasaSnapshotBeforeOrOnAsync(endDate, ct);
        if (lastGenelKasaSnap is not null)
        {
            startDate = lastGenelKasaSnap.RaporTarihi.AddDays(1);
            startDateSource = "snapshot";
        }
        else if (defaults.DefaultGenelKasaBaslangicTarihiSeed is not null)
        {
            startDate = DateOnly.FromDateTime(defaults.DefaultGenelKasaBaslangicTarihiSeed.Value);
            startDateSource = "seed";
        }

        if (startDate > endDate) startDate = endDate;

        decimal bankaDevreden = 0m;
        decimal bankaBakiye = 0m;
        // Dev.Son.Tarih, aralığın bir önceki gününü temsil eder (ayın 1'inden bir gün önce).
        DateOnly devSonTarih = startDate.AddDays(-1);
        if (bt is not null)
        {
            // Banka dosyasından: devreden ve bitiş bakiyesi.
            // Dev.Son.Tarih alanı ise bankadan değil, aralığın bir önceki gününden gelir.
            (bankaDevreden, bankaBakiye, _) = ComputeBankaBalances(bt, startDate, endDate, issues);
        }

        // R12.5: Devreden kuralı - SNAPSHOT ÖNCELİKLİ
        // 1) DB'de "Genel Kasa" snapshot varsa: devreden = lastSnapshot.GenelKasa
        // 2) yoksa seed devreden (Ayarlar)
        // 3) yoksa 0
        decimal devreden = 0m;
        if (lastGenelKasaSnap is not null)
        {
            var fromSnap = TryReadDecimalFromSnapshotResults(lastGenelKasaSnap, new[] { "Genel Kasa", "GenelKasa" });
            if (fromSnap is not null)
            {
                devreden = fromSnap.Value;
                devredenSource = "snapshot";
            }
            else
            {
                // Snapshot var ama alan bulunamadıysa seed/0'a düş.
                devreden = defaults.DefaultGenelKasaDevredenSeed ?? 0m;
                devredenSource = defaults.DefaultGenelKasaDevredenSeed is not null ? "seed" : "fallback";
                issues.Add("Uyarı: DB'de Genel Kasa snapshot bulundu ama Results içinde 'Genel Kasa' alanı okunamadı. Seed/0 kullanıldı.");
            }
        }
        else if (defaults.DefaultGenelKasaDevredenSeed is not null)
        {
            devreden = defaults.DefaultGenelKasaDevredenSeed.Value;
            devredenSource = "seed";
        }
        else
        {
            devreden = 0m;
            devredenSource = "fallback";
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
            snapshot = new { lastGenelKasaSnapDate = lastGenelKasaSnap?.RaporTarihi, lastGenelKasaSnapId = lastGenelKasaSnap?.Id },
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

    private static (decimal devreden, decimal endBalance, DateOnly devSonTarih)
        ComputeBankaBalances(ImportedTable table, DateOnly start, DateOnly end, List<string> issues)
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
        if (endRow.row is not null && bakiyeCol is not null && endRow.row.TryGetValue(bakiyeCol, out var rawB) && TryParseDecimal(rawB, out var b))
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

            var okB = firstOnStart.row.TryGetValue(bakiyeCol, out var rawAfter) && TryParseDecimal(rawAfter, out after);
            var okT = firstOnStart.row.TryGetValue(tutarCol, out var rawAmt) && TryParseDecimal(rawAmt, out amt);

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
            if (prev.row is not null && bakiyeCol is not null && prev.row.TryGetValue(bakiyeCol, out var rawPrev) && TryParseDecimal(rawPrev, out var pb))
                devreden = pb;
            else
                issues.Add("BankaTahsilat: devreden bakiye bulunamadı, 0 kabul edildi.");
        }

        return (devreden, endBalance, endBalanceDate);
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

            if (miktarCol is null || !row.TryGetValue(miktarCol, out var rawM) || !TryParseDecimal(rawM, out var m))
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

    private static decimal? TryReadDevredenFromSnapshot(KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot? snap)
    {
        if (snap?.Results is null) return null;
        var json = snap.Results.ValuesJson;
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var keys = new[] { "SonrayaDevredecek", "sonrayaDevredecek", "BankaBakiye", "bankaBakiye", "Devreden", "devreden" };
            foreach (var k in keys)
            {
                if (!doc.RootElement.TryGetProperty(k, out var el)) continue;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var n))
                    return n;

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (TryParseDecimal(s, out var d)) return d;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryReadDecimalFromSnapshotResults(
        KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot? snap,
        IEnumerable<string> keys)
    {
        if (snap?.Results is null) return null;
        var json = snap.Results.ValuesJson;
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var k in keys)
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                if (!doc.RootElement.TryGetProperty(k, out var el)) continue;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var n))
                    return n;

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (TryParseDecimal(s, out var d)) return d;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

private sealed record EksikFazlaChain(
    decimal OncekiTahsilat,
    decimal DundenTahsilat,
    decimal GuneTahsilat,
    decimal OncekiHarc,
    decimal DundenHarc,
    decimal GuneHarc);

/// <summary>
/// R14G: Eksik/Fazla taşıma zinciri (carry-forward).
/// Kural:
/// - Bugün için guneAit = Excel formülü ile hesaplanır.
/// - Dünden = bir önceki günün guneAit
/// - Önceki gün = (bir önceki günün onceki + dünden)
/// </summary>
private async Task<EksikFazlaChain> ComputeEksikFazlaChainAsync(
    DateOnly date,
    string uploadFolderAbsolute,
    CancellationToken ct)
{
    const int MaxDepthDays = 14;

    async Task<EksikFazlaChain> RecurseAsync(DateOnly d, int depth)
    {
        if (depth > MaxDepthDays)
            return new EksikFazlaChain(0m, 0m, 0m, 0m, 0m, 0m);

        var snap = await _snapshots.GetAsync(d, KasaRaporTuru.Genel, ct);
        if (snap is null)
            return new EksikFazlaChain(0m, 0m, 0m, 0m, 0m, 0m);

        var localIssues = new List<string>();

        var bankaGun = ReadBankaTahsilatGun(d, uploadFolderAbsolute, localIssues, out _);
        var bankaHarcGun = ReadBankaHarcGun(d, uploadFolderAbsolute, localIssues, out _);
        var online = ReadOnlineReddiyatTotals(d, uploadFolderAbsolute, localIssues, out _);
        var onlineHarc = ReadOnlineTotal(d, uploadFolderAbsolute, "onlineHarc.xlsx", ImportFileKind.OnlineHarcama, localIssues, out _);

        var ust = ReadKasaUstRaporSummary(snap, localIssues);

        // Bu hesap için yalnızca BankayaYatirilacakNakit ve ToplamHarc gerekiyor.
        // Kullanıcı manuel girişleri / finalize burada devreye sokulmaz (0 varsayılır).
        #pragma warning disable CS0618 // LEGACY parity/debug only – intentionally used
        var calc = CalculateAksamLegacy(
            devredenKasa: 0m,
            isSabah: true,
            bankaTahsilatGun: bankaGun,
            bankaHarcGun: bankaHarcGun,
            ust: ust,
            online: online,
            bankayaYatirilacakHarciDegistir: 0m,
            bankayaYatirilacakTahsilatiDegistir: 0m,
            kaydenTahsilat: 0m,
            kaydenHarc: 0m,
            vergiKasa: snap.SelectionTotal,
            vergiGelenKasa: 0m,
            bankadanCekilen: 0m,
            cesitliNedenlerleBankadanCikamayanTahsilat: 0m,
            bankayaGonderilmisDeger: 0m,
            bozukPara: 0m);
        #pragma warning restore CS0618

        // Excel parity:
        // Gün içi Ek.Faz T = BankayaGiren - ( (OnlineTahsilat - OnlineReddiyat) + (ToplamTahsilat - BankayaYatirilacakNakit) )
        var guneTahsilat = bankaGun.Giren - ((ust.OnlineTahsilat - online.OnlineReddiyat) + (ust.Tahsilat - calc.BankayaYatirilacakNakit));

        // Gün içi Ek.Faz H = BankaHarcGiren - (OnlineHarc_Dosya + ToplamHarc)
        var guneHarc = bankaHarcGun.Giren - (onlineHarc + calc.NormalHarc);

        var prev = await RecurseAsync(d.AddDays(-1), depth + 1);

        var oncekiT = prev.OncekiTahsilat + prev.DundenTahsilat;
        var dundenT = prev.GuneTahsilat;

        var oncekiH = prev.OncekiHarc + prev.DundenHarc;
        var dundenH = prev.GuneHarc;

        return new EksikFazlaChain(
            OncekiTahsilat: oncekiT,
            DundenTahsilat: dundenT,
            GuneTahsilat: guneTahsilat,
            OncekiHarc: oncekiH,
            DundenHarc: dundenH,
            GuneHarc: guneHarc);
    }

    return await RecurseAsync(date, 0);
}

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

    private async Task<(DateOnly rangeStart, DateOnly devredenSonTarihi, decimal devreden)> ResolveGenelKasaRangeAndDevredenAsync(DateOnly endDate, CancellationToken ct)
    {
        // R12.5 prensibi:
        // - Ayarlarda DefaultGenelKasaBaslangicTarihiSeed varsa: o başlangıç (seed).
        // - Yoksa: en son kaydedilmiş GenelKasa snapshot'ından "BitisTarihi" okunur ve +1 yapılır.
        var defaults = await _globalDefaults.GetOrCreateAsync(ct);
        if (defaults.DefaultGenelKasaBaslangicTarihiSeed is DateTime seedStartDt)
        {
            var baslangic = DateOnly.FromDateTime(seedStartDt);
            var devSon = baslangic.AddDays(-1);
            // Seed devreden: sadece DB boş senaryosunda kullanılır.
            var dev = defaults.DefaultGenelKasaDevredenSeed ?? 0m;
            return (baslangic, devSon, dev);
        }

        var last = await _snapshots.GetLastGenelKasaSnapshotBeforeOrOnAsync(endDate, ct);
        if (last is null)
        {
            // Seed: hiç snapshot yoksa R10 başlangıcını endDate ile başlatıp devreden 0 kabul et.
            var start = endDate;
            return (start, start.AddDays(-1), 0m);
        }

        // Snapshot'tan bitiş tarihi ve devreden sonucunu JSON içinden oku.
        // (Eski modelde BitisTarihi/GenelKasaSonuc doğrudan property değildi.)
        var lastEnd = TryReadDateOnlyFromSnapshotInputs(last, "BitisTarihi") ?? last.RaporTarihi;
        var devreden = TryReadDecimalFromSnapshotResults(last, "GenelKasaSonuc")
            ?? TryReadDecimalFromSnapshotResults(last, KasaCanonicalKeys.GenelKasa)
            ?? 0m;

        var startDate = lastEnd.AddDays(1);
        return (startDate, lastEnd, devreden);
    }

    private static DateOnly? TryReadDateOnlyFromSnapshotInputs(KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot snap, string key)
    {
        try
        {
            var json = snap.Inputs?.ValuesJson;
            if (string.IsNullOrWhiteSpace(json)) return null;
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
            if (!dict.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
            if (DateOnly.TryParse(raw, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var d)) return d;
            if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryReadDecimalFromSnapshotResults(KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot snap, string key)
    {
        try
        {
            var json = snap.Results?.ValuesJson;
            if (string.IsNullOrWhiteSpace(json)) return null;
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
            if (!dict.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var v)) return v;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var vi)) return vi;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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

    private Task<(decimal endBakiye, string? rawJson, bool ok)> ImportBankaBakiyeAsync(
        string uploadFolderAbsolute,
        DateOnly start,
        DateOnly end,
        List<string> issues,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
        var res = _import.ImportTrueSource(full, ImportFileKind.BankaTahsilat);
        if (!res.Ok || res.Value is null)
        {
            issues.Add($"BankaTahsilat.xlsx okunamadı: {res.Error}");
            return Task.FromResult((0m, (string?)null, false));
        }

        var (devreden, endBalance, endBalanceDate) = ComputeBankaBalances(res.Value, start, end, issues);
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

        return Task.FromResult<(decimal endBakiye, string? rawJson, bool ok)>((endBalance, rawJson, true));
    }

}
