namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// Bir kasa senaryosu için formül seti.
/// Versiyonlama/fork için temel birim.
/// </summary>
public sealed class FormulaSet
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AppliesToKasa AppliesTo { get; init; } = AppliesToKasa.Any;
    public string Version { get; init; } = "1.0.0";

    public List<FormulaTemplate> Templates { get; init; } = new();
}
