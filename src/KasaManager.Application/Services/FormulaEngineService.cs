using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Guards;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Constants;
using System.Globalization;
using NCalc;

namespace KasaManager.Application.Services;

/// <summary>
/// R16: NCalc tabanlı gelişmiş formül motoru.
/// - max(), min(), if() gibi fonksiyonları destekler.
/// - UnifiedPool verilerini (inputs/overrides) dinamik olarak çözer.
/// - Guard + Explain üretir.
/// </summary>
public sealed class FormulaEngineService : IFormulaEngineService
{
    public const string BuiltInFormulaSetV1Id = BuiltInFormulaSetIds.FormulaSetV1;
    public const string BuiltInGenelKasaR10Id = BuiltInFormulaSetIds.GenelKasaR10;

    private static readonly CultureInfo _invariant = CultureInfo.InvariantCulture;

    // Formula authoring compatibility:
    // - We keep UnifiedPool keys as single-source canonical.
    // - Old/legacy aliases used in stored formulas are normalized at runtime.
    private static readonly Dictionary<string, string> _formulaAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b_cekilen"] = KasaCanonicalKeys.BankadanCekilen,
    };

    private static string NormalizeIdentifier(string id)
        => _formulaAliases.TryGetValue(id, out var canonical) ? canonical : id;


    public IReadOnlyList<FormulaSet> GetBuiltInFormulaSets()
    {
        return
        [
            BuildBuiltInFormulaSetV1(),
            BuildBuiltInGenelKasaR10()
        ];
    }

    private static FormulaSet BuildBuiltInFormulaSetV1()
    {
        return new FormulaSet
        {
            Id = BuiltInFormulaSetV1Id,
            Name = "Built-in FormulaSet v1 (Excel Temel)",
            AppliesTo = AppliesToKasa.Any,
            Version = "1.0.0",
            Templates =
            {
                new FormulaTemplate
                {
                    Id = "v1.normal_reddiyat",
                    Name = "Normal Reddiyat (Toplam - Online)",
                    TargetKey = "normal_reddiyat",
                    Expression = "toplam_reddiyat - online_reddiyat",
                    Category = FormulaCategory.Reddiyat,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "v1.normal_stopaj",
                    Name = "Normal Stopaj (max(0, Toplam - Online))",
                    TargetKey = "normal_stopaj",
                    Expression = "Max(0, toplam_stopaj - online_stopaj)",
                    Category = FormulaCategory.Stopaj,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "v1.bankaya_tahsilat",
                    Name = "Bankaya Yatırılacak Tahsilat",
                    TargetKey = "bankaya_yatirilacak_tahsilat",
                    Expression = "(toplam_tahsilat - online_tahsilat) + bankaya_yatirilacak_tahsilati_degistir",
                    Category = FormulaCategory.Tahsilat,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "v1.bankaya_harc",
                    Name = "Bankaya Yatırılacak Harç",
                    TargetKey = "bankaya_yatirilacak_harc",
                    Expression = "toplam_harc + bankaya_yatirilacak_harci_degistir",
                    Category = FormulaCategory.Harc,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "v1.bankaya_toplam",
                    Name = "Bankaya Yatırılacak Toplam",
                    TargetKey = "bankaya_yatirilacak_toplam",
                    Expression = "bankaya_yatirilacak_tahsilat + bankaya_yatirilacak_harc + normal_stopaj",
                    Category = FormulaCategory.Banka,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                },
                // Örnek "yeni attribute": modelde yoksa bile output üretilebilir.
                new FormulaTemplate
                {
                    Id = "v1.genel_ornek",
                    Name = "Genel Kasa (örnek) - çıktı anahtarı üretimi",
                    TargetKey = "genel_kasa_ornek",
                    Expression = "bankaya_yatirilacak_toplam - bozuk_para",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Any,
                    Version = "1.0.0"
                }
            }
        };
    }

    /// <summary>
    /// FAZ-2 / Adım-1: GenelKasaRapor ekranı "UI-only" olacak.
    /// Bu nedenle Genel Kasa R10 kartının tüm hesapları FormulaEngine hattından üretilir.
    /// </summary>
    private static FormulaSet BuildBuiltInGenelKasaR10()
    {
        return new FormulaSet
        {
            Id = BuiltInGenelKasaR10Id,
            Name = "Built-in Genel Kasa R10",
            AppliesTo = AppliesToKasa.Genel,
            Version = "1.0.0",
            Templates =
            {
                new FormulaTemplate
                {
                    Id = "genelkasa_r10.tah_red_fark",
                    Name = "Tah.Red Fark",
                    TargetKey = KasaCanonicalKeys.TahRedFark,
                    Expression = $"{KasaCanonicalKeys.ToplamTahsilat} - {KasaCanonicalKeys.ToplamReddiyat}",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Genel,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "genelkasa_r10.sonraya_devredecek",
                    Name = "Sn.Dn.Devredecek",
                    TargetKey = KasaCanonicalKeys.SonrayaDevredecek,
                    Expression = $"{KasaCanonicalKeys.Devreden} + {KasaCanonicalKeys.TahRedFark} - {KasaCanonicalKeys.GelmeyenD}",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Genel,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "genelkasa_r10.beklenen_banka",
                    Name = "Beklenen Banka",
                    TargetKey = KasaCanonicalKeys.BeklenenBanka,
                    Expression = $"{KasaCanonicalKeys.SonrayaDevredecek} + {KasaCanonicalKeys.EksikFazla}",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Genel,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "genelkasa_r10.mutabakat_farki",
                    Name = "Mutabakat Farkı",
                    TargetKey = KasaCanonicalKeys.MutabakatFarki,
                    Expression = $"{KasaCanonicalKeys.BankaBakiye} - {KasaCanonicalKeys.BeklenenBanka}",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Genel,
                    Version = "1.0.0"
                },
                new FormulaTemplate
                {
                    Id = "genelkasa_r10.genel_kasa",
                    Name = "Genel Kasa",
                    TargetKey = KasaCanonicalKeys.GenelKasa,
                    // DÜZELTME: Doğru formül
                    Expression = $"{KasaCanonicalKeys.Devreden} + {KasaCanonicalKeys.EksikFazla} + {KasaCanonicalKeys.TahRedFark} - {KasaCanonicalKeys.BankaBakiye} - {KasaCanonicalKeys.KasaNakit} - {KasaCanonicalKeys.GelmeyenD}",
                    Category = FormulaCategory.Genel,
                    AppliesTo = AppliesToKasa.Genel,
                    Version = "1.0.0"
                },
            }
        };
    }

    public Result<CalculationRun> Run(
        DateOnly reportDate,
        FormulaSet formulaSet,
        IReadOnlyList<UnifiedPoolEntry> poolEntries,
        IReadOnlyDictionary<string, decimal>? overrides = null)
    {
        if (formulaSet is null)
            return Result<CalculationRun>.Fail("FormulaSet null.");
        if (poolEntries is null)
            return Result<CalculationRun>.Fail("UnifiedPool null.");

        // 1. Context Hazırlığı (Inputs + Overrides)
        var inputMap = BuildDecimalMap(poolEntries);
        var overrideMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // UnifiedPool içindeki override'lar
        foreach (var e in poolEntries)
        {
            if (e.Type == UnifiedPoolValueType.Override && e.IncludeInCalculations)
            {
                if (TryParseDecimal(e.Value, out var dv))
                    overrideMap[e.CanonicalKey] = dv;
            }
        }

        // Explicit overrides (Controller'den gelen)
        if (overrides is not null)
        {
            foreach (var kv in overrides)
                overrideMap[kv.Key] = kv.Value;
        }

        var usedVariables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        // "usedVariables" is filled during evaluation callback.

        // 2. CalculationRun Başlat
        var run = new CalculationRun
        {
            ReportDate = reportDate,
            FormulaSetId = formulaSet.Id,
            FormulaSetVersion = formulaSet.Version,
            Inputs = new Dictionary<string, decimal>(inputMap, StringComparer.OrdinalIgnoreCase),
            Overrides = new Dictionary<string, decimal>(overrideMap, StringComparer.OrdinalIgnoreCase)
        };

        // 3. Dependency Ordering
        var ordered = OrderByDependencies(formulaSet.Templates);

        // 4. Evaluator (NCalc) Loop
        // Context: Inputs (read-only) + Overrides (read-only) + Outputs (write-then-read)
        var outputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in ordered)
        {
            if (string.IsNullOrWhiteSpace(t.TargetKey)) continue;

            // R9/R16 Kuralı: Eğer bir alan Override edilmişse, formül çalışmaz, override değeri basılır.
            if (overrideMap.TryGetValue(t.TargetKey, out var forced))
            {
                outputs[t.TargetKey] = forced;
                run.Outputs[t.TargetKey] = forced;
                run.Explain.Add(new CalculationExplainItem
                {
                    TargetKey = t.TargetKey,
                    Expression = "(override)",
                    ResolvedVariables = new Dictionary<string, decimal> { { t.TargetKey, forced } },
                    Result = forced
                });
                continue;
            }

            // NCalc Expression
            try
            {
                var expr = new Expression(t.Expression);
                
                // Parametre Çözümleyici (EvaluateFunction / EvaluateParameter)
                expr.EvaluateParameter += (name, args) =>
                {
                    var key = NormalizeIdentifier(name);
                    
                    // Öncelik: Outputs > Overrides > Inputs
                    if (outputs.TryGetValue(key, out var val)) args.Result = val;
                    else if (overrideMap.TryGetValue(key, out val)) args.Result = val;
                    else if (inputMap.TryGetValue(key, out val)) args.Result = val;
                    else args.Result = 0m; // Bulunamayan değişken 0 kabul edilir (Excel davranışı)

                    usedVariables[key] = (decimal)args.Result; 
                };

                // Fonksiyon Desteği (Max, Min, If, vb. NCalc default'unda var ama culture sorunu olabilir)
                // Özel fonksiyonlar gerekirse EvaluateFunction ile eklenir.

                var rawResult = expr.Evaluate();
                var resultDecimal = 0m;
                
                if (rawResult is decimal d) resultDecimal = d;
                else if (rawResult is double dbl) resultDecimal = (decimal)dbl;
                else if (rawResult is float f) resultDecimal = (decimal)f;
                else if (rawResult is int i) resultDecimal = (decimal)i;
                else if (rawResult is long l) resultDecimal = (decimal)l;

                // P1-FIN-01: IEEE 754 precision kaybını finansal yuvarlama ile düzelt.
                // NCalc çoğu aritmetikte double döner; (decimal)dbl cast'i
                // kuruş altı artıklar bırakabilir (ör: 300.00000000000003).
                // FinancialMath.Round: 2 basamak, MidpointRounding.AwayFromZero.
                resultDecimal = Domain.Helpers.FinancialMath.Round(resultDecimal);
                outputs[t.TargetKey] = resultDecimal;
                run.Outputs[t.TargetKey] = resultDecimal;
                
                // Explain için, bu adımda kullanılan değişkenleri kopyala
                // (Basit implementasyon: tüm context'i değil, sadece bu adımda kullanılanları yakalamak zor
                // NCalc event-driven olduğu için, yukarıdaki event'te yakalanabilir ama thread-safe değil.
                // Şimdilik "Sonuç" ve "Formül" yeterli, değişken dökümü için tüm inputMap verilebilir.)
                
                run.Explain.Add(new CalculationExplainItem
                {
                    TargetKey = t.TargetKey,
                    Expression = t.Expression,
                    ResolvedVariables = new Dictionary<string, decimal>(inputMap), // İyileştirilebilir
                    Result = resultDecimal
                });

            }
            catch (Exception ex)
            {
                return Result<CalculationRun>.Fail($"Formül Hatası ({t.TargetKey}): {ex.Message}");
            }
        }

        // Canonical aliases & Guards
        EnsureCanonicalAliases(run);
        ApplyGuards(run);

        return Result<CalculationRun>.Success(run);
    }
   
    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private static Dictionary<string, decimal> BuildDecimalMap(IReadOnlyList<UnifiedPoolEntry> pool)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in pool)
        {
            if (string.IsNullOrWhiteSpace(e.CanonicalKey)) continue;
            if (!e.IncludeInCalculations) continue;
            if (TryParseDecimal(e.Value, out var d))
                map[e.CanonicalKey] = d;
        }
        return map;
    }

    private static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim().Replace("\u00A0", " ").Replace("₺", "").Replace("TL", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 1. Invariant (NCalc genelde nokta kullanır)
        if (decimal.TryParse(s, NumberStyles.Any, _invariant, out value)) return true;

        // 2. TR Culture
        if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("tr-TR"), out value)) return true;

        return false;
    }

    private static IReadOnlyList<FormulaTemplate> OrderByDependencies(IReadOnlyList<FormulaTemplate> templates)
    {
        // Basit topolojik sıralama: bağımlılık ağırlıkları ile.
        // sonraya_devredecek → beklenen_banka → mutabakat_farki zinciri doğru sırada hesaplanmalı.
        static int GetWeight(string? key) => key switch
        {
            "sonraya_devredecek" => 90,
            "beklenen_banka" => 95,
            "mutabakat_farki" => 99,
            _ => 0
        };

        return templates.OrderBy(x => GetWeight(x.TargetKey)).ToList(); 
    }

    private static void EnsureCanonicalAliases(CalculationRun run) { }
    private static void ApplyGuards(CalculationRun run) { }
}
