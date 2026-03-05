using System;
using System.Collections.Generic;

namespace KasaManager.Domain.FormulaEngine.Authoring;

public sealed class PersistedFormulaSet
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "Custom";
    public bool IsActive { get; set; }
    public string SelectedInputsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<PersistedFormulaLine> Lines { get; set; } = new();
    public List<PersistedFormulaRun> Runs { get; set; } = new();
}
