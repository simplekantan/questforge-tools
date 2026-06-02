namespace QuestForge.Tools.Trace.Validation;

public sealed record TraceValidationResult(
    IReadOnlyList<TraceValidationIssue> Errors,
    IReadOnlyList<TraceValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
}
