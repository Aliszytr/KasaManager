using System;

namespace KasaManager.Domain.FormulaEngine.Authoring;

public sealed class PersistedFormulaRun
{
    public Guid Id { get; set; }

    public Guid SetId { get; set; }
    public PersistedFormulaSet? Set { get; set; }

    public DateTime RunAtUtc { get; set; }

    public string InputsJson { get; set; } = "{}";
    public string OutputsJson { get; set; } = "{}";
    public string IssuesJson { get; set; } = "[]";
}
