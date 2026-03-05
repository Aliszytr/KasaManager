namespace KasaManager.Application.Orchestration.Dtos;

public class KasaInputCatalogEntry
{
    public string Key { get; set; } = string.Empty;
    public bool IsFromUnifiedPool { get; set; }
    public bool IsVirtual { get; set; }
    public string? ValueText { get; set; }
    public string? Hint { get; set; }
}

public class KasaPreviewMappingRow
{
    public string? RowId { get; set; }
    public string? Mode { get; set; }
    public string? TargetKey { get; set; }
    public string? SourceKey { get; set; }
    public string? Expression { get; set; }
    public bool IsHidden { get; set; }

    // R10: Calculation Results (Output plumbing)
    public string? ResultValue { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}

public class FormulaSetListItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ScopeType { get; set; }
    public bool IsActive { get; set; }
    public string? UpdatedAtUtc { get; set; }
}

public class ParityDiffItem
{
    public string? Scope { get; set; }
    public string? CanonicalKey { get; set; }
    public string? LegacyKey { get; set; }
    public string? EngineKey { get; set; }
    public decimal? LegacyValue { get; set; }
    public decimal? EngineValue { get; set; }
    public decimal? Delta { get; set; }
    public ParityDiffStatus Status { get; set; }
    public string? Note { get; set; }
}

public enum ParityDiffStatus
{
    Same,
    Different,
    MissingInLegacy,
    MissingInEngine,
    NotComparable
}
