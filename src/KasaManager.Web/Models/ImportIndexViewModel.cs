using KasaManager.Domain.Reports;

namespace KasaManager.Web.Models;

public sealed class ImportIndexViewModel
{
    public List<string> UploadedFiles { get; set; } = new();

    public string? SelectedFileName { get; set; }
    public ImportFileKind SelectedKind { get; set; } = ImportFileKind.Unknown;

    /// <summary>
    /// KasaÜstRapor Panel verileri — Import sayfasında inline snapshot kaydetme desteği.
    /// </summary>
    public KasaUstRaporPanelViewModel? UstRaporPanel { get; set; }
}

