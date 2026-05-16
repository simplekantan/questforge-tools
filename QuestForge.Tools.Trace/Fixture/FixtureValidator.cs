namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Cross-validates a committed <see cref="FixtureModel"/> against the quest file it references.
/// </summary>
public sealed class FixtureValidator
{
    private readonly string _questDataRoot;

    public FixtureValidator(string questDataRoot)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Validate a fixture model against the quest file it references inside
    /// <see cref="_questDataRoot"/>.
    /// </summary>
    public FixtureValidationResult Validate(FixtureModel fixture)
        => throw new NotImplementedException();

    /// <summary>
    /// Convenience overload: load the fixture from disk, then validate.
    /// </summary>
    public FixtureValidationResult ValidateFile(string fixturePath)
        => throw new NotImplementedException();
}
