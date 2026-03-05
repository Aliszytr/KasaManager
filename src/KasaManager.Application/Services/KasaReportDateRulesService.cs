using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using System.Text.RegularExpressions;

namespace KasaManager.Application.Services;

/// <summary>
/// R7 Tarih Kural Motoru.
/// - Excel'e kolon eklemez, değiştirmez.
/// - Upload klasöründeki raporların mevcut tarih sütunlarını tarar.
/// - Tüm rapor tarihlerini kıyaslar, çakışmaları raporlar.
/// - DB'deki son snapshot tarihiyle süreklilik kontrolü yapar.
/// </summary>
public sealed class KasaReportDateRulesService : IKasaReportDateRulesService
{
    private static readonly ImportFileKind[] _dateBearingKinds = new[]
    {
        ImportFileKind.BankaTahsilat,
        ImportFileKind.BankaHarcama,
        ImportFileKind.OnlineMasraf,
        ImportFileKind.OnlineHarcama,
        ImportFileKind.OnlineReddiyat,
    };

    private readonly IImportOrchestrator _orchestrator;
    private readonly IKasaRaporSnapshotService _snapshots;

    public KasaReportDateRulesService(IImportOrchestrator orchestrator, IKasaRaporSnapshotService snapshots)
    {
        _orchestrator = orchestrator;
        _snapshots = snapshots;
    }

    public async Task<DateRulesEvaluation> EvaluateAsync(string uploadFolderAbsolute, CancellationToken ct = default)
    {
        var eval = new DateRulesEvaluation();

        if (string.IsNullOrWhiteSpace(uploadFolderAbsolute) || !Directory.Exists(uploadFolderAbsolute))
        {
            return eval with
            {
                Errors = new List<string> { "Upload klasörü bulunamadı." }
            };
        }

        // DB süreklilik referansı (KasaÜstRapor = Genel)
        var last = await _snapshots.GetLastSnapshotDateAsync(KasaRaporTuru.Genel, ct);
        DateOnly? expected = last.HasValue ? last.Value.AddDays(1) : null;

        var sources = new List<DateRulesSourceDate>();

        // Klasördeki tüm excel dosyalarını tara
        var allFiles = Directory.EnumerateFiles(uploadFolderAbsolute)
            .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        foreach (var kind in _dateBearingKinds)
        {
            var file = PickBestFileForKind(allFiles, kind);
            if (file == null)
            {
                sources.Add(new DateRulesSourceDate
                {
                    Kind = kind,
                    FileName = "(yok)",
                    DistinctDates = new List<DateOnly>(),
                    ParsedRowCount = 0,
                    TotalRowCount = 0
                });
                continue;
            }

            var import = _orchestrator.Import(file.FullName, kind);
            if (!import.Ok || import.Value is null)
            {
                sources.Add(new DateRulesSourceDate
                {
                    Kind = kind,
                    FileName = file.Name,
                    DistinctDates = new List<DateOnly>(),
                    ParsedRowCount = 0,
                    TotalRowCount = 0
                });
                continue;
            }

            var table = import.Value;
            var dates = ExtractDistinctDates(table);

            sources.Add(new DateRulesSourceDate
            {
                Kind = kind,
                FileName = file.Name,
                DistinctDates = dates.OrderBy(x => x).ToList(),
                ParsedRowCount = dates.Count > 0 ? CountRowsWithParseableDate(table) : 0,
                TotalRowCount = table.Rows?.Count ?? 0
            });
        }

        var allDistinct = sources
            .SelectMany(s => s.DistinctDates)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var hasAny = allDistinct.Count > 0;

        // Çakışma: (a) bir kaynakta birden fazla tarih (b) farklı kaynaklar farklı tarih
        var anySourceConflicting = sources.Any(s => s.IsConflicting);
        var crossConflict = allDistinct.Count > 1;
        var hasConflict = anySourceConflicting || crossConflict;

        DateOnly? proposed = null;
        if (!hasAny)
        {
            // Hiç tarih yoksa: DB beklenen tarihi öner, yoksa null
            proposed = expected;
        }
        else if (!hasConflict)
        {
            proposed = allDistinct[0];
        }
        else
        {
            // Çakışmada otomatik "doğru" seçmeyelim → en sık olanı öner ama "onay" gerektir.
            proposed = MostFrequentDate(sources) ?? allDistinct.Last();
        }

        var warnings = new List<string>();

        // Eksik raporlar
        foreach (var s in sources)
        {
            if (s.FileName == "(yok)")
                warnings.Add($"{s.Kind}: dosya bulunamadı.");
            else if (!s.HasDate)
                warnings.Add($"{s.Kind}: tarih tespit edilemedi (satır yok veya tarih kolonu okunamadı)." );
            else if (s.IsConflicting)
                warnings.Add($"{s.Kind}: dosya içinde birden fazla tarih var: {string.Join(", ", s.DistinctDates)}");
        }

        if (crossConflict)
            warnings.Add($"Raporlar arasında tarih uyuşmazlığı var: {string.Join(", ", allDistinct)}");

        // Süreklilik
        var continuityOk = true;
        if (expected.HasValue && proposed.HasValue && proposed.Value != expected.Value)
        {
            continuityOk = false;
            warnings.Add($"DB süreklilik uyarısı: Son kayıt {last:yyyy-MM-dd}. Beklenen {expected:yyyy-MM-dd} ama tespit edilen/önerilen {proposed:yyyy-MM-dd}." );
        }

        return new DateRulesEvaluation
        {
            ProposedDate = proposed,
            FinalSuggestedDate = proposed,
            DbLastSnapshotDate = last,
            DbExpectedNextDate = expected,
            HasConflict = hasConflict,
            HasAnyDate = hasAny,
            ContinuityLooksOk = continuityOk,
            Sources = sources,
            Warnings = warnings
        };
    }

    private static FileInfo? PickBestFileForKind(List<FileInfo> files, ImportFileKind kind)
    {
        // Aynı türden birden fazla dosya varsa: en son yazılanı al.
        foreach (var f in files)
        {
            var guessed = GuessKindFromName(f.Name);
            if (guessed == kind)
                return f;
        }
        return null;
    }

    private static ImportFileKind GuessKindFromName(string fileName)
    {
        var n = (fileName ?? string.Empty).ToLowerInvariant();

        // Banka raporları
        if (n.Contains("bankahar") || (n.Contains("banka") && n.Contains("harc")))
            return ImportFileKind.BankaHarcama;

        if (n.Contains("bankatah") || (n.Contains("banka") && n.Contains("tah")))
            return ImportFileKind.BankaTahsilat;

        // Online raporlar (önce kontrol edilmeli; aksi halde 'reddiyat'/'masraf' ile MasrafVeReddiyat'a düşer)
        if (n.Contains("online") && n.Contains("harc"))
            return ImportFileKind.OnlineHarcama;

        if (n.Contains("online") && n.Contains("masraf"))
            return ImportFileKind.OnlineMasraf;

        if (n.Contains("online") && (n.Contains("redd") || n.Contains("reddiyat")))
            return ImportFileKind.OnlineReddiyat;

        // Masraf / Reddiyat birleşik raporu (en sonda)
        if (n.Contains("masrafveredd") || (n.Contains("masraf") && n.Contains("redd")))
            return ImportFileKind.MasrafVeReddiyat;

        return ImportFileKind.Unknown;
    }

    private static List<DateOnly> ExtractDistinctDates(ImportedTable table)
    {
        var set = new HashSet<DateOnly>();
        if (table.Rows is null || table.Rows.Count == 0) return new List<DateOnly>();

        var dateCanonical = FindDateCanonical(table);
        if (string.IsNullOrWhiteSpace(dateCanonical)) return new List<DateOnly>();

        foreach (var row in table.Rows)
        {
            if (row == null) continue;
            if (!row.TryGetValue(dateCanonical, out var raw)) continue;
            if (TryParseDateOnly(raw, out var d)) set.Add(d);
        }

        return set.ToList();
    }

    private static int CountRowsWithParseableDate(ImportedTable table)
    {
        if (table is null) return 0;

        var dateCanonical = FindDateCanonical(table);
        if (string.IsNullOrWhiteSpace(dateCanonical) || table.Rows is null) return 0;

        var count = 0;
        foreach (var row in table.Rows)
        {
            if (row == null) continue;
            if (!row.TryGetValue(dateCanonical, out var raw)) continue;
            if (TryParseDateOnly(raw, out _)) count++;
        }

        return count;
    }

    private static string? FindDateCanonical(ImportedTable table)
    {
        // R7: Excel'e kolon ekleme yok.
        // Mevcut kolonlardan tarih barındıranı buluruz (canonical + original header + data-based fallback).

        if (table?.ColumnMetas == null || table.ColumnMetas.Count == 0) return null;

        // 1) Bilinen canonical isimler (profil bazlı varyasyonlar + "Tar." kısaltması)
        var candidates = new[]
        {
            "tarih",
            "islem_tarihi",
            "işlem_tarihi",
            "islem_tarih",
            "tahsilat_tarihi",
            "reddiyat_tarihi",
            "reddiyat_tar",
            "tar",
        };

        foreach (var cand in candidates)
        {
            var hit = table.ColumnMetas.FirstOrDefault(m =>
                string.Equals(m.CanonicalName, cand, StringComparison.OrdinalIgnoreCase));

            if (hit != null) return hit.CanonicalName;
        }

        // 2) OriginalHeader üzerinden "tarih" veya "tar." yakala (ör: "Reddiyat Tar.")
        var byHeader = table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.OriginalHeader) &&
            (m.OriginalHeader.Contains("tarih", StringComparison.OrdinalIgnoreCase) ||
             m.OriginalHeader.Contains("tar.", StringComparison.OrdinalIgnoreCase) ||
             Regex.IsMatch(m.OriginalHeader, @"\btar\b", RegexOptions.IgnoreCase)));

        if (byHeader != null) return byHeader.CanonicalName;

        // 3) Data-based fallback: en çok parse edilebilen tarih hücresi olan kolonu seç
        if (table.Rows != null && table.Rows.Count > 0)
        {
            var best = table.ColumnMetas
                .Select(m => new
                {
                    Canonical = m.CanonicalName,
                    Hits = CountDateParsableInColumn(table, m.CanonicalName)
                })
                .OrderByDescending(x => x.Hits)
                .FirstOrDefault();

            // En azından birkaç satır parse edilebiliyorsa anlamlıdır
            if (best != null && best.Hits >= 2 && !string.IsNullOrWhiteSpace(best.Canonical))
                return best.Canonical;
        }

        // 4) Son çare: canonical içinde "tarih" geçen ilk kolon
        var fallback = table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.CanonicalName) &&
            m.CanonicalName.Contains("tarih", StringComparison.OrdinalIgnoreCase));

        return fallback?.CanonicalName;
    }

    private static int CountDateParsableInColumn(ImportedTable table, string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical) || table.Rows is null) return 0;

        var hits = 0;
        foreach (var row in table.Rows)
        {
            if (row == null) continue;
            if (!row.TryGetValue(canonical, out var raw)) continue;
            if (TryParseDateOnly(raw, out _)) hits++;
        }

        return hits;
    }

    private static bool TryParseDateOnly(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Trim();

        // ExcelDataReader bazen OADate sayısı döndürebilir
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa) && oa > 1000)
        {
            try
            {
                var dt = DateTime.FromOADate(oa);
                date = DateOnly.FromDateTime(dt);
                return true;
            }
            catch
            {
                // ignore
            }
        }

        // TR formatlar
        var formats = new[] { "d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy" };
        if (DateTime.TryParseExact(raw, formats, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var dt1))
        {
            date = DateOnly.FromDateTime(dt1);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var dt2))
        {
            date = DateOnly.FromDateTime(dt2);
            return true;
        }

        return false;
    }

    private static DateOnly? MostFrequentDate(List<DateRulesSourceDate> sources)
    {
        var counts = new Dictionary<DateOnly, int>();
        foreach (var s in sources)
        {
            // Bir kaynaktan birden çok tarih çıktıysa, hepsini saymayalım; kaynağı "conflict" sayıp geç.
            if (s.DistinctDates.Count != 1) continue;
            var d = s.DistinctDates[0];
            counts.TryGetValue(d, out var c);
            counts[d] = c + 1;
        }

        if (counts.Count == 0) return null;
        return counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
    }
}
