using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;

namespace QuestForge.Tools.Trace;

/// <summary>
/// Accumulates game-state observations from a trace replay so that
/// <see cref="StepInferenceEngine.Infer"/> can be called at each action boundary.
/// Last-value-wins. Quest-scoped fields are only updated when the argument quest ID
/// matches <see cref="_activeQuest"/>.
/// </summary>
public sealed class SnapshotState
{
    private readonly QuestId _activeQuest;

    public SnapshotState(QuestId activeQuest)
        => _activeQuest = activeQuest;

    // Accumulated state — all mutable.
    public ZoneId Zone { get; private set; }
    public WorldPosition Position { get; private set; }
    public int QuestSequence { get; private set; }
    public uint QuestFlags { get; private set; }
    public bool QuestAccepted { get; private set; }
    public bool QuestCompleted { get; private set; }
    public NpcId? LastNpcInteracted { get; private set; }
    public WorldPosition? LastNpcPosition { get; private set; }

    /// <summary>
    /// Apply one observation event to the accumulated state.
    /// Returns <c>false</c> only when the method name is not recognised.
    /// Returns <c>true</c> for recognised methods even when the quest-ID filter
    /// prevents mutation (the method is still recognised).
    /// Null or failure-shaped values are accepted without mutation.
    /// Type mismatches are swallowed silently.
    /// </summary>
    public bool Apply(ObservationEvent ev)
    {
        // Check for failure-shaped value (has "failure" key)
        if (ev.Value.HasValue && ev.Value.Value.ValueKind == JsonValueKind.Object
            && ev.Value.Value.TryGetProperty("failure", out _))
            return true; // recognised but skipped

        switch (ev.Method)
        {
            case "GetPlayerZone":
                if (ev.Value.HasValue && ev.Value.Value.TryGetProperty("value", out var zv))
                {
                    try { Zone = new ZoneId(zv.GetUInt32()); } catch { /* swallow type mismatch */ }
                }
                return true;

            case "GetPlayerPosition":
                if (ev.Value.HasValue && ev.Value.Value.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var x = ev.Value.Value.TryGetProperty("x", out var xp) ? xp.GetSingle() : 0f;
                        var y = ev.Value.Value.TryGetProperty("y", out var yp) ? yp.GetSingle() : 0f;
                        var z = ev.Value.Value.TryGetProperty("z", out var zp) ? zp.GetSingle() : 0f;
                        Position = new WorldPosition(x, y, z);
                    }
                    catch { /* swallow type mismatch */ }
                }
                return true;

            case "GetQuestSequence":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind == JsonValueKind.Number)
                {
                    try { QuestSequence = ev.Value.Value.GetInt32(); } catch { /* swallow */ }
                }
                return true;

            case "GetQuestFlags":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind == JsonValueKind.Number)
                {
                    try { QuestFlags = ev.Value.Value.GetUInt32(); } catch { /* swallow */ }
                }
                return true;

            case "IsQuestAccepted":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    try { QuestAccepted = ev.Value.Value.GetBoolean(); } catch { /* swallow */ }
                }
                return true;

            case "IsQuestComplete":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    try { QuestCompleted = ev.Value.Value.GetBoolean(); } catch { /* swallow */ }
                }
                return true;

            default:
                return false; // unrecognised
        }
    }

    private bool QuestArgMatches(ObservationEvent ev)
    {
        if (!ev.Argument.HasValue) return false;
        if (ev.Argument.Value.TryGetProperty("value", out var v))
        {
            try { return v.GetUInt32() == _activeQuest.Value; } catch { return false; }
        }
        return false;
    }

    /// <summary>Capture an immutable snapshot at the given timestamp.</summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at)
        => new(at, Zone, Position, _activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, 0u);

    /// <summary>
    /// Convenience overload used by <see cref="Quest.TraceToQuestExtractor"/>.
    /// Overrides the active quest in the produced snapshot without mutating this instance.
    /// </summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at, QuestId activeQuest)
        => new(at, Zone, Position, activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, 0u);

    /// <summary>
    /// Record an NPC interaction action (called by the extractor at action boundaries,
    /// not from observation events).
    /// </summary>
    public void RecordInteract(NpcId target)
    {
        LastNpcInteracted = target;
        // LastNpcPosition stays from the snapshot polling (not from action params)
    }
}
