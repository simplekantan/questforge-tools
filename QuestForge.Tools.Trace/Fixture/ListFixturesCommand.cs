namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Enumerates committed fixture files and computes capability coverage gaps.
/// </summary>
public sealed class ListFixturesCommand
{
    private readonly string _questDataRoot;

    public ListFixturesCommand(string questDataRoot)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Enumerate all fixture files under <c>fixtures/engine/*.json</c> relative to
    /// <see cref="_questDataRoot"/>.
    /// </summary>
    public IReadOnlyList<FixtureListEntry> Enumerate()
        => throw new NotImplementedException();

    /// <summary>
    /// Compute capability tags that appear in any quest in the data root but are not
    /// covered by any fixture in <paramref name="fixtures"/>.
    /// </summary>
    public IReadOnlyList<string> ComputeGaps(IReadOnlyList<FixtureListEntry> fixtures)
        => throw new NotImplementedException();
}
