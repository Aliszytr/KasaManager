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

// Readers: KasaUstSummary, ReadKasaUstRaporSummary, ReadOnlineReddiyatTotals, ReadBankaTahsilatGun, ReadBankaTahsilatExtraInflowAgg, ReadBankaOutflowByType
public sealed partial class KasaDraftService
{
    private sealed record KasaUstSummary(
        decimal PosTahsilat,
        decimal OnlineTahsilat,
        decimal PostTahsilat,
        decimal Tahsilat,
        decimal Reddiyat,
        decimal PosHarc,
        decimal OnlineHarc,
        decimal PostHarc,
        decimal GelmeyenPost,
        decimal Harc,
        decimal GelirVergisi,
        decimal DamgaVergisi,
        decimal Stopaj);

    private sealed record OnlineReddiyatAgg(decimal OnlineReddiyat, decimal OnlineStopaj, string RawJson);

    private sealed record BankaGunAgg(decimal Devreden, decimal Yarina, decimal Giren, decimal Cikan, string RawJson);

    private static KasaUstSummary ReadKasaUstRaporSummary(KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot genSnap, List<string> issues)
    {
        try
        {
            var sum = genSnap.Rows.FirstOrDefault(r => r.IsSummaryRow);
            if (sum == null)
            {
                issues.Add("KasaÜstRapor snapshot'ında TOPLAMLAR satırı bulunamadı (IsSummaryRow=false). Tahsilat/Harç/Reddiyat/Stopaj hesapları 0 kabul edildi.");
                return new KasaUstSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }


            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(sum.ColumnsJson) ?? new();

            decimal ReadDec(string key)
            {
                return Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(TryGet(dict, key), out var d) ? d : 0m;
            }

            // İşlem sayısı (count) kolonlarını tutar (amount) kolonlarıyla karıştırmamak için
            // "_islem_sayisi" veya "_sayisi" ile biten key'leri hariç tutuyoruz.
            var countSuffixes = new[] { "_islem_sayisi", "_sayisi" };
            bool IsCountKey(string key) =>
                countSuffixes.Any(s => key.EndsWith(s, StringComparison.OrdinalIgnoreCase));

            string? FindKeyContains(params string[] tokens)
            {
                // Basit fallback: canonical key bulunamazsa, anahtar adında token geçen ilk alanı al.
                // Örn: "harc" yerine "toplam_harc" gibi varyasyonlar.
                // KRİTİK: _islem_sayisi (count) key'lerini hariç tut — bu sayı kolonları, tutar değil!
                foreach (var k in dict.Keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var kk = k.Trim();
                    if (IsCountKey(kk)) continue; // Skip count columns
                    var ok = true;
                    foreach (var t in tokens)
                    {
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (!kk.Contains(t, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
                    }
                    if (ok) return kk;
                }
                return null;
            }

            decimal ReadDecFallback(string preferredKey, params string[] containsTokens)
            {
                // KRİTİK FIX: Exact key dict'te VARSA (değeri 0 bile olsa) fallback'e düşme.
                // Eski mantık: v != 0m ise dön → 0 değerinde yanlışlıkla _islem_sayisi fallback'ine düşüyordu.
                if (dict.ContainsKey(preferredKey))
                    return ReadDec(preferredKey);

                var altKey = FindKeyContains(containsTokens);
                if (!string.IsNullOrWhiteSpace(altKey))
                    return ReadDec(altKey);

                return 0m;
            }

            // Harç / Tahsilat / Stopaj kolonları farklı header varyasyonlarıyla gelebilir.
            // Önce canonical key, yoksa "contains" fallback.
            var posTahsilat = ReadDecFallback("pos_tahsilat", "pos", "tahsil");
            var onlineTahsilat = ReadDecFallback("online_tahsilat", "online", "tahsil");
            var postTahsilat = ReadDecFallback("post_tahsilat", "post", "tahsil");
            var tahsilat = ReadDecFallback("tahsilat", "tahsil");
            var reddiyat = ReadDecFallback("reddiyat", "redd");

            var posHarc = ReadDecFallback("pos_harc", "pos", "harc");
            var onlineHarc = ReadDecFallback("online_harc", "online", "harc");
            var postHarc = ReadDecFallback("post_harc", "post", "harc");
            var gelmeyenPost = ReadDecFallback("gelmeyen_post", "gelmeyen", "post");

            // Toplam Harç için pos/online ile çakışmasın.
            var harc = ReadDec("harc");
            if (harc == 0m && !dict.ContainsKey("harc"))
            {
                var kToplam = FindKeyContains("toplam", "harc") ?? FindKeyContains("harc");
                if (!string.IsNullOrWhiteSpace(kToplam)
                    && !kToplam.Contains("pos", StringComparison.OrdinalIgnoreCase)
                    && !kToplam.Contains("online", StringComparison.OrdinalIgnoreCase))
                {
                    harc = ReadDec(kToplam);
                }
            }

            var gelirVergisi = ReadDecFallback("gelir_vergisi", "gelir", "verg");
            var damgaVergisi = ReadDecFallback("damga_vergisi", "damga", "verg");

            // Stopaj: önce "stopaj", yoksa (gelir+damga) toplamı.
            var stopaj = ReadDec("stopaj");
            if (stopaj == 0m && !dict.ContainsKey("stopaj"))
            {
                var kStopaj = FindKeyContains("stopaj");
                if (!string.IsNullOrWhiteSpace(kStopaj))
                    stopaj = ReadDec(kStopaj);
            }
            if (stopaj == 0m && !dict.ContainsKey("stopaj"))
                stopaj = gelirVergisi + damgaVergisi;

            return new KasaUstSummary(
                PosTahsilat: posTahsilat,
                OnlineTahsilat: onlineTahsilat,
                PostTahsilat: postTahsilat,
                Tahsilat: tahsilat,
                Reddiyat: reddiyat,
                PosHarc: posHarc,
                OnlineHarc: onlineHarc,
                PostHarc: postHarc,
                GelmeyenPost: gelmeyenPost,
                Harc: harc,
                GelirVergisi: gelirVergisi,
                DamgaVergisi: damgaVergisi,
                Stopaj: stopaj);
}
        catch (Exception ex)
        {
            issues.Add($"KasaÜstRapor TOPLAMLAR okuma hatası: {ex.Message}. Tahsilat/Harç/Reddiyat/Stopaj 0 kabul edildi.");
            return new KasaUstSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    private OnlineReddiyatAgg ReadOnlineReddiyatTotals(DateOnly raporTarihi, string uploadFolderAbsolute, List<string> issues, out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = Path.Combine(uploadFolderAbsolute, "OnlineReddiyat.xlsx");
            if (!File.Exists(full))
            {
                issues.Add("OnlineReddiyat.xlsx bulunamadı. Online reddiyat/stopaj 0 kabul edildi.");
                return new OnlineReddiyatAgg(0m, 0m, "{}");
            }

            var imported = _import.Import(full, ImportFileKind.OnlineReddiyat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"OnlineReddiyat okunamadı: {imported.Error}. Online reddiyat/stopaj 0 kabul edildi.");
                return new OnlineReddiyatAgg(0m, 0m, "{}");
            }

            var table = imported.Value;
            var dateCol = FindDateCanonical(table);
            var miktarCol =
                FindCanonical(table, "odenecek_miktar")
                ?? FindCanonical(table, "miktar")
                ?? FindCanonicalByHeaderContains(table, "miktar");

            // OnlineStopaj: legacy'de Damga + Gelir toplamı bekleniyor.
            // Bazı Excel'lerde bu iki sütun tek bir sütuna düşebilir veya
            // header-contains aramaları aynı canonical sütunu döndürebilir.
            // Bu durumda aynı sütunu iki kez toplarsak değer 2 katına çıkar.
            var gelirVerCol = FindCanonicalByHeaderContains(table, "gelir") ?? FindCanonical(table, "gelir_ver");
            var damgaVerCol = FindCanonicalByHeaderContains(table, "damga") ?? FindCanonical(table, "damga_ver");

            // Tekil stopaj kolonları (duplicate'leri engelle)
            var stopajCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(gelirVerCol)) stopajCols.Add(gelirVerCol);
            if (!string.IsNullOrWhiteSpace(damgaVerCol)) stopajCols.Add(damgaVerCol);

            decimal totalMiktar = 0m;
            decimal totalStopaj = 0m;

            foreach (var row in table.Rows)
            {
                if (row is null) continue;
                if (!fullExcelTotals)
                {
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        if (!RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                    }
                    else
                    {
                        if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(miktarCol) && row.TryGetValue(miktarCol, out var mRaw) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(mRaw, out var m))
                    totalMiktar += m;

                // Stopaj kolonlarını tekil şekilde topla (2x bug'ı engeller)
                foreach (var col in stopajCols)
                {
                    if (row.TryGetValue(col, out var raw) && Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(raw, out var v))
                        totalStopaj += v;
                }
            }

            var dbg = new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["miktarTotal"] = totalMiktar,
                ["stopajTotal"] = totalStopaj,
                ["dateCol"] = dateCol,
                ["miktarCol"] = miktarCol,
                ["gelirVerCol"] = gelirVerCol,
                ["damgaVerCol"] = damgaVerCol,
                ["stopajCols"] = stopajCols.ToArray()
            };
            rawJson = JsonSerializer.Serialize(dbg);

            return new OnlineReddiyatAgg(totalMiktar, totalStopaj, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"OnlineReddiyat hesaplama hatası: {ex.Message}. Online reddiyat/stopaj 0 kabul edildi.");
            return new OnlineReddiyatAgg(0m, 0m, "{}");
        }
    }

    private BankaGunAgg ReadBankaTahsilatGun(DateOnly raporTarihi, string uploadFolderAbsolute, List<string> issues, out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
            if (!File.Exists(full))
            {
                issues.Add("BankaTahsilat.xlsx bulunamadı. Banka günlük özet 0 kabul edildi.");
                return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
            }

            var imported = _import.Import(full, ImportFileKind.BankaTahsilat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"BankaTahsilat okunamadı: {imported.Error}. Banka günlük özet 0 kabul edildi.");
                return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
            }

            var table = imported.Value;
            var dateCol = FindDateCanonical(table) ?? "islem_tarihi";
            var tutarCol = FindCanonical(table, "islem_tutari") 
                ?? FindCanonical(table, "tutar") 
                ?? FindCanonical(table, "miktar") 
                ?? "islem_tutari";
            var bakiyeCol = FindCanonical(table, "islem_sonrasi_bakiye") 
                ?? FindCanonical(table, "bakiye") 
                ?? FindCanonical(table, "son_bakiye")
                ?? "islem_sonrasi_bakiye";

            // Banka export'larında tutar genelde pozitif gelir ve yön bilgisi ayrı bir kolondadır
            // (Borç/Alacak). Eğer bu kolon varsa, tutarı işaretleyerek (signed) hesap yaparız.
            // Böylece Devreden hesabı (İlkSatırBakiye ± Tutar) otomatik doğru çıkar.
            var directionCol =
                FindCanonical(table, "borc_alacak")
                ?? FindCanonicalByHeaderContains(table, "borç")
                ?? FindCanonicalByHeaderContains(table, "borc")
                ?? FindCanonicalByHeaderContains(table, "alacak");

            // Gün satırları (tarih eşleşen)
            var dayRows = new List<(decimal SignedTutar, decimal Bakiye, string? Direction)>();
            foreach (var row in table.Rows)
            {
                if (row is null) continue;
                if (!fullExcelTotals)
                {
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        if (!RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                    }
                    else
                    {
                        if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;
                    }
                }
                if (!row.TryGetValue(tutarCol, out var tRaw) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(tRaw, out var tAbs)) continue;
                if (!row.TryGetValue(bakiyeCol, out var bRaw) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(bRaw, out var b)) continue;

                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = ApplyDebitCreditSign(tAbs, dir);

                dayRows.Add((signed, b, dir));
            }

            if (dayRows.Count == 0)
            {
                // Gün satırı yoksa: en azından son bakiyeyi koy (banka bakiye motoruyla uyumlu)
                return new BankaGunAgg(0m, 0m, 0m, 0m, JsonSerializer.Serialize(new { date = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), note = "no rows" }));
            }

            decimal giren = 0m;
            decimal cikan = 0m;
            foreach (var (signedTutar, _, _) in dayRows)
            {
                if (signedTutar >= 0) giren += signedTutar;
                else cikan += Math.Abs(signedTutar);
            }

            // Devreden: ilk satırdaki (sonrasi bakiye - signed tutar)
            // - Alacak (+): Devreden = Bakiye - Tutar
            // - Borç  (-): Devreden = Bakiye + Tutar
            var first = dayRows[0];
            var devreden = first.Bakiye - first.SignedTutar;
            var yarina = dayRows[^1].Bakiye;

            var dbg = new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["devreden"] = devreden,
                ["yarina"] = yarina,
                ["giren"] = giren,
                ["cikan"] = cikan,
                ["dateCol"] = dateCol,
                ["tutarCol"] = tutarCol,
                ["bakiyeCol"] = bakiyeCol,
                ["directionCol"] = directionCol,
                ["firstDirection"] = dayRows[0].Direction,
                ["rowCount"] = dayRows.Count
            };

            rawJson = JsonSerializer.Serialize(dbg);
            return new BankaGunAgg(devreden, yarina, giren, cikan, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"BankaTahsilat günlük özet hatası: {ex.Message}. Banka günlük özet 0 kabul edildi.");
            return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
        }
    }

    
    
    private sealed record BankaTahsilatExtraInflowAgg(
        decimal EftOtomatikIade,
        decimal GelenHavale,
        decimal IadeKelimesiGiris,
        decimal IslemDisiToplam,
        int EftCount,
        int HavaleCount,
        int IadeCount,
        string RawJson);

    /// <summary>
    /// BankaTahsilat.xlsx içinden "işlem dışı banka girişleri"ni yakalar:
    /// - İşlem Adı = "Gelen EFT Otomatik Yatan"
    /// - İşlem Adı = "Gelen Havale"
    /// - Açıklama içinde "iade" geçen (SADECE bankaya giren) satırlar
    ///
    /// Kural (Ali - KİLİTLİ):
    /// - iade tespitleri, EFT/Havale ile çakışıyorsa mükerrer sayılmaz.
    /// - Sadece bankaya GİREN (alacak) satırlar değerlendirilir.
    /// </summary>
    private BankaTahsilatExtraInflowAgg ReadBankaTahsilatExtraInflowAgg(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;

        try
        {
            var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
            if (!File.Exists(full))
            {
                issues.Add("BankaTahsilat.xlsx bulunamadı. (EFT Otomatik İade / Gelen Havale / iade) 0 kabul edildi.");
                rawJson = "{}";
                return new BankaTahsilatExtraInflowAgg(0m, 0m, 0m, 0m, 0, 0, 0, rawJson);
            }

            var imported = _import.Import(full, ImportFileKind.BankaTahsilat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"BankaTahsilat okunamadı (extra inflow): {imported.Error}. 0 kabul edildi.");
                rawJson = "{}";
                return new BankaTahsilatExtraInflowAgg(0m, 0m, 0m, 0m, 0, 0, 0, rawJson);
            }

            var table = imported.Value;

            var dateCol = FindDateCanonical(table) ?? "islem_tarihi";
            var tutarCol = FindCanonical(table, "islem_tutari") ?? "islem_tutari";
            var bakiyeCol = FindCanonical(table, "islem_sonrasi_bakiye") ?? "islem_sonrasi_bakiye";
            var islemAdiCol = FindCanonical(table, "islem_adi") ?? "islem_adi";
            var aciklamaCol = FindCanonical(table, "aciklama") ?? "aciklama";

            var directionCol =
                FindCanonical(table, "borc_alacak")
                ?? FindCanonicalByHeaderContains(table, "borç")
                ?? FindCanonicalByHeaderContains(table, "borc")
                ?? FindCanonicalByHeaderContains(table, "alacak");

            static string N(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;

                // TR i/İ normalize: "İADE" vb. sağlam yakalansın
                return s.Trim()
                    .Replace('İ', 'I')
                    .Replace('ı', 'i')
                    .ToLowerInvariant();
            }

            bool IsMatchIslemAdi(string? islemAdi, string needle)
                => N(islemAdi).Contains(N(needle));

            bool ContainsIade(string? aciklama)
                => N(aciklama).Contains("iade");

            decimal eft = 0m, havale = 0m, iade = 0m;
            int eftCount = 0, havaleCount = 0, iadeCount = 0;

            // Mükerrer engelleme: Önce EFT/Havale seç, sonra "iade"yi geri kalanlardan al.
            var alreadyCounted = new HashSet<int>();

            // Debug samples
            var eftSamples = new List<Dictionary<string, object?>>();
            var havaleSamples = new List<Dictionary<string, object?>>();
            var iadeSamples = new List<Dictionary<string, object?>>();

            int rowIndex = -1;
            foreach (var row in table.Rows)
            {
                rowIndex++;
                if (row is null) continue;

                if (!fullExcelTotals)
                {
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        if (!RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                    }
                    else
                    {
                        if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;
                    }
                }

                if (!row.TryGetValue(tutarCol, out var tRaw) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(tRaw, out var tAbs)) continue;

                // Yön/işaret
                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = ApplyDebitCreditSign(tAbs, dir);

                // Sadece bankaya giren (alacak) satırlar
                if (signed <= 0m) continue;

                row.TryGetValue(islemAdiCol, out var islemAdi);
                row.TryGetValue(aciklamaCol, out var aciklama);
                row.TryGetValue(bakiyeCol, out var bakiyeRaw);
                _ = Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(bakiyeRaw, out var bakiye);

                // 1) İşlem Adı: EFT Otomatik İade
                if (IsMatchIslemAdi(islemAdi, "Gelen EFT Otomatik Yatan"))
                {
                    eft += signed;
                    eftCount++;
                    alreadyCounted.Add(rowIndex);

                    if (eftSamples.Count < 5)
                    {
                        eftSamples.Add(new Dictionary<string, object?>
                        {
                            ["row"] = rowIndex,
                            ["tutar"] = signed,
                            ["bakiye"] = bakiyeRaw,
                            ["islemAdi"] = islemAdi,
                            ["aciklama"] = aciklama
                        });
                    }

                    continue;
                }

                // 2) İşlem Adı: Gelen Havale
                if (IsMatchIslemAdi(islemAdi, "Gelen Havale"))
                {
                    havale += signed;
                    havaleCount++;
                    alreadyCounted.Add(rowIndex);

                    if (havaleSamples.Count < 5)
                    {
                        havaleSamples.Add(new Dictionary<string, object?>
                        {
                            ["row"] = rowIndex,
                            ["tutar"] = signed,
                            ["bakiye"] = bakiyeRaw,
                            ["islemAdi"] = islemAdi,
                            ["aciklama"] = aciklama
                        });
                    }

                    continue;
                }

                // Diğerleri: şimdilik bekle (iade için ikinci pass)
            }

            // 2. pass: iade kelimesi (mükerrer olmasın)
            rowIndex = -1;
            foreach (var row in table.Rows)
            {
                rowIndex++;
                if (row is null) continue;

                if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;

                if (!row.TryGetValue(tutarCol, out var tRaw) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(tRaw, out var tAbs)) continue;

                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = ApplyDebitCreditSign(tAbs, dir);

                if (signed <= 0m) continue; // sadece giriş

                if (alreadyCounted.Contains(rowIndex)) continue; // mükerrer engelle

                row.TryGetValue(aciklamaCol, out var aciklama);
                if (!ContainsIade(aciklama)) continue;

                iade += signed;
                iadeCount++;
                alreadyCounted.Add(rowIndex);

                row.TryGetValue(islemAdiCol, out var islemAdi);
                row.TryGetValue(bakiyeCol, out var bakiyeRaw);

                if (iadeSamples.Count < 5)
                {
                    iadeSamples.Add(new Dictionary<string, object?>
                    {
                        ["row"] = rowIndex,
                        ["tutar"] = signed,
                        ["bakiye"] = bakiyeRaw,
                        ["islemAdi"] = islemAdi,
                        ["aciklama"] = aciklama
                    });
                }
            }

            var total = eft + havale + iade;

            var dbg = new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["file"] = Path.GetFileName(full),
                ["dateCol"] = dateCol,
                ["tutarCol"] = tutarCol,
                ["bakiyeCol"] = bakiyeCol,
                ["directionCol"] = directionCol,
                ["islemAdiCol"] = islemAdiCol,
                ["aciklamaCol"] = aciklamaCol,
                ["eftOtomatikIade"] = eft,
                ["gelenHavale"] = havale,
                ["iadeKelimesiGiris"] = iade,
                ["toplamIslemDisi"] = total,
                ["eftCount"] = eftCount,
                ["havaleCount"] = havaleCount,
                ["iadeCount"] = iadeCount,
                ["samples"] = new Dictionary<string, object?>
                {
                    ["eft"] = eftSamples,
                    ["havale"] = havaleSamples,
                    ["iade"] = iadeSamples
                }
            };

            rawJson = JsonSerializer.Serialize(dbg);
            return new BankaTahsilatExtraInflowAgg(eft, havale, iade, total, eftCount, havaleCount, iadeCount, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"BankaTahsilat extra inflow hatası: {ex.Message}. 0 kabul edildi.");
            rawJson = "{}";
            return new BankaTahsilatExtraInflowAgg(0m, 0m, 0m, 0m, 0, 0, 0, rawJson);
        }
    }


    private sealed record BankaOutflowByTypeAgg(
        decimal MevduatYatirma,
        decimal Virman,
        int MevduatCount,
        int VirmanCount,
        string RawJson);

    /// <summary>
    /// Banka dosyasından borç (çıkan) satırlarını İşlem Adı'na göre filtreler:
    /// - "Mevduata Para Yatırma" → bankaya yatırılan fiziksel para
    /// - "Virman" → virman transferi (stopaj)
    /// Sabah Kasa kontrolü için kullanılır.
    /// </summary>
    private BankaOutflowByTypeAgg ReadBankaOutflowByType(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        string fileName,
        ImportFileKind fileKind,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains('/')
                ? fileName
                : (ResolveExistingFile(uploadFolderAbsolute, fileName) ?? Path.Combine(uploadFolderAbsolute, fileName));

            if (!File.Exists(full))
            {
                issues.Add($"{fileName} bulunamadı. Mevduat/Virman çıkış 0 kabul edildi.");
                rawJson = "{}";
                return new BankaOutflowByTypeAgg(0m, 0m, 0, 0, rawJson);
            }

            var imported = _import.Import(full, fileKind);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"{fileName} okunamadı (outflow): {imported.Error}. 0 kabul edildi.");
                rawJson = "{}";
                return new BankaOutflowByTypeAgg(0m, 0m, 0, 0, rawJson);
            }

            var table = imported.Value;
            var dateCol = FindDateCanonical(table) ?? "islem_tarihi";
            var tutarCol = FindCanonical(table, "islem_tutari") ?? "islem_tutari";
            var islemAdiCol = FindCanonical(table, "islem_adi") ?? "islem_adi";
            
            // Commit 5.2: Yön filtresi için directionCol tespiti
            var directionCol =
                FindCanonical(table, "borc_alacak")
                ?? FindCanonicalByHeaderContains(table, "borç")
                ?? FindCanonicalByHeaderContains(table, "borc")
                ?? FindCanonicalByHeaderContains(table, "alacak");

            static string N(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                return s.Trim()
                    .Replace('İ', 'I')
                    .Replace('ı', 'i')
                    .ToLowerInvariant();
            }

            decimal mevduat = 0m, virman = 0m;
            int mevduatCount = 0, virmanCount = 0;
            var mevduatSamples = new List<Dictionary<string, object?>>();
            var virmanSamples = new List<Dictionary<string, object?>>();

            int rowIndex = -1;
            foreach (var row in table.Rows)
            {
                rowIndex++;
                if (row is null) continue;

                if (!fullExcelTotals)
                {
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        if (!RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                    }
                    else
                    {
                        if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;
                    }
                }

                if (!row.TryGetValue(tutarCol, out var tRaw) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(tRaw, out var tAbs)) continue;

                // Commit 5.2: Yön/işaret belirleme
                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = ApplyDebitCreditSign(tAbs, dir);

                row.TryGetValue(islemAdiCol, out var islemAdi);
                var nIslem = N(islemAdi);

                // Sadece "Virman" işlemleri için bankadan ÇIKAN (borç) satırları topla — İptal iadelerini (alacak/giren) atla.
                // Mevduata Para Yatırma gibi işlemler banka excel'inde alacak yönlü kayıtlı olabileceği için onları atlamıyoruz.
                if (signed > 0m && nIslem.Contains("virman")) continue;

                // İşlem Adı'na göre filtreleme
                var absAmount = Math.Abs(signed);

                // "Mevduata Para Yatırma" — "Mevduat Para Yatır" gibi kısaltmalar da dahil
                if (nIslem.Contains("mevduata para") || nIslem.Contains("mevduat para") || nIslem.Contains("mevduat yatir"))
                {
                    mevduat += absAmount;
                    mevduatCount++;
                    if (mevduatSamples.Count < 5)
                        mevduatSamples.Add(new Dictionary<string, object?> { ["row"] = rowIndex, ["tutar"] = absAmount, ["islemAdi"] = islemAdi });
                    continue;
                }

                // "Virman"
                if (nIslem.Contains("virman"))
                {
                    virman += absAmount;
                    virmanCount++;
                    if (virmanSamples.Count < 5)
                        virmanSamples.Add(new Dictionary<string, object?> { ["row"] = rowIndex, ["tutar"] = absAmount, ["islemAdi"] = islemAdi });
                }
            }

            var dbg = new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["file"] = Path.GetFileName(full),
                ["mevduatYatirma"] = mevduat,
                ["virman"] = virman,
                ["mevduatCount"] = mevduatCount,
                ["virmanCount"] = virmanCount,
                ["samples"] = new Dictionary<string, object?> { ["mevduat"] = mevduatSamples, ["virman"] = virmanSamples }
            };

            rawJson = JsonSerializer.Serialize(dbg);
            return new BankaOutflowByTypeAgg(mevduat, virman, mevduatCount, virmanCount, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"{fileName} outflow okuma hatası: {ex.Message}. 0 kabul edildi.");
            rawJson = "{}";
            return new BankaOutflowByTypeAgg(0m, 0m, 0, 0, rawJson);
        }
    }
}
