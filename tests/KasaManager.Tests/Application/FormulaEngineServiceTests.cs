using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using Xunit;

namespace KasaManager.Tests.Application;

/// <summary>
/// FormulaEngineService unit testleri.
/// </summary>
public class FormulaEngineServiceTests
{
    private readonly FormulaEngineService _engine = new();

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
}
