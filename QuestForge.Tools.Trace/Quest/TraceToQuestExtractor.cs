using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;

namespace QuestForge.Tools.Trace.Quest;

/// <summary>
/// Replays a trace event sequence to produce a <see cref="QuestDraftResult"/> containing
/// a partially-inferred <see cref="Schema.QuestDefinition"/> and a checklist of TODOs for
/// the human author.
/// </summary>
public sealed class TraceToQuestExtractor
{
    private readonly StepInferenceEngine _inference;

    public TraceToQuestExtractor(StepInferenceEngine? inference = null)
        => _inference = inference ?? new StepInferenceEngine();

    /// <summary>
    /// Extract a quest draft from the supplied event list.
    /// Returns <see cref="Result{T}.Failure"/> with code <c>"no-run-start"</c> when no
    /// <see cref="RunStartEvent"/> is present.
    /// </summary>
    public Result<QuestDraftResult> Extract(IReadOnlyList<TraceEvent> events)
        => throw new NotImplementedException();
}
