namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// The result of validating a <see cref="FixtureModel"/> against its referenced quest file.
/// </summary>
public sealed record FixtureValidationResult(
    IReadOnlyList<FixtureValidationIssue> Errors,
    IReadOnlyList<FixtureValidationIssue> Warnings)
{
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>A single validation error or warning with an error code and human-readable message.</summary>
public sealed record FixtureValidationIssue(
    string Code,
    string Message,
    string? StepId = null);
