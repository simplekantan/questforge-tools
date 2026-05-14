namespace QuestForge.Tools.Validator;

public record ValidationError(
    string Code,
    string Message,
    string FilePath,
    string Location,        // e.g. "seq:1/branch:fight-or-flee/case:0"
    string? StepId = null,
    Severity Severity = Severity.Error
);

public enum Severity { Error, Warning }