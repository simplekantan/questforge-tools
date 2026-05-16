namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// A single entry in the list produced by <see cref="ListFixturesCommand.Enumerate"/>.
/// </summary>
public sealed record FixtureListEntry(
    string FixtureFile,
    IReadOnlyList<string> Capabilities,
    bool QuestFileExists,
    string? QuestFile);
