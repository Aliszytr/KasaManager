#nullable enable
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 1: Unified Data Pipeline - tek veri kaynağı.
/// Tüm Excel reader'ları ve diğer kaynakları tek bir akışta birleştirir.
/// </summary>
public sealed class UnifiedDataPipeline : IDataPipeline
{
    private readonly IKasaDraftService _draftService;
    private readonly IKasaGlobalDefaultsService _defaults;
    private readonly IKasaRaporSnapshotService _snapshotService;

    // Pipeline seviyesinde çok kısa süreli cache (aynı tarih için peş peşe çağrıları hızlandırır).
    // Özellikle sol menü "alanları getir" + ardından "şablon yükle/hesapla" senaryosunda.
    private static readonly ConcurrentDictionary<string, (DateTimeOffset createdAt, IReadOnlyList<UnifiedPoolEntry> entries)> _poolCache = new();
    private static readonly TimeSpan _poolCacheTtl = TimeSpan.FromMinutes(2);
    
    public UnifiedDataPipeline(
        IKasaDraftService draftService,
        IKasaGlobalDefaultsService defaults,
        IKasaRaporSnapshotService snapshotService)
    {
        _draftService = draftService;
        _defaults = defaults;
        _snapshotService = snapshotService;
    }
    
    /// <summary>
    /// Pipeline'ı çalıştır ve tüm veri kaynaklarını birleştir.
    /// </summary>
    public async Task<Result<PipelineResult>> ExecuteAsync(PipelineRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var debugLog = new List<string>();
        var warnings = new List<string>();
        var cells = new CellRegistry();
        
        debugLog.Add($"[Pipeline] Başlatılıyor: {request.RaporTarihi:yyyy-MM-dd}, Scope: {request.KasaScope}");
        
        try
        {
            // 1. UnifiedPool'dan Excel verilerini yükle
            var poolResult = await LoadExcelDataAsync(request, debugLog, ct);
            if (!poolResult.Ok)
                return Result<PipelineResult>.Fail(poolResult.Error ?? "Excel veri yükleme hatası");
            
            foreach (var poolEntry in poolResult.Value!)
            {
                var cell = MapPoolEntryToCell(poolEntry);
                cells.Set(cell);
            }
            debugLog.Add($"[Pipeline] Excel: {poolResult.Value!.Count} hücre yüklendi");
            
            // 2. Ayarlar/Defaults yükle
            var defaultsCount = await LoadDefaultsAsync(cells, debugLog, ct);
            debugLog.Add($"[Pipeline] Defaults: {defaultsCount} hücre yüklendi");
            
            // 3. Kullanıcı girişlerini ekle
            if (request.UserInputs is not null)
            {
                var userCount = LoadUserInputs(cells, request.UserInputs);
                debugLog.Add($"[Pipeline] UserInputs: {userCount} hücre yüklendi");
            }
            
            // 4. Carryover (önceki günden taşınan) değerleri yükle
            var carryoverCount = await LoadCarryoverAsync(cells, request, debugLog, ct);
            debugLog.Add($"[Pipeline] Carryover: {carryoverCount} hücre yüklendi");
            
            // 5. FieldCatalog'dan eksik alanları ekle
            var catalogCount = LoadCatalogDefaults(cells);
            debugLog.Add($"[Pipeline] Catalog defaults: {catalogCount} hücre eklendi");
            
            sw.Stop();
            debugLog.Add($"[Pipeline] Tamamlandı: {cells.Count} toplam hücre, {sw.ElapsedMilliseconds}ms");
            
            return Result<PipelineResult>.Success(new PipelineResult
            {
                Cells = cells.ToDictionary(),
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                SourceCount = 5, // Excel + Defaults + UserInputs + Carryover + Catalog
                Warnings = warnings,
                DebugLog = debugLog
            });
        }
        catch (Exception ex)
        {
            debugLog.Add($"[Pipeline] HATA: {ex.Message}");
            return Result<PipelineResult>.Fail($"Pipeline hatası: {ex.Message}");
        }
    }
    
    private async Task<Result<IReadOnlyList<UnifiedPoolEntry>>> LoadExcelDataAsync(
        PipelineRequest request, 
        List<string> debugLog, 
        CancellationToken ct)
    {
        // Mevcut KasaDraftService.BuildUnifiedPoolAsync kullanılıyor.
        // Bu, tüm Excel reader'ları çalıştırır.

        // Aynı request parametreleriyle peş peşe çağrılarda cache'den dön.
        // NOT: kasaScope dahil edilmeli ki farklı scope'lar farklı cache kullanslın!
        var cacheKey = $"{request.RaporTarihi:yyyyMMdd}|{request.UploadFolder}|{request.RangeStart:yyyyMMdd}|{request.RangeEnd:yyyyMMdd}|full={request.FullExcelTotals}|scope={request.KasaScope}";
        if (_poolCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTimeOffset.UtcNow - cached.createdAt <= _poolCacheTtl)
            {
                debugLog.Add($"[Pipeline] Excel cache HIT ({cached.entries.Count} entry)");
                return Result<IReadOnlyList<UnifiedPoolEntry>>.Success(cached.entries);
            }
            _poolCache.TryRemove(cacheKey, out _);
        }

        var poolResult = await _draftService.BuildUnifiedPoolAsync(
            request.RaporTarihi,
            request.UploadFolder,
            null, // finalizeInputs
            request.RangeStart,
            request.RangeEnd,
            fullExcelTotals: request.FullExcelTotals,
            kasaScope: request.KasaScope,
            mesaiSonuModu: false,
            ct);

        if (poolResult.Ok && poolResult.Value is not null)
        {
            _poolCache[cacheKey] = (DateTimeOffset.UtcNow, poolResult.Value);
        }
        
        return poolResult;
    }
    
    private async Task<int> LoadDefaultsAsync(
        CellRegistry cells, 
        List<string> debugLog, 
        CancellationToken ct)
    {
        var defaults = await _defaults.GetAsync(ct);
        if (defaults is null) return 0;
        
        int count = 0;
        
        // Default değerleri Cell olarak ekle
        if (defaults.DefaultBozukPara is decimal bp)
        {
            cells.Set(new Cell
            {
                Key = "default_bozuk_para",
                Value = bp,
                Source = CellSource.Settings,
                DisplayName = "Varsayılan Bozuk Para",
                Category = "Ayarlar"
            });
            count++;
            
            // R23 FIX: Formüllerin bozuk_para kullanması için fallback ekle
            // Eğer bozuk_para yoksa VEYA değeri 0 ise, default değeri kullan
            var existingBozuk = cells.Get("bozuk_para");
            if (existingBozuk is null || existingBozuk.Value == 0m)
            {
                cells.Set(new Cell
                {
                    Key = "bozuk_para",
                    Value = bp,
                    Source = CellSource.Settings,
                    DisplayName = "Bozuk Para (Varsayılan)",
                    Category = "Kullanıcı Girişi"
                });
                count++;
                debugLog.Add($"[Pipeline] bozuk_para fallback: {bp:N2} (mevcut: {existingBozuk?.Value ?? 0m:N2})");
            }
        }
        
        if (defaults.DefaultNakitPara is decimal np)
        {
            cells.Set(new Cell
            {
                Key = "default_nakit_para",
                Value = np,
                Source = CellSource.Settings,
                DisplayName = "Varsayılan Nakit Para",
                Category = "Ayarlar"
            });
            count++;
            
            // R23 FIX: Formüllerin nakit_para kullanması için fallback ekle
            // Eğer nakit_para yoksa VEYA değeri 0 ise, default değeri kullan
            var existingNakit = cells.Get("nakit_para");
            if (existingNakit is null || existingNakit.Value == 0m)
            {
                cells.Set(new Cell
                {
                    Key = "nakit_para",
                    Value = np,
                    Source = CellSource.Settings,
                    DisplayName = "Nakit Para (Varsayılan)",
                    Category = "Kullanıcı Girişi"
                });
                count++;
                debugLog.Add($"[Pipeline] nakit_para fallback: {np:N2} (mevcut: {existingNakit?.Value ?? 0m:N2})");
            }
        }

        // === R16 parity için kritik Ayarlar hücreleri ===
        // NOT: Bu key'ler R16 tarafında UI-only override olarak görünüyordu.
        // R20 pipeline'da da aynı key'ler mutlaka üretilmeli ki:
        // (1) sol menü sayısı birebir olsun,
        // (2) DB'deki FormulaSet hesaplamaları settings değerlerini bulsun.

        void AddSettingsIfMissing(string key, decimal? value, string displayName, string category = "Ayarlar")
        {
            if (value is not decimal v) return;
            if (cells.ContainsKey(key)) return; // mevcut (Excel/Override) varsa ezme
            cells.Set(new Cell
            {
                Key = key,
                Value = v,
                Source = CellSource.Settings,
                DisplayName = displayName,
                Category = category
            });
            count++;
        }

        AddSettingsIfMissing("genel_kasa_devreden_seed", defaults.DefaultGenelKasaDevredenSeed, "Genel Kasa Devreden (Seed)");
        AddSettingsIfMissing("kayden_tahsilat_ayar", defaults.DefaultKaydenTahsilat, "Kayden Tahsilat (Ayar)");
        AddSettingsIfMissing("dunden_devreden_kasa_nakit", defaults.DefaultDundenDevredenKasaNakit, "Dünden Devreden Kasa Nakit");
        
        return count;
    }
    
    private static int LoadUserInputs(CellRegistry cells, UserInputs inputs)
    {
        int count = 0;
        
        void AddIfPresent(string key, decimal? value, string displayName)
        {
            if (value is decimal v)
            {
                cells.Set(new Cell
                {
                    Key = KeyNormalizer.Normalize(key),
                    Value = v,
                    Source = CellSource.UserInput,
                    DisplayName = displayName,
                    Category = "Kullanıcı Girişi"
                });
                count++;
            }
        }
        
        AddIfPresent("bozuk_para", inputs.BozukPara, "Bozuk Para");
        AddIfPresent("nakit_para", inputs.NakitPara, "Nakit Para");
        AddIfPresent("vergiden_gelen", inputs.VergidenGelen, "Vergiden Gelen");
        AddIfPresent("gelmeyen_d", inputs.GelmeyenD, "Gelmeyen D");
        AddIfPresent("kasada_kalacak_hedef", inputs.KasadaKalacakHedef, "Kasada Kalacak Hedef");
        AddIfPresent("kayden_tahsilat", inputs.KaydenTahsilat, "Kayden Tahsilat");
        AddIfPresent("kayden_harc", inputs.KaydenHarc, "Kayden Harç");
        AddIfPresent("bankadan_cekilen", inputs.BankadanCekilen, "Bankadan Çekilen");
        AddIfPresent("cesitli_nedenlerle_bankadan_cikamayan_tahsilat", 
            inputs.CesitliNedenlerleBankadanCikamayanTahsilat, 
            "Çeşitli Nedenlerle Bankadan Çıkamayan Tahsilat");
        AddIfPresent("bankaya_gonderilmis_deger", inputs.BankayaGonderilmisDeger, "Bankaya Gönderilmiş Değer");
        AddIfPresent("bankaya_yatirilacak_harci_degistir", 
            inputs.BankayaYatirilacakHarciDegistir, 
            "Bankaya Yatırılacak Harcı Değiştir");
        AddIfPresent("bankaya_yatirilacak_tahsilati_degistir", 
            inputs.BankayaYatirilacakTahsilatiDegistir, 
            "Bankaya Yatırılacak Tahsilatı Değiştir");
        
        return count;
    }
    
    private async Task<int> LoadCarryoverAsync(
        CellRegistry cells,
        PipelineRequest request,
        List<string> debugLog,
        CancellationToken ct)
    {
        // Önceki günün snapshot'ından taşınan değerler
        var previousDate = request.RaporTarihi.AddDays(-1);
        var snapshot = await _snapshotService.GetLastGenelKasaSnapshotBeforeOrOnAsync(previousDate, ct);
        if (snapshot?.Results is null) return 0;
        
        int count = 0;
        
        // Results.ValuesJson'dan değerleri parse et
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, decimal>>(snapshot.Results.ValuesJson);
            if (values is not null && values.TryGetValue("GenelKasa", out var devreden))
            {
                cells.Set(new Cell
                {
                    Key = "dunden_devreden_kasa",
                    Value = devreden,
                    Source = CellSource.Carryover,
                    DisplayName = "Dünden Devreden Kasa",
                    Category = "Carryover",
                    Notes = $"Tarih: {previousDate:yyyy-MM-dd}"
                });
                count++;
            }
        }
        catch (Exception ex)
        {
            // P1-EXC-01: Carryover JSON parse hatası — dünden devreden değer 0 olacak
            debugLog.Add($"[Carryover] Results.ValuesJson parse edilemedi: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[UnifiedDataPipeline] Carryover parse hatası (önceki gün: {previousDate:yyyy-MM-dd}): {ex.Message}");
        }

        return count;
    }
    
    private static int LoadCatalogDefaults(CellRegistry cells)
    {
        int count = 0;
        
        // FieldCatalog'daki tüm alanları kontrol et
        foreach (var entry in FieldCatalog.All)
        {
            var normalizedKey = KeyNormalizer.Normalize(entry.Key);
            
            // Zaten yüklenmemişse varsayılan değerle ekle
            if (!cells.ContainsKey(normalizedKey))
            {
                cells.Set(new Cell
                {
                    Key = normalizedKey,
                    Value = 0m,
                    Source = CellSource.Catalog,
                    DisplayName = entry.DisplayName,
                    Category = entry.Category
                });
                count++;
            }
        }
        
        return count;
    }
    
    private static Cell MapPoolEntryToCell(UnifiedPoolEntry entry)
    {
        var source = entry.Type switch
        {
            UnifiedPoolValueType.Override => CellSource.UserInput,
            UnifiedPoolValueType.Derived => CellSource.Derived,
            _ => CellSource.Excel
        };
        
        // Value'yu decimal'e parse et
        decimal.TryParse(entry.Value, 
            System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, 
            out var value);
        
        return new Cell
        {
            Key = KeyNormalizer.Normalize(entry.CanonicalKey),
            Value = value,
            Source = source,
            DisplayName = entry.CanonicalKey,
            SourceFile = entry.SourceFile,
            Notes = entry.Notes
        };
    }
}
