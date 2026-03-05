namespace KasaManager.Application.Processing.Narratives;

public sealed class NarrativeIssue
{
    public NarrativeIssue(string message)
    {
        Message = message ?? string.Empty;
    }

    public string Message { get; }
}
