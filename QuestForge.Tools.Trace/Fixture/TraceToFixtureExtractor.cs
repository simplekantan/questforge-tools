using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;

namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Converts a list of parsed <see cref="TraceEvent"/>s into a <see cref="FixtureModel"/>
/// suitable for committing as a regression fixture.
/// </summary>
public sealed class TraceToFixtureExtractor
{
    private readonly string? _questDataRoot;

    public TraceToFixtureExtractor(string? questDataRoot = null)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Extract a fixture from the supplied event list.
    /// Returns <see cref="Result{T}.Failure"/> with code <c>"no-run-start"</c> when the
    /// list contains no <see cref="RunStartEvent"/>.
    /// </summary>
    public Result<FixtureModel> Extract(IReadOnlyList<TraceEvent> events)
        => throw new NotImplementedException();

    /// <summary>
    /// Suggest a canonical filename for the given fixture based on the capability set it exercises.
    /// Falls back to <c>"simple-linear-acceptance.json"</c> when no mapping is found.
    /// </summary>
    public string SuggestFilename(FixtureModel fixture)
        => throw new NotImplementedException();
}
