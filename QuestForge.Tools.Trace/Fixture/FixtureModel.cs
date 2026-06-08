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
    string? TerminalOutcome,
    InitialOverrides? InitialOverrides = null);

/// <summary>
/// A single expected decision transition (step-id + action-type pair).
/// </summary>
public sealed record TransitionEntry(string? StepId, string ActionType);

/// <summary>
/// Optional overrides for the initial game state when running a fixture.
/// Inferred from the trace's first observations where available.
/// Values not present in the trace (equipped slots, inventory) must be author-provided.
/// </summary>
public sealed record InitialOverrides(
    int? Zone = null,
    float? PositionX = null,
    float? PositionY = null,
    float? PositionZ = null,
    int? QuestSequence = null);
