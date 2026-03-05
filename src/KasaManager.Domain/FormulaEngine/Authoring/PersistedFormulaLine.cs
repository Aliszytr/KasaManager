using System;

namespace KasaManager.Domain.FormulaEngine.Authoring;

public sealed class PersistedFormulaLine
{
    public Guid Id { get; set; }

    public Guid SetId { get; set; }
    public PersistedFormulaSet? Set { get; set; }

    public string TargetKey { get; set; } = string.Empty;
    public string Mode { get; set; } = "Map";

    public string? SourceKey { get; set; }
    public string? Expression { get; set; }

    // NOTE: "Order" is a problematic column/property name (SQL keyword + common LINQ method name).
    // We use SortOrder instead for stable migrations and predictable SQL.
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
}
