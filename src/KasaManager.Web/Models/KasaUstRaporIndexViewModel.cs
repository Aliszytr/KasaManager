using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Web.Models;

public sealed class KasaUstRaporIndexViewModel
{
    public List<string> UploadedFiles { get; init; } = new();

    public string? KasaUstRaporFileName { get; init; }
    public ImportedTable? Table { get; init; }

    public DateRulesEvaluation? DateEval { get; init; }

    // UI seçimleri
    public DateOnly? ProposedDate { get; init; }
    public DateOnly? FinalDate { get; init; }

    public string? VeznedarColumn { get; init; }
    public string? BakiyeColumn { get; init; }

    public bool RequireExplicitApprove { get; init; }

    /// <summary>
    /// Ayarlardan gelen varsayılan "Vergi Kasa" veznedarları.
    /// KasaÜstRapor grid'inde checkbox'ları otomatik işaretlemek için kullanılır.
    /// </summary>
    public List<string> DefaultVergiKasaVeznedarlar { get; init; } = new();
}