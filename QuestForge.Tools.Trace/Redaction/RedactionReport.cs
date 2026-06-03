namespace QuestForge.Tools.Trace.Redaction;

public sealed record ExcludedFieldHit(string PropertyName, int LineNumber);

public sealed record RedactionReport(
    int TotalLines,
    int WallClockStripped,
    int AlreadyRedacted,
    IReadOnlyList<ExcludedFieldHit> ExcludedFieldHits);
