namespace KasaManager.Web.Models;

public sealed class FormulaSetListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "Custom";
    public bool IsActive { get; set; }
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
