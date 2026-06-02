namespace QuestForge.Tools.Trace.Validation;

public sealed record TraceValidationIssue(string Code, string Message, int? LineNumber = null);
