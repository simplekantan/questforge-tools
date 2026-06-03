namespace QuestForge.Tools.Trace.Analysis;

public sealed record QuestStateChange(
    int Seq,
    string Kind,
    string PreviousValue,
    string NewValue,
    string? Detail,
    string? AfterStepId,
    string? AfterActionType);

public sealed record QuestStateChangeReport(
    uint? QuestId,
    IReadOnlyList<QuestStateChange> Changes);
