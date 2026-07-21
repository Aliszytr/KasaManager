using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// FormulaEngineService unit testleri.
/// </summary>
public class FormulaEngineServiceTests
{
    private readonly FormulaEngineService _engine = new();

    [Fact]
    public void Run_SelfReferentialFormula_ReturnsCyclicFormulaError()
    {
        var set = new FormulaSet
        {
            Id = "cycle-self", Name = "Self cycle",
            Templates = { new() { Id = "x", TargetKey = "x", Expression = "x + 1", Name = "X", Version = "1" } }
        };

        var result = _engine.Run(new DateOnly(2070, 8, 1), set, Array.Empty<UnifiedPoolEntry>());

        Assert.False(result.Ok);
        Assert.Contains("döngüsel formül", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_BuiltInSabahIdentityOnlySet_DoesNotReportCycle()
    {
        var set = new FormulaSet
        {
            Id = "builtin-sabah-identity-only", Name = "SabahKasaSablonu",
            Templates =
            {
                new()
                {
                    Id = "builtin-sabah-tahsilat",
                    TargetKey = "takip_kasa_etkisi_tahsilat",
                    Expression = "takip_kasa_etkisi_tahsilat",
                    Name = "Built-in Sabah tahsilat identity",
                    Version = "1"
                },
                new()
                {
                    Id = "builtin-sabah-harc",
                    TargetKey = "takip_kasa_etkisi_harc",
                    Expression = "takip_kasa_etkisi_harc",
                    Name = "Built-in Sabah harc identity",
                    Version = "1"
                }
            }
        };

        var result = _engine.Run(new DateOnly(2070, 8, 1), set, Array.Empty<UnifiedPoolEntry>());

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void Run_CrossFormulaCycle_ReturnsCyclicFormulaError()
    {
        var set = new FormulaSet
        {
            Id = "cycle-cross", Name = "Cross cycle",
            Templates =
            {
                new() { Id = "a", TargetKey = "a", Expression = "b + 1", Name = "A", Version = "1" },
                new() { Id = "b", TargetKey = "b", Expression = "a + 1", Name = "B", Version = "1" }
            }
        };

        var result = _engine.Run(new DateOnly(2070, 8, 2), set, Array.Empty<UnifiedPoolEntry>());

        Assert.False(result.Ok);
        Assert.Contains("döngüsel formül", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_ExistingSystemKeyTarget_SkipsLineKeepsPoolInputAndLogsWarning()
    {
        var logger = new Mock<ILogger<FormulaEngineService>>();
        var engine = new FormulaEngineService(logger.Object);
        var set = new FormulaSet
        {
            Id = "legacy-invalid", Name = "Legacy invalid",
            Templates =
            {
                new() { Id = "legacy", TargetKey = "takip_kasa_etkisi_tahsilat", Expression = "1", Name = "Legacy", Version = "1" }
            }
        };
        var pool = new[]
        {
            new UnifiedPoolEntry { CanonicalKey = "takip_kasa_etkisi_tahsilat", Value = "987", IncludeInCalculations = true }
        };

        var result = engine.Run(new DateOnly(2070, 8, 3), set, pool);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(987m, result.Value!.Inputs["takip_kasa_etkisi_tahsilat"]);
        Assert.DoesNotContain("takip_kasa_etkisi_tahsilat", result.Value.Outputs.Keys);
        logger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("FORMULA-SYSTEM-KEY-SKIPPED")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void Run_SystemKeyIdentityLine_SkipsIdentityAndPreservesEquivalentPoolValue()
    {
        var set = new FormulaSet
        {
            Id = "identity", Name = "Identity",
            Templates =
            {
                new() { Id = "identity", TargetKey = "normal_tahsilat", Expression = " ( ( normal_tahsilat ) ) ", Name = "Identity", Version = "1" },
                new() { Id = "consumer", TargetKey = "consumer", Expression = "normal_tahsilat + 1", Name = "Consumer", Version = "1" }
            }
        };
        var pool = new[]
        {
            new UnifiedPoolEntry { CanonicalKey = "normal_tahsilat", Value = "100", IncludeInCalculations = true }
        };

        var result = _engine.Run(new DateOnly(2070, 10, 1), set, pool);

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain("normal_tahsilat", result.Value!.Outputs.Keys);
        Assert.Equal(101m, result.Value.Outputs["consumer"]);
    }

    [Fact]
    public void Run_ExcludedPoolKeyCollision_DoesNotSkipFormulaLine()
    {
        var set = new FormulaSet
        {
            Id = "excluded-pool-key", Name = "Excluded pool key",
            Templates =
            {
                new() { Id = "formula", TargetKey = "x", Expression = "40 + 2", Name = "X", Version = "1" }
            }
        };
        var pool = new[]
        {
            new UnifiedPoolEntry
            {
                CanonicalKey = "x", Value = "999", IncludeInCalculations = false
            }
        };

        var result = _engine.Run(new DateOnly(2070, 8, 5), set, pool);

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain("x", result.Value!.Inputs.Keys);
        Assert.Equal(42m, result.Value.Outputs["x"]);
    }

    [Fact]
    public void Run_BuiltInSeedSets_PreserveLegacyStableOrdering()
    {
        static int LegacyWeight(string? key) => key switch
        {
            "sonraya_devredecek" => 90,
            "beklenen_banka" => 95,
            "mutabakat_farki" => 99,
            _ => 0
        };

        foreach (var set in _engine.GetBuiltInFormulaSets())
        {
            var expected = set.Templates.OrderBy(template => LegacyWeight(template.TargetKey))
                .Select(template => template.TargetKey);
            var result = _engine.Run(new DateOnly(2070, 8, 4), set, Array.Empty<UnifiedPoolEntry>());

            Assert.True(result.Ok, result.Error);
            Assert.Equal(expected, result.Value!.Explain.Select(item => item.TargetKey));
        }
    }

    // ── GetBuiltInFormulaSets ──

    [Fact]
    public void GetBuiltInFormulaSets_Returns_TwoSets()
    {
        var sets = _engine.GetBuiltInFormulaSets();
        Assert.Equal(2, sets.Count);
    }

    [Fact]
    public void GetBuiltInFormulaSets_ContainsV1AndGenelKasa()
    {
        var sets = _engine.GetBuiltInFormulaSets();
        Assert.Contains(sets, s => s.Name.Contains("v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sets, s => s.Name.Contains("Genel Kasa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBuiltInFormulaSets_AllHaveTemplates()
    {
        var sets = _engine.GetBuiltInFormulaSets();
        Assert.All(sets, s => Assert.NotEmpty(s.Templates));
    }

    // ── Run: basic formula ──

    [Fact]
    public void Run_SimpleAddition_ReturnsCorrectOutput()
    {
        var formulaSet = new FormulaSet
        {
            Id = "test-set",
            Name = "Test",
            Version = "1",
            Templates =
            {
                new FormulaTemplate
                {
                    Id = "t1",
                    TargetKey = "result",
                    Expression = "a + b",
                    Name = "Test",
                    Version = "1"
                }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "a", Value = "100", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true },
            new() { CanonicalKey = "b", Value = "200", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var result = _engine.Run(DateOnly.FromDateTime(DateTime.Today), formulaSet, pool);

        Assert.True(result.Ok);
        Assert.Equal(300m, result.Value!.Outputs["result"]);
    }

    [Fact]
    public void Run_OverrideSkipsFormula_UsesOverrideValue()
    {
        var formulaSet = new FormulaSet
        {
            Id = "test-set-2",
            Name = "Test Override",
            Version = "1",
            Templates =
            {
                new FormulaTemplate
                {
                    Id = "t2",
                    TargetKey = "result",
                    Expression = "a + b",
                    Name = "Test",
                    Version = "1"
                }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "a", Value = "100", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true },
            new() { CanonicalKey = "b", Value = "200", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var overrides = new Dictionary<string, decimal> { ["result"] = 999m };

        var result = _engine.Run(DateOnly.FromDateTime(DateTime.Today), formulaSet, pool, overrides);

        Assert.True(result.Ok);
        Assert.Equal(999m, result.Value!.Outputs["result"]);
    }

    [Fact]
    public void Run_NullFormulaSet_ReturnsFail()
    {
        var pool = new List<UnifiedPoolEntry>();
        var result = _engine.Run(DateOnly.FromDateTime(DateTime.Today), null!, pool);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Run_MissingVariable_DefaultsToZero()
    {
        var formulaSet = new FormulaSet
        {
            Id = "test-set-3",
            Name = "Test Missing",
            Version = "1",
            Templates =
            {
                new FormulaTemplate
                {
                    Id = "t3",
                    TargetKey = "result",
                    Expression = "a + nonexistent_var",
                    Name = "Test",
                    Version = "1"
                }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "a", Value = "100", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var result = _engine.Run(DateOnly.FromDateTime(DateTime.Today), formulaSet, pool);

        Assert.True(result.Ok);
        // nonexistent_var defaults to 0, so result = 100 + 0 = 100
        Assert.Equal(100m, result.Value!.Outputs["result"]);
    }

    [Fact]
    public void Run_NullPool_ReturnsFail()
    {
        var set = new FormulaSet
        {
            Id = "test-null-pool",
            Name = "Test",
            Version = "1",
            Templates = new List<FormulaTemplate>()
        };
        var result = _engine.Run(DateOnly.FromDateTime(DateTime.Today), set, null!);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Run_DependencyChain_ResolvesCorrectly()
    {
        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "x", Value = "100", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var set = new FormulaSet
        {
            Id = "test-chain",
            Name = "Test Chain",
            Version = "1",
            Templates = new List<FormulaTemplate>
            {
                new() { Id = "s1", TargetKey = "step1", Expression = "x * 2", Name = "Step1", Version = "1" },
                new() { Id = "s2", TargetKey = "step2", Expression = "step1 + 50", Name = "Step2", Version = "1" }
            }
        };

        var result = _engine.Run(new DateOnly(2026, 1, 1), set, pool);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(200m, result.Value!.Outputs["step1"]); // 100*2
        Assert.Equal(250m, result.Value!.Outputs["step2"]); // 200+50
    }

    [Fact]
    public void Run_EmptyTemplates_ReturnsEmptyOutputs()
    {
        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "a", Value = "10", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var set = new FormulaSet
        {
            Id = "test-empty",
            Name = "Test Empty",
            Version = "1",
            Templates = new List<FormulaTemplate>()
        };

        var result = _engine.Run(new DateOnly(2026, 1, 1), set, pool);
        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Value!.Outputs);
    }

    [Fact]
    public void Run_SabahEksikFazlaFormula_DoesNotDoubleCountCarryover()
    {
        var formulaSet = new FormulaSet
        {
            Id = "sabah-ef-r1",
            Name = "Sabah EF R1",
            Version = "1",
            Templates = new List<FormulaTemplate>
            {
                new() { Id = "ef-1", TargetKey = "tespit_edilen_eksik_fazla", Expression = "0", Name = "Tespit", Version = "1" },
                new() { Id = "ef-2", TargetKey = "gune_ait_eksik_fazla_tahsilat", Expression = "tespit_edilen_eksik_fazla", Name = "Gune Ait", Version = "1" }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "dunden_eksik_fazla_gelen_tahsilat", Value = "7950", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true },
            new() { CanonicalKey = "tespit_edilen_eksik_fazla", Value = "0", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var result = _engine.Run(new DateOnly(2026, 5, 6), formulaSet, pool);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0m, result.Value!.Inputs["tespit_edilen_eksik_fazla"]);
        Assert.DoesNotContain("tespit_edilen_eksik_fazla", result.Value.Outputs.Keys);
        Assert.Equal(0m, result.Value!.Outputs["gune_ait_eksik_fazla_tahsilat"]);
        Assert.NotEqual(15900m, result.Value!.Outputs["gune_ait_eksik_fazla_tahsilat"]);
    }
    [Fact]
    public void Run_SabahTakipKasaEtkisiNet_SubtractsHarc()
    {
        var formulaSet = new FormulaSet
        {
            Id = "sabah-gateway-net",
            Name = "Sabah Gateway Net",
            Version = "1",
            Templates = new List<FormulaTemplate>
            {
                new() { Id = "gw-1", TargetKey = "takip_kasa_etkisi_net", Expression = "takip_kasa_etkisi_tahsilat - takip_kasa_etkisi_harc", Name = "Gateway Net", Version = "1" }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "takip_kasa_etkisi_tahsilat", Value = "8836", Type = UnifiedPoolValueType.Derived, IncludeInCalculations = true },
            new() { CanonicalKey = "takip_kasa_etkisi_harc", Value = "250", Type = UnifiedPoolValueType.Derived, IncludeInCalculations = true }
        };

        var result = _engine.Run(new DateOnly(2026, 5, 6), formulaSet, pool);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(8586m, result.Value!.Outputs["takip_kasa_etkisi_net"]);
    }

    [Fact]
    public void Run_SabahGenelKasaFormula_AddsGatewayNetOnly()
    {
        var formulaSet = new FormulaSet
        {
            Id = "sabah-gateway-genel-kasa",
            Name = "Sabah Gateway Genel Kasa",
            Version = "1",
            Templates = new List<FormulaTemplate>
            {
                new() { Id = "gw-1", TargetKey = "takip_kasa_etkisi_net", Expression = "takip_kasa_etkisi_tahsilat - takip_kasa_etkisi_harc", Name = "Gateway Net", Version = "1" },
                new() { Id = "gk-1", TargetKey = "genel_kasa", Expression = "base_genel_kasa + takip_kasa_etkisi_net", Name = "Genel Kasa", Version = "1" }
            }
        };

        var pool = new List<UnifiedPoolEntry>
        {
            new() { CanonicalKey = "base_genel_kasa", Value = "1164", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true },
            new() { CanonicalKey = "takip_kasa_etkisi_tahsilat", Value = "8836", Type = UnifiedPoolValueType.Derived, IncludeInCalculations = true },
            new() { CanonicalKey = "takip_kasa_etkisi_harc", Value = "0", Type = UnifiedPoolValueType.Derived, IncludeInCalculations = true },
            new() { CanonicalKey = "dunden_eksik_fazla_gelen_tahsilat", Value = "7950", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true },
            new() { CanonicalKey = "dunden_eksik_fazla_gelen_harc", Value = "5444", Type = UnifiedPoolValueType.Raw, IncludeInCalculations = true }
        };

        var result = _engine.Run(new DateOnly(2026, 5, 6), formulaSet, pool);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(8836m, result.Value!.Outputs["takip_kasa_etkisi_net"]);
        Assert.Equal(10000m, result.Value!.Outputs["genel_kasa"]);
    }
}
