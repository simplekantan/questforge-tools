namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// POCO matching the fixture JSON format defined in FIXTURES.md.
/// </summary>
public sealed record FixtureModel(
    string SchemaVersion,
    string Description,
    string InitialState,
    IReadOnlyList<string> Capabilities,
    string QuestFile,
    IReadOnlyList<TransitionEntry> ExpectedTransitions,
    string? TerminalOutcome);

/// <summary>
/// A single expected decision transition (step-id + action-type pair).
/// </summary>
public sealed record TransitionEntry(string? StepId, string ActionType);
