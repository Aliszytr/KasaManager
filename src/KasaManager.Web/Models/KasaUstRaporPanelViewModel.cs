using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Web.Models;

/// <summary>
/// KasaÜstRapor Panel — hem Import hem KasaPreview sayfalarında kullanılan
/// reusable partial view model. Grid, tarih kural motoru ve snapshot kontrolü.
/// </summary>
public sealed class KasaUstRaporPanelViewModel
{
    // Grid verileri
    public ImportedTable? Table { get; set; }
    public string? KasaUstRaporFileName { get; set; }
    public DateOnly? ProposedDate { get; set; }
    public DateOnly? FinalDate { get; set; }
    public string? VeznedarColumn { get; set; }
    public string? BakiyeColumn { get; set; }
    public List<string> DefaultVergiKasaVeznedarlar { get; set; } = new();

    // Tarih Kural Motoru bilgisi
    public DateRulesEvaluation? DateEval { get; set; }

    // Snapshot durumu
    public bool HasExistingSnapshot { get; set; }
    public DateOnly? LastSnapshotDate { get; set; }

    // Kontrol ayarları
    public bool StartOpen { get; set; } = true;
    public bool ShowSaveButton { get; set; } = true;
    public string Context { get; set; } = "import"; // "import" | "kasapreview"

    // Uyarılar
    public List<string> Warnings { get; set; } = new();
}
