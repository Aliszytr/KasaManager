using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Orchestration.Helpers;
using KasaManager.Application.Pipeline;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Application.Orchestration;

// Helpers: Hydration, builders, embedded templates, stubs
public partial class KasaOrchestrator
{
    // =========================================================================
    // Embedded Fallback & Formula Injection
    // =========================================================================

    /// <summary>
    /// Embedded fallback: DB'de şablon yoksa veya DB'ye erişilemezse
    /// kaynak kodda tanımlı varsayılan formülleri yükler.
    /// </summary>
    private void LoadEmbeddedFallbackTemplate(KasaPreviewDto dto, string scopeType)
    {
        var fallback = GetEmbeddedDefaultFormulas(scopeType);
        if (fallback.Count == 0)
        {
            dto.Warnings.Add($"'{scopeType}' için embedded fallback şablonu bulunamadı.");
            return;
        }

        dto.DbFormulaSetName = $"{scopeType}Kasa (Embedded Fallback)";
        dto.DbScopeType = scopeType;
        dto.DbFormulaSetId = null;

        dto.Mappings = fallback.Select((f, i) => new KasaPreviewMappingRow
        {
            RowId = $"fallback-{i}",
            TargetKey = f.Target,
            Mode = "Formula",
            Expression = f.Expression,
            IsHidden = false
        }).ToList();

        dto.Warnings.Add($"Intent-First: '{scopeType}' embedded fallback şablonu yüklendi.");
    }

    /// <summary>
    /// Genel scope DB formula setine temel mutabakat formüllerini ekler — eğer eksikse.
    /// GenelKasaR10 ile uyumlu: sonraya_devredecek → beklenen_banka → mutabakat_farki zinciri.
    /// </summary>
    private static void InjectEssentialGenelFormulas(KasaPreviewDto dto)
    {
        // Gerçek pool canonical key'lerini kullan (GenelKasaR10 abstract key'leri yerine)
        // Pool key'leri: genel_kasa_devreden_seed, masraf, masraf_reddiyat, kasa_eksik_fazla,
        //                banka_yarina_devredecek_tahsilat, gelmeyen_d, banka_bakiye
        var essentials = new[]
        {
            // sonraya_devredecek = devreden + (masraf - masraf_reddiyat) - gelmeyen_d
            (Target: "sonraya_devredecek", Expression: "genel_kasa_devreden_seed + masraf - masraf_reddiyat - gelmeyen_d"),
            // beklenen_banka = sonraya_devredecek + eksik/fazla
            (Target: "beklenen_banka",     Expression: "sonraya_devredecek + kasa_eksik_fazla"),
            // mutabakat_farki = banka_bakiye - beklenen_banka
            (Target: "mutabakat_farki",    Expression: "banka_yarina_devredecek_tahsilat - beklenen_banka"),
        };

        dto.Mappings ??= new List<KasaPreviewMappingRow>();
        var existingKeys = new HashSet<string>(
            dto.Mappings.Select(m => m.TargetKey ?? ""),
            StringComparer.OrdinalIgnoreCase);

        var injectedCount = 0;
        foreach (var (target, expression) in essentials)
        {
            if (existingKeys.Contains(target)) continue;

            dto.Mappings.Add(new KasaPreviewMappingRow
            {
                RowId = $"essential-{target}",
                TargetKey = target,
                Mode = "Formula",
                Expression = expression,
                IsHidden = false  // Görünür olsun — formül motorunda çalışması şart
            });
            injectedCount++;
        }

        if (injectedCount > 0)
            dto.Warnings.Add($"Genel scope: {injectedCount} temel mutabakat formülü otomatik eklendi.");
    }

    /// <summary>
    /// Kaynak kodda saklanan varsayılan formül tanımları.
    /// FormulaTemplateSeeder ile aynı veriler — DB bağımsız çalışabilmek için.
    /// </summary>
    private static List<(string Target, string Expression)> GetEmbeddedDefaultFormulas(string scopeType)
    {
        return KasaScopeTypes.Normalize(scopeType).ToLowerInvariant() switch
        {
            "aksam" => new()
            {
                ("genel_kasa", "banka_tahsilat + online_tahsilat + nakit_para + bozuk_para - banka_harc"),
                ("tahsil_red_fark", "kayden_tahsilat - banka_tahsilat"),
                ("harc_red_fark", "kayden_harc - banka_harc"),
                ("muhasebe_fark_tahsilat", "bankaya_giren_tahsilat - bankaya_yatirilacak_tahsilat - online_tahsilat - eft_otomatik_iade - gelen_havale - iade_kelimesi_giris - dunden_eksik_fazla_gelen_tahsilat"),
                ("muhasebe_fark_harc", "bankaya_giren_harc - bankaya_yatirilacak_harc - online_harc - dunden_eksik_fazla_gelen_harc"),
                ("normal_stopaj", "Max(0, toplam_stopaj - online_stopaj)"),
                ("bankaya_yatirilacak_harc", "banka_harc + bankaya_yatirilacak_harci_degistir"),
                ("bankaya_yatirilacak_tahsilat", "banka_tahsilat + bankaya_yatirilacak_tahsilati_degistir"),
                ("bankaya_yatirilacak_toplam", "bankaya_yatirilacak_tahsilat + bankaya_yatirilacak_harc + normal_stopaj"),
                ("stopaj_kontrol", "online_reddiyat - bankadan_cikan_tahsilat - toplam_stopaj"),
                ("kasa_toplam", "nakit_para + bozuk_para + vergiden_gelen")
            },
            "sabah" => new()
            {
                ("sabah_kasa_devir", "dunden_devreden_kasa_nakit + dunden_eksik_veya_fazla_tahsilat"),
                ("sabah_toplam", "sabah_kasa_devir + banka_tahsilat + online_tahsilat"),
                ("gune_ait_tahsilat_farki", "kayden_tahsilat - banka_tahsilat"),
                ("dunden_eksik_veya_fazla_tahsilat", "0"),
                ("gune_ait_eksik_veya_fazla_tahsilat", "0")
            },
            "genel" => new()
            {
                ("kasa_nakit", "bozuk_para + nakit_para"),
                ("toplam_tahsilat", "banka_cekilen_tahsilat + online_tahsilat + vergiden_gelen + gelmeyen_d"),
                ("toplam_harc", "banka_cekilen_harc + diger_harclar + masraf + masraf_reddiyat"),
                ("genel_kasa_toplam", "genel_kasa_devreden_seed + toplam_tahsilat - toplam_harc + kasa_nakit"),
                ("genel_kasa_devir", "genel_kasa_toplam - bankaya_yatirilacak_tahsilat"),
                ("banka_tahsilat", "banka_cekilen_tahsilat + online_tahsilat"),
                ("bankaya_yatirilacak_tahsilat", "banka_tahsilat + bankaya_yatirilacak_tahsilati_degistir"),
                ("banka_harc", "banka_cekilen_harc"),
                ("banka_yarina_devredecek_tahsilat", "genel_kasa_devir"),
                ("tahsil_red_fark", "kayden_tahsilat - banka_tahsilat"),
                ("harc_red_fark", "kayden_harc - banka_harc"),
                ("kasa_eksik_fazla", "beklenen_kasa - gercek_kasa"),
                // Temel mutabakat formülleri (gerçek pool key'leri ile)
                ("sonraya_devredecek", "genel_kasa_devreden_seed + masraf - masraf_reddiyat - gelmeyen_d"),
                ("beklenen_banka", "sonraya_devredecek + kasa_eksik_fazla"),
                ("mutabakat_farki", "banka_yarina_devredecek_tahsilat - beklenen_banka")
            },
            "ortak" => new()
            {
                ("net_tahsilat", "banka_tahsilat - bankadan_iade"),
                ("net_harc", "banka_harc - harc_iade"),
                ("kasa_farki", "beklenen_kasa - gercek_kasa")
            },
            _ => new()
        };
    }

    // =========================================================================
    // Hydration & Snapshot
    // =========================================================================

    private async Task<bool> HydrateFromSnapshotAndDefaultsInternalAsync(KasaPreviewDto dto, DateOnly date, CancellationToken ct)
    {
        var snap = await _snapshots.GetAsync(date, KasaRaporTuru.Genel, ct);
        if (snap?.Rows is null || snap.Rows.Count == 0)
        {
            dto.HasSnapshot = false;
            dto.IsDataLoaded = false;
            dto.Errors.Add($"{date:dd.MM.yyyy} için Genel snapshot bulunamadı.");
            return false;
        }

        dto.HasSnapshot = true;
        dto.VeznedarOptions = snap.Rows.Where(r => !r.IsSummaryRow && !string.IsNullOrWhiteSpace(r.Veznedar))
            .Select(r => r.Veznedar!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();

        dto.VergiKasaVeznedarlar = snap.Rows.Where(r => !r.IsSummaryRow && r.IsSelected && !string.IsNullOrWhiteSpace(r.Veznedar))
            .Select(r => r.Veznedar!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
            
        dto.VergiKasaBakiyeToplam = snap.Rows.Where(r => !r.IsSummaryRow && r.IsSelected).Sum(r => r.Bakiye ?? 0m);

        // DRY: Tek kaynak üzerinden varsayılan hydration
        var defaults = await _globalDefaults.GetAsync(ct);
        KasaDefaultsHydrator.Apply(dto, defaults);
        
        if (string.IsNullOrWhiteSpace(dto.KasayiYapan) && dto.VeznedarOptions.Count == 1)
            dto.KasayiYapan = dto.VeznedarOptions[0];

        return true;
    }

    private async Task HydrateGenelKasaDateRangeAsync(KasaPreviewDto dto, DateOnly date, CancellationToken ct)
    {
        // Kullanıcı zaten tarih girdiyse (UI'dan), o tarihleri koru — üzerine yazma
        if (dto.GenelKasaStartDate.HasValue && dto.GenelKasaEndDate.HasValue)
        {
            dto.GenelKasaStartDateSource ??= "Kullanıcı Girişi";
            dto.GenelKasaEndDateSource ??= "Kullanıcı Girişi";
            return;
        }

        if (dto.DefaultGenelKasaBaslangicTarihiSeed.HasValue)
        {
            var seed = dto.DefaultGenelKasaBaslangicTarihiSeed.Value;
            if (date >= seed)
            {
                dto.GenelKasaStartDate ??= seed;
                dto.GenelKasaEndDate ??= date;
                dto.GenelKasaStartDateSource ??= "Ayarlar (Seed)";
                dto.GenelKasaEndDateSource ??= "Rapor Tarihi";
                return;
            }
        }
        
        dto.GenelKasaStartDate ??= date;
        dto.GenelKasaEndDate ??= date;
        dto.GenelKasaStartDateSource ??= "Ayar Bulunamadı (Günlük)";
    }

    // =========================================================================
    // Builders & Utility
    // =========================================================================

    private KasaDraftFinalizeInputs BuildFinalizeInputs(KasaPreviewDto dto) => new()
    {
        KasayiYapan = dto.KasayiYapan,
        Aciklama = dto.Aciklama,
        BozukPara = dto.BozukPara,
        NakitPara = dto.NakitPara,
        VergiKasaBakiyeToplam = dto.VergiKasaBakiyeToplam,
        VergidenGelen = dto.VergidenGelen,
        GelmeyenD = dto.GelmeyenD,
        KasadaKalacakHedef = dto.KasadaKalacakHedef,
        KaydenTahsilat = dto.KaydenTahsilat,
        KaydenHarc = dto.KaydenHarc,
        BankadanCekilen = dto.BankadanCekilen,
        CesitliNedenlerleBankadanCikamayanTahsilat = dto.CesitliNedenlerleBankadanCikamayanTahsilat,
        BankayaGonderilmisDeger = dto.BankayaGonderilmisDeger,
        BankayaYatirilacakHarciDegistir = dto.BankayaYatirilacakHarciDegistir,
        BankayaYatirilacakTahsilatiDegistir = dto.BankayaYatirilacakTahsilatiDegistir,
        GuneAitEksikFazlaTahsilat = dto.GuneAitEksikFazlaTahsilat,
        GuneAitEksikFazlaHarc = dto.GuneAitEksikFazlaHarc,
        DundenEksikFazlaTahsilat = dto.DundenEksikFazlaTahsilat,
        DundenEksikFazlaHarc = dto.DundenEksikFazlaHarc,
        DundenEksikFazlaGelenTahsilat = dto.DundenEksikFazlaGelenTahsilat,
        DundenEksikFazlaGelenHarc = dto.DundenEksikFazlaGelenHarc
    };

    private List<KasaInputCatalogEntry> BuildInputCatalog(List<UnifiedPoolEntry> pool)
    {
        var catalog = new List<KasaInputCatalogEntry>();
        foreach(var p in pool.OrderBy(x=>x.CanonicalKey))
        {
             if(string.IsNullOrWhiteSpace(p.CanonicalKey)) continue;
             catalog.Add(new KasaInputCatalogEntry {
                 Key = p.CanonicalKey,
                 IsFromUnifiedPool = true,
                 ValueText = p.Value,
                 Hint = !string.IsNullOrWhiteSpace(p.SourceName) ? p.SourceName : p.Type.ToString()
             });
        }
        return catalog;
    }

    private void AppendUiOnlyOverridePoolEntries(KasaPreviewDto dto)
    {
         if(dto.PoolEntries == null) return;
         var existing = new HashSet<string>(dto.PoolEntries.Select(x=>x.CanonicalKey).Where(x=>x!=null), StringComparer.OrdinalIgnoreCase);
         
         void Add(string key, decimal? val, string note) {
             if(val == null || existing.Contains(key)) return;
             dto.PoolEntries.Add(new UnifiedPoolEntry {
                 CanonicalKey = key,
                 Value = val.Value.ToString("N2", CultureInfo.InvariantCulture),
                 Type = UnifiedPoolValueType.Override,
                 IncludeInCalculations = true,
                 SourceName = "UI/Settings",
                 SourceDetails = note,
                 Notes = note
             });
             existing.Add(key);
         }
         
         Add("genel_kasa_devreden_seed", dto.DefaultGenelKasaDevredenSeed, "Ayarlar");
         Add("kayden_tahsilat_ayar", dto.DefaultKaydenTahsilat, "Ayarlar");
         Add("vergiden_gelen", dto.VergidenGelen, "Manual");
         Add("gelmeyen_d", dto.GelmeyenD, "Manual");
         Add("bankaya_yatirilacak_tahsilati_degistir", dto.BankayaYatirilacakTahsilatiDegistir, "Manual");
         // BUG-6 FIX: Aksam embedded formülü kullanıyor — pool'da yoksa formül patlar
         Add("bankaya_yatirilacak_harci_degistir", dto.BankayaYatirilacakHarciDegistir, "Manual");
    }

    private void EnsureSelectedKeysInPool(KasaPreviewDto dto)
    {
        if(dto.PoolEntries == null) return;
        var existing = new HashSet<string>(dto.PoolEntries.Select(x=>x.CanonicalKey).Where(x=>x!=null), StringComparer.OrdinalIgnoreCase);
        foreach(var k in dto.SelectedInputKeys)
        {
            if(!existing.Contains(k))
            {
                dto.PoolEntries.Add(new UnifiedPoolEntry {
                    CanonicalKey = k,
                    Value = "0,00",
                    Type = UnifiedPoolValueType.Raw, 
                    IncludeInCalculations = true,
                    SourceName = "Explicit Selection",
                    Notes = "Added to pool because selected"
                });
                existing.Add(k);
            }
        }
    }

    private FormulaSet BuildUiFormulaSetFromMappings(KasaPreviewDto dto)
    {
        return new FormulaSet {
            Id = "ui",
            Name = "UI Set",
            Templates = (dto.Mappings ?? new List<KasaPreviewMappingRow>())
                .Where(x => !x.IsHidden)
                .Select((x,i) => {
                    var targetKey = x.TargetKey ?? string.Empty;
                    string expression;
                    if (x.Mode == "Map")
                    {
                        // Map mode: SourceKey varsa onu kullan, yoksa TargetKey'i identity olarak kullan.
                        // Bu, pool'daki değerin FormulaEngine output'una kopyalanmasını sağlar.
                        // Örnek: online_reddiyat (Map, SourceKey=NULL) → expression = "online_reddiyat"
                        // FormulaEngine bunu pool input'undan çözer ve output'a yazar.
                        expression = !string.IsNullOrWhiteSpace(x.SourceKey) ? x.SourceKey : targetKey;
                    }
                    else
                    {
                        expression = x.Expression ?? string.Empty;
                    }
                    return new { Id = $"ui.t{i}", TargetKey = targetKey, Expression = expression };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Expression))
                .Select(x => new FormulaTemplate {
                    Id = x.Id,
                    TargetKey = x.TargetKey,
                    Expression = x.Expression
                }).ToList()
        };
    }

    /// <summary>
    /// Kullanıcının manuel override girişlerini FormulaEngine'e iletir.
    /// Şu an sadece bankaya_yatirilacak_tahsilati_degistir desteklenmektedir.
    /// </summary>
    private Dictionary<string, decimal> BuildOverridesForFormulaEngine(KasaPreviewDto dto)
    {
        var d = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if(dto.BankayaYatirilacakTahsilatiDegistir.HasValue) d["bankaya_yatirilacak_tahsilati_degistir"] = dto.BankayaYatirilacakTahsilatiDegistir.Value;
        return d;
    }

    /// <summary>
    /// Seçilen alanların pool'da mevcut olup olmadığını doğrular.
    /// Gelecekte genişletilecek — şu an her zaman geçerli döndürür.
    /// </summary>
    private (bool ok, string? error) ValidateSelection(KasaPreviewDto dto, IReadOnlyList<UnifiedPoolEntry> pool, FormulaSet set)
    {
        return (true, null);
    }
    
    /// <summary>
    /// Draft bundle ile FormulaEngine sonuçları arasındaki farkları hesaplar.
    /// Gelecekte implemente edilecek — şu an boş liste döndürür.
    /// </summary>
    private List<ParityDiffItem> BuildParityDiffs(KasaDraftBundle bundle, CalculationRun run)
    {
         return new List<ParityDiffItem>();
    }

}
