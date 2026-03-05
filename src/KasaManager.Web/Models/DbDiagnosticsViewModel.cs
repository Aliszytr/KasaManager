namespace KasaManager.Web.Models;

public sealed class DbDiagnosticsViewModel
{
    public string? DataSource { get; set; }
    public string? Database { get; set; }
    public string? ProviderName { get; set; }

    /// <summary>
    /// Şifre vs. içeriyorsa maskele.
    /// </summary>
    public string? ConnectionStringMasked { get; set; }

    public int SettingsCount { get; set; }
    public int FormulaSetCount { get; set; }
    public int FormulaLineCount { get; set; }
    public int SnapshotCount { get; set; }

    public List<string> Notes { get; set; } = new();
}
