namespace KasaManager.Web.Models;

public enum ParityDiffStatus
{
    Same = 0,
    Different = 1,
    MissingInEngine = 2,
    MissingInLegacy = 3,
    NotComparable = 4
}

/// <summary>
/// R4: Parity & DiffMap çıktısı.
/// Legacy Draft (Fields) ile FormulaEngine (Inputs/Overrides/Outputs) arasındaki farkları tek satırda taşır.
/// </summary>
public sealed class ParityDiffItem
{
    public string Scope { get; init; } = ""; // Genel | Sabah | Akşam

    // Normalize edilmiş (canonical) karşılaştırma anahtarı
    public string CanonicalKey { get; init; } = "";

    // UI için ham anahtarlar
    public string? LegacyKey { get; init; }
    public string? EngineKey { get; init; }

    public decimal? LegacyValue { get; init; }
    public decimal? EngineValue { get; init; }

    public decimal? Delta { get; init; }

    public ParityDiffStatus Status { get; init; }
    public string? Note { get; init; }
}
