namespace KasaManager.Domain.Guards;

public sealed class GuardResult
{
    public string RuleId { get; init; } = string.Empty;
    public GuardSeverity Severity { get; init; } = GuardSeverity.Info;
    public string Message { get; init; } = string.Empty;

    public List<string> RelatedKeys { get; init; } = new();

    /// <summary>
    /// Guard bazı senaryolarda çıktı değerini düzeltmiş olabilir (ör: negatif banka -> 0'a clamp).
    /// </summary>
    public bool MutatedOutput { get; init; }
    public string? MutatedKey { get; init; }
    public decimal? OriginalValue { get; init; }
    public decimal? NewValue { get; init; }
}
