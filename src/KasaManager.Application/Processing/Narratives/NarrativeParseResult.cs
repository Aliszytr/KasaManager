using System.Collections.Generic;

namespace KasaManager.Application.Processing.Narratives;

public sealed class NarrativeParseResult
{
    public string SelectedSegment { get; init; } = string.Empty;
    public string Normalized { get; init; } = string.Empty;
    public string NormalizedCourtUnit { get; init; } = string.Empty;
    public string FileNo { get; init; } = string.Empty;

    public List<NarrativeIssue> Issues { get; init; } = new();
}
