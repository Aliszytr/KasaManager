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

// ReadersHarc: ReadBankaHarcGun, ReadOnlineTotal, ReadMasrafReddiyatAggUnified, ResolveExistingFile
public sealed partial class KasaDraftService
{

// =========================
    // R8 – Ek okuma yardımcıları
    // =========================

    private BankaGunAgg ReadBankaHarcGun(DateOnly raporTarihi, string uploadFolderAbsolute, List<string> issues, out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = ResolveExistingFile(uploadFolderAbsolute, "BankaHarc.xlsx");
            if (full is null)
            {
                issues.Add("BankaHarc.xlsx bulunamadı. Banka harc günlük özet 0 kabul edildi.");
                return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
            }

            var imported = _import.Import(full, ImportFileKind.BankaHarcama);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"BankaHarc okunamadı: {imported.Error}. Banka harc günlük özet 0 kabul edildi.");
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

            var directionCol =
                FindCanonical(table, "borc_alacak")
                ?? FindCanonicalByHeaderContains(table, "borç")
                ?? FindCanonicalByHeaderContains(table, "borc")
                ?? FindCanonicalByHeaderContains(table, "alacak");

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
                rawJson = JsonSerializer.Serialize(new
                {
                    date = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    note = "no rows"
                });
                return new BankaGunAgg(0m, 0m, 0m, 0m, rawJson);
            }

            decimal giren = 0m;
            decimal cikan = 0m;
            foreach (var (signedTutar, _, _) in dayRows)
            {
                if (signedTutar >= 0) giren += signedTutar;
                else cikan += Math.Abs(signedTutar);
            }

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
            issues.Add($"BankaHarc günlük özet hatası: {ex.Message}. Banka harc günlük özet 0 kabul edildi.");
            return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
        }
    }

    private decimal ReadOnlineTotal(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        string fileName,
        ImportFileKind kind,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = ResolveExistingFile(uploadFolderAbsolute, fileName);
            if (full is null)
            {
                issues.Add($"{fileName} bulunamadı. {kind} toplamı 0 kabul edildi.");
                return 0m;
            }

            var imported = _import.Import(full, kind);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"{fileName} okunamadı: {imported.Error}. Toplam 0 kabul edildi.");
                return 0m;
            }

            var table = imported.Value;
            var dateCol = FindDateCanonical(table); // online dosyalarında da tarih olabilir
            var miktarCol = FindCanonical(table, "miktar") 
                ?? FindCanonical(table, "islem_tutari") 
                ?? FindCanonical(table, "tutar") 
                ?? "miktar";

            decimal total = 0m;
            int matched = 0;

            foreach (var row in table.Rows)
            {
                if (row is null) continue;

                if (dateCol is not null)
                {
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
                }
                // Tarih kolonu yoksa: bu rapor "tek gün" kabul edilir ve tüm satırlar toplanır.

                if (!row.TryGetValue(miktarCol, out var raw)) continue;
                if (!Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(raw, out var v)) continue;

                total += v;
                matched++;
            }

            rawJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["file"] = Path.GetFileName(full),
                ["kind"] = kind.ToString(),
                ["dateCol"] = dateCol,
                ["miktarCol"] = miktarCol,
                ["rowCount"] = table.Rows.Count,
                ["matchedRows"] = matched,
                ["total"] = total
            });

            return total;
        }
        catch (Exception ex)
        {
            issues.Add($"{fileName} toplam hesaplama hatası: {ex.Message}. Toplam 0 kabul edildi.");
            return 0m;
        }
    }

    private sealed record MasrafReddiyatAgg(decimal Masraf, decimal Reddiyat, decimal Diger, string RawJson);

    private MasrafReddiyatAgg ReadMasrafReddiyatAggUnified(
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
            var full = ResolveExistingFile(uploadFolderAbsolute, "MasrafveReddiyat.xlsx");
            if (full is null)
            {
                issues.Add("MasrafveReddiyat.xlsx bulunamadı. Masraf/Reddiyat 0 kabul edildi.");
                return new MasrafReddiyatAgg(0m, 0m, 0m, "{}");
            }

            var imported = _import.ImportTrueSource(full, ImportFileKind.MasrafVeReddiyat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"MasrafveReddiyat okunamadı: {imported.Error}. Masraf/Reddiyat 0 kabul edildi.");
                return new MasrafReddiyatAgg(0m, 0m, 0m, "{}");
            }

            var table = imported.Value;
            var dateCol = FindDateCanonical(table);
            var tipCol = FindCanonical(table, "tip") 
                ?? FindCanonical(table, "tur") 
                ?? FindCanonical(table, "islem_tipi") 
                ?? FindCanonicalByHeaderContains(table, "tip") 
                ?? FindCanonicalByHeaderContains(table, "tür")
                ?? FindCanonicalByHeaderContains(table, "tur")
                ?? FindCanonicalByHeaderContains(table, "nevi")
                ?? "tip";

            var miktarCol = FindCanonical(table, "miktar") 
                ?? FindCanonical(table, "islem_tutari") 
                ?? FindCanonical(table, "tutar") 
                ?? FindCanonical(table, "net_tutar")
                ?? FindCanonicalByHeaderContains(table, "tutar") 
                ?? "miktar";
            
            // Debug: Dosyada bulduğumuz tüm kolonları virgülle birleştir (Opsiyonel: kalabilir, zararı yok)
            var allColumns = table.ColumnMetas != null 
                ? string.Join(", ", table.ColumnMetas.Select(x => $"{x.CanonicalName} ({x.OriginalHeader})")) 
                : "Yok";


            decimal masraf = 0m;
            decimal reddiyat = 0m;
            decimal diger = 0m;
            
            int matched = 0;
            int inRangeRows = 0;
            int masrafRows = 0;
            int reddiyatRows = 0;
            int digerRows = 0;




            foreach (var row in table.Rows)
            {
                if (row is null) continue;

                // Tarih Filtresi
                if (!fullExcelTotals)
                {
                    if (dateCol is not null)
                    {
                        if (rangeStart.HasValue && rangeEnd.HasValue)
                        {
                            // Range Mode
                            if (!RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                        }
                        else
                        {
                            // Rapor Tarihi Mode (Tek Gün)
                            if (!RowMatchesDate(row, dateCol, raporTarihi)) continue;
                        }
                    }
                }

                inRangeRows++;
                if (!row.TryGetValue(miktarCol, out var rawM) || !Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(rawM, out var m)) 
                {
                    // Debug: Miktar parse edilemedi
                    continue;
                }

                // If we are here, date matched & miktar parsed
                                
                var tip = row.GetValueOrDefault(tipCol) ?? string.Empty;

                var tipU = tip.Trim().ToUpperInvariant();



                if (tipU.Contains("MASRAF") || tipU.Contains("GİDER") || tipU.Contains("GIDER") || tipU.Contains("HARCAMA"))
                { 
                    masraf += m; 
                    masrafRows++; 
                }
                else if (tipU.Contains("REDD"))
                { 
                    reddiyat += m; 
                    reddiyatRows++; 
                }
                else
                { 
                    diger += m; 
                    digerRows++; 
                }

                matched++;
            }

            if (masraf == 0 && reddiyat == 0 && table.Rows.Count > 0)
            {
               issues.Add($"MasrafveReddiyat UYARI: {table.Rows.Count} satır tarandı ancak Masraf/Reddiyat 0 çıktı. Filtre={(!fullExcelTotals ? (rangeStart.HasValue ? "Aralık" : "TekGün") : "Yok")}. Kolonlar: Tip='{tipCol}', Miktar='{miktarCol}', Tarih='{dateCol}'. InRange={inRangeRows}, Matched={matched}.");
            }

            rawJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["mode"] = fullExcelTotals ? "FullExcel" : (rangeStart.HasValue ? "Range" : "SingleDay"),
                ["range"] = rangeStart.HasValue ? $"{rangeStart:dd.MM}-{rangeEnd:dd.MM}" : null,
                ["file"] = Path.GetFileName(full),
                ["dateCol"] = dateCol,
                ["tipCol"] = tipCol,
                ["miktarCol"] = miktarCol,
                ["rowCount"] = table.Rows.Count,
                ["inRangeRows"] = inRangeRows, // <-- Bu önemli: Tarih tuttu mu?
                ["matchedRows"] = matched,     // <-- Bu önemli: Miktar parse oldu mu?
                ["masrafRows"] = masrafRows,
                ["reddiyatRows"] = reddiyatRows,
                ["digerRows"] = digerRows,
                ["masraf"] = masraf,
                ["reddiyat"] = reddiyat,
                ["diger"] = diger,
                ["allColumns"] = allColumns
            });

            return new MasrafReddiyatAgg(masraf, reddiyat, diger, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"MasrafveReddiyat hesaplama hatası: {ex.Message}. Masraf/Reddiyat 0 kabul edildi.");
            return new MasrafReddiyatAgg(0m, 0m, 0m, "{}");
        }
    }



    private static string? ResolveExistingFile(string folder, string fileName)
    {
        try
        {
            var full = Path.Combine(folder, fileName);
            if (File.Exists(full)) return full;

            // Case-insensitive fallback (Windows için şart değil ama Drive/host farkı olabilir)
            var target = fileName.Trim().ToLowerInvariant();
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();
            foreach (var f in files)
            {
                if (Path.GetFileName(f).Trim().ToLowerInvariant() == target)
                    return f;
            }

            // Fallback 2: Upload klasörü yanlış seçilmiş olabilir (örn: Data\\Raporlar yerine Data\\Raporlar1)
            // Aynı parent altındaki "Raporlar*" klasörlerinde de arayalım.
            var parent = Directory.GetParent(folder)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                foreach (var dir in Directory.GetDirectories(parent, "Raporlar*"))
                {
                    try
                    {
                        var p = Path.Combine(dir, fileName);
                        if (File.Exists(p)) return p;

                        var files2 = Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
                        foreach (var f2 in files2)
                        {
                            if (Path.GetFileName(f2).Trim().ToLowerInvariant() == target)
                                return f2;
                        }
                    }
                    catch
                    {
                        // ignore per-folder
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
