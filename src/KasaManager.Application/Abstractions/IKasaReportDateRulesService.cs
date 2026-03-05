using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R7: KasaÜstRapor tarihi, rapor dosyalarının mevcut tarih sütunlarından tespit edilir.
/// Excel'e kolon ekleme/doldurma yoktur. Motor sadece okur, kıyaslar ve uyarı üretir.
/// </summary>
public interface IKasaReportDateRulesService
{
    Task<DateRulesEvaluation> EvaluateAsync(string uploadFolderAbsolute, CancellationToken ct = default);
}

public sealed record DateRulesEvaluation
{
    public DateOnly? ProposedDate { get; init; }
    public DateOnly? FinalSuggestedDate { get; init; }

    /// <summary>DB'de (Genel) son snapshot tarihi.</summary>
    public DateOnly? DbLastSnapshotDate { get; init; }

    /// <summary>DB'ye göre beklenen bir sonraki tarih (son + 1 gün).</summary>
    public DateOnly? DbExpectedNextDate { get; init; }

    public bool HasConflict { get; init; }
    public bool HasAnyDate { get; init; }
    public bool ContinuityLooksOk { get; init; }

    public List<DateRulesSourceDate> Sources { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> Errors { get; init; } = new();

    public bool RequiresUserDecision => HasConflict || !HasAnyDate || !ContinuityLooksOk;
}

public sealed class DateRulesSourceDate
{
    public ImportFileKind Kind { get; init; }
    public string FileName { get; init; } = string.Empty;
    public List<DateOnly> DistinctDates { get; init; } = new();
    public int ParsedRowCount { get; init; }
    public int TotalRowCount { get; init; }

    public bool HasDate => DistinctDates.Count > 0;
    public bool IsConflicting => DistinctDates.Count > 1;
}
