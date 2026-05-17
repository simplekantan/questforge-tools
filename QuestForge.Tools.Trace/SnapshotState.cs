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
                if (ev.Value.HasValue
                    && ev.Value.Value.TryGetProperty("value", out var zv)
                    && zv.TryGetUInt32(out var zoneVal))
                    Zone = new ZoneId(zoneVal);
                return true;

            case "GetPlayerPosition":
                if (ev.Value.HasValue && ev.Value.Value.ValueKind == JsonValueKind.Object)
                {
                    var root = ev.Value.Value;
                    var x = root.TryGetProperty("x", out var xp) && xp.TryGetSingle(out var xv) ? xv : 0f;
                    var y = root.TryGetProperty("y", out var yp) && yp.TryGetSingle(out var yv) ? yv : 0f;
                    var z = root.TryGetProperty("z", out var zp) && zp.TryGetSingle(out var zv2) ? zv2 : 0f;
                    Position = new WorldPosition(x, y, z);
                }
                return true;

            case "GetQuestSequence":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.TryGetInt32(out var seq))
                    QuestSequence = seq;
                return true;

            case "GetQuestFlags":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.TryGetUInt32(out var flags))
                    QuestFlags = flags;
                return true;

            case "IsQuestAccepted":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    QuestAccepted = ev.Value.Value.GetBoolean();
                return true;

            case "IsQuestComplete":
                if (QuestArgMatches(ev) && ev.Value.HasValue
                    && ev.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    QuestCompleted = ev.Value.Value.GetBoolean();
                return true;

            default:
                return false;
        }
    }

    private bool QuestArgMatches(ObservationEvent ev)
    {
        if (!ev.Argument.HasValue) return false;
        var arg = ev.Argument.Value;

        // Old traces may have written the quest ID as a plain JSON number (e.g. 66104)
        // instead of an object ({"value": 66104}). Handle both forms.
        if (arg.ValueKind == JsonValueKind.Number)
        {
            try { return arg.GetUInt32() == _activeQuest.Value; } catch { return false; }
        }

        if (arg.ValueKind == JsonValueKind.Object &&
            arg.TryGetProperty("value", out var v))
        {
            try { return v.GetUInt32() == _activeQuest.Value; } catch { return false; }
        }

        return false;
    }

    /// <summary>Capture an immutable snapshot at the given timestamp.</summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at)
        => new(at, Zone, Position, _activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, 0u, null);  // Phase 11B: LastAttuned not tracked by replay extractor yet

    /// <summary>
    /// Convenience overload used by <see cref="Quest.TraceToQuestExtractor"/>.
    /// Overrides the active quest in the produced snapshot without mutating this instance.
    /// </summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at, QuestId activeQuest)
        => new(at, Zone, Position, activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, 0u, null);  // Phase 11B: LastAttuned not tracked by replay extractor yet

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
