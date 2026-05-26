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
    public AetheryteId? LastAttuned { get; private set; }
    public AetheryteId? LastAethernetShardInteracted { get; private set; }

    private readonly Dictionary<uint, int> _keyItemCounts = new();
    private List<uint>? _pendingKeyItemsAdded;
    private List<uint>? _pendingKeyItemsRemoved;
    private uint _lastInventoryHash;

    // Combat correlation state — mirrors SnapshotAggregator.
    private bool _inCombat;
    private WorldPosition? _combatStartPosition;
    private int _combatStartZone;
    private readonly List<(uint DataId, DateTimeOffset At)> _recentKills = new();
    private readonly List<(NibbleKey Key, int FinalValue, DateTimeOffset At)> _recentNibbleBumps = new();
    private readonly Dictionary<NibbleKey, (HashSet<uint> DataIds, int FinalValue)> _killCorrelatedTargets = new();
    // Baseline for per-index delta detection. Survives ResetPendingKeyItemDeltas.
    private byte[]? _prevQuestVariables;

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

            case "InCombat":
            {
                if (!ev.Value.HasValue) return true;
                var val = ev.Value.Value;
                bool inCombat;
                if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                    inCombat = val.GetBoolean();
                else if (val.ValueKind == JsonValueKind.Object
                    && val.TryGetProperty("value", out var bv)
                    && (bv.ValueKind == JsonValueKind.True || bv.ValueKind == JsonValueKind.False))
                    inCombat = bv.GetBoolean();
                else
                    return true;

                if (inCombat && !_inCombat)
                {
                    _combatStartPosition = Position;
                    _combatStartZone = (int)Zone.Value;
                }
                _inCombat = inCombat;
                return true;
            }

            case "EnemyKilled":
            {
                if (!ev.Value.HasValue) return true;
                var val = ev.Value.Value;
                uint dataId = 0;
                if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("dataId", out var did))
                    try { dataId = did.GetUInt32(); } catch { return true; }
                else
                    return true;

                if (dataId == 0) return true;

                _recentKills.Add((dataId, ev.At));
                EvictStale(ev.At);

                // Correlate-on-arrival: look back at buffered nibble bumps within the symmetric window.
                foreach (var (key, finalValue, bumpAt) in _recentNibbleBumps)
                {
                    if (AbsDelta(ev.At, bumpAt) <= SnapshotAggregator.CombatCorrelationWindow)
                        AddToCorrelation(key, finalValue, dataId);
                }

                return true;
            }

            case "GetQuestVariables":
            {
                if (!QuestArgMatches(ev) || !ev.Value.HasValue) return true;

                // Parse value: array shape [v0..v5] or object-wrapped {"value":[...]}
                var val = ev.Value.Value;
                JsonElement arrayEl;
                if (val.ValueKind == JsonValueKind.Array)
                    arrayEl = val;
                else if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var wrappedArr)
                    && wrappedArr.ValueKind == JsonValueKind.Array)
                    arrayEl = wrappedArr;
                else
                    return true;

                var newVars = new byte[arrayEl.GetArrayLength()];
                int idx2 = 0;
                foreach (var el in arrayEl.EnumerateArray())
                {
                    try { newVars[idx2] = (byte)el.GetInt32(); } catch { }
                    idx2++;
                }

                // First observation: establish the baseline only. No delta, no bump, no correlation.
                if (_prevQuestVariables is null)
                {
                    _prevQuestVariables = newVars;
                    return true;
                }

                var prev = _prevQuestVariables;
                _prevQuestVariables = newVars;

                EvictStale(ev.At);

                for (int i = 0; i < newVars.Length; i++)
                {
                    var n = newVars[i];
                    var p = i < prev.Length ? prev[i] : (byte)0;

                    var lowUp  = (n & 0x0F) > (p & 0x0F);
                    var highUp = (n >> 4)   > (p >> 4);

                    if (lowUp)
                    {
                        var key = new NibbleKey(i, NibbleHalf.Low);
                        var nibbleValue = n & 0x0F;
                        _recentNibbleBumps.Add((key, nibbleValue, ev.At));
                        CorrelateKillsToBump(key, nibbleValue, ev.At);
                    }

                    if (highUp)
                    {
                        var key = new NibbleKey(i, NibbleHalf.High);
                        var nibbleValue = n >> 4;
                        _recentNibbleBumps.Add((key, nibbleValue, ev.At));
                        CorrelateKillsToBump(key, nibbleValue, ev.At);
                    }
                }

                return true;
            }

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

            case "IsAetheryteAttuned":
            {
                // Parse argument: {"value": uint} → aetheryteId
                if (!ev.Argument.HasValue) return true;
                var arg = ev.Argument.Value;
                uint argId = 0;
                if (arg.ValueKind == JsonValueKind.Number)
                    { try { argId = arg.GetUInt32(); } catch { } }
                else if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var av))
                    { try { argId = av.GetUInt32(); } catch { } }

                if (argId == 0) return true;

                // Parse value: integer 1, boolean true → set LastAttuned; 0 or false → no-op
                if (!ev.Value.HasValue) return true;
                var val = ev.Value.Value;
                bool isTruthy = val.ValueKind == JsonValueKind.True
                    || (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var iv) && iv == 1)
                    // object-wrapped: {"value": true/1}
                    || (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var vv)
                        && (vv.ValueKind == JsonValueKind.True
                            || (vv.ValueKind == JsonValueKind.Number && vv.TryGetInt32(out var ivv) && ivv == 1)));

                if (isTruthy)
                    LastAttuned = new AetheryteId(argId);

                return true;
            }

            case "AethernetShardTargeted":
            {
                // Parse value as plain uint (or {"value": uint})
                if (!ev.Value.HasValue) return true;
                var val = ev.Value.Value;
                uint shardId = 0;
                if (val.ValueKind == JsonValueKind.Number)
                    { try { shardId = val.GetUInt32(); } catch { } }
                else if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var vv))
                    { try { shardId = vv.GetUInt32(); } catch { } }

                LastAethernetShardInteracted = new AetheryteId(shardId);
                return true;
            }

            case "GetItemCount":
            {
                // Parse argument: {"value": uint} → itemId
                if (!ev.Argument.HasValue || !ev.Value.HasValue) return true;
                var arg = ev.Argument.Value;
                uint itemId = 0;
                if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var av))
                    { try { itemId = av.GetUInt32(); } catch { return true; } }
                else if (arg.ValueKind == JsonValueKind.Number)
                    { try { itemId = arg.GetUInt32(); } catch { return true; } }

                if (itemId == 0) return true;

                // Parse value: {"value": int} or plain int; guard against failure shape
                var val = ev.Value.Value;
                int count = 0;
                if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var vv))
                    { try { count = vv.GetInt32(); } catch { return true; } }
                else if (val.ValueKind == JsonValueKind.Number)
                    { try { count = val.GetInt32(); } catch { return true; } }
                else
                    return true; // failure or unknown shape — guard

                _keyItemCounts[itemId] = count;
                return true;
            }

            case "InventoryChanged":
            {
                // Value shape: {"gained":[{"itemId":N,"qty":N}],"lost":[...],"newHash":N}
                if (!ev.Value.HasValue) return true;
                var root = ev.Value.Value;
                if (root.ValueKind != JsonValueKind.Object) return true;

                if (root.TryGetProperty("gained", out var gainedArr)
                    && gainedArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in gainedArr.EnumerateArray())
                    {
                        if (!item.TryGetProperty("itemId", out var idEl)) continue;
                        if (!item.TryGetProperty("qty", out var qtyEl)) continue;
                        try
                        {
                            var id  = idEl.GetUInt32();
                            var qty = qtyEl.GetInt32();
                            _keyItemCounts.TryGetValue(id, out var current);
                            _keyItemCounts[id] = current + qty;
                            (_pendingKeyItemsAdded ??= []).Add(id);
                        }
                        catch { /* guard */ }
                    }
                }

                if (root.TryGetProperty("lost", out var lostArr)
                    && lostArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in lostArr.EnumerateArray())
                    {
                        if (!item.TryGetProperty("itemId", out var idEl)) continue;
                        if (!item.TryGetProperty("qty", out var qtyEl)) continue;
                        try
                        {
                            var id  = idEl.GetUInt32();
                            var qty = qtyEl.GetInt32();
                            _keyItemCounts.TryGetValue(id, out var current);
                            var newQty = current - qty;
                            if (newQty <= 0)
                                _keyItemCounts.Remove(id);
                            else
                                _keyItemCounts[id] = newQty;
                            (_pendingKeyItemsRemoved ??= []).Add(id);
                        }
                        catch { /* guard */ }
                    }
                }

                if (root.TryGetProperty("newHash", out var hashEl))
                    try { _lastInventoryHash = hashEl.GetUInt32(); } catch { }

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Convenience alias used by test helpers — applies an ObservationEvent with
    /// Method="InventoryChanged" to accumulated key-item counts.
    /// </summary>
    public void ApplyInventoryChanged(ObservationEvent ev) => Apply(ev);

    /// <summary>
    /// Clear pending delta lists so the next decision's "after" snapshot sees a clean slate.
    /// Does NOT clear the running _keyItemCounts or _prevQuestVariables (cross-window continuity).
    /// </summary>
    public void ResetPendingKeyItemDeltas()
    {
        _pendingKeyItemsAdded = null;
        _pendingKeyItemsRemoved = null;
        _recentKills.Clear();
        _recentNibbleBumps.Clear();
        _killCorrelatedTargets.Clear();
    }

    /// <summary>
    /// Record the navigate destination as the last NPC position.
    /// Called from the extractor when a Navigate action completes.
    /// </summary>
    public void RecordNavigateDestination(float x, float y, float z)
        => LastNpcPosition = new WorldPosition(x, y, z);

    /// <summary>Overload accepting WorldPosition directly (used by tests).</summary>
    public void RecordNavigateDestination(WorldPosition destination)
        => LastNpcPosition = destination;

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

    private IReadOnlyDictionary<NibbleKey, KillCorrelation>? BuildKillCorrelatedTargets()
    {
        if (_killCorrelatedTargets.Count == 0) return null;
        var result = new Dictionary<NibbleKey, KillCorrelation>(_killCorrelatedTargets.Count);
        foreach (var (key, (dataIds, finalValue)) in _killCorrelatedTargets)
        {
            if (dataIds.Count == 0) continue;
            result[key] = new KillCorrelation(
                dataIds.OrderBy(id => id).ToList(),
                finalValue);
        }
        return result.Count > 0 ? result : null;
    }

    private void EvictStale(DateTimeOffset now)
    {
        var cutoff = now - SnapshotAggregator.CombatCorrelationWindow;
        _recentKills.RemoveAll(k => k.At < cutoff);
        _recentNibbleBumps.RemoveAll(b => b.At < cutoff);
    }

    private static TimeSpan AbsDelta(DateTimeOffset a, DateTimeOffset b)
    {
        var d = a - b;
        return d < TimeSpan.Zero ? -d : d;
    }

    private void CorrelateKillsToBump(NibbleKey key, int finalValue, DateTimeOffset bumpAt)
    {
        foreach (var (dataId, killAt) in _recentKills)
        {
            if (AbsDelta(bumpAt, killAt) <= SnapshotAggregator.CombatCorrelationWindow)
                AddToCorrelation(key, finalValue, dataId);
        }

        if (!_killCorrelatedTargets.TryGetValue(key, out var bucket))
            _killCorrelatedTargets[key] = (new HashSet<uint>(), finalValue);
        else
            _killCorrelatedTargets[key] = (bucket.DataIds, finalValue);
    }

    private void AddToCorrelation(NibbleKey key, int finalValue, uint dataId)
    {
        if (!_killCorrelatedTargets.TryGetValue(key, out var bucket))
            bucket = (new HashSet<uint>(), finalValue);
        bucket.DataIds.Add(dataId);
        _killCorrelatedTargets[key] = (bucket.DataIds, finalValue);
    }

    /// <summary>Capture an immutable snapshot at the given timestamp.</summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at)
        => new(at, Zone, Position, _activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, _lastInventoryHash, LastAttuned)
        {
            LastAethernetShardInteracted = LastAethernetShardInteracted,
            KeyItems = new Dictionary<uint, int>(_keyItemCounts),
            KeyItemsAdded = _pendingKeyItemsAdded,
            KeyItemsRemoved = _pendingKeyItemsRemoved,
            InCombat = _inCombat,
            KillCorrelatedTargets = BuildKillCorrelatedTargets(),
            CombatStartPosition = _combatStartPosition,
            CombatStartZone = _combatStartZone
        };

    /// <summary>
    /// Convenience overload used by <see cref="Quest.TraceToQuestExtractor"/>.
    /// Overrides the active quest in the produced snapshot without mutating this instance.
    /// </summary>
    public GameStateSnapshot ToSnapshot(DateTimeOffset at, QuestId activeQuest)
        => new(at, Zone, Position, activeQuest, QuestSequence, QuestFlags,
               QuestAccepted, QuestCompleted, LastNpcInteracted, LastNpcPosition,
               null, null, _lastInventoryHash, LastAttuned)
        {
            LastAethernetShardInteracted = LastAethernetShardInteracted,
            KeyItems = new Dictionary<uint, int>(_keyItemCounts),
            KeyItemsAdded = _pendingKeyItemsAdded,
            KeyItemsRemoved = _pendingKeyItemsRemoved,
            InCombat = _inCombat,
            KillCorrelatedTargets = BuildKillCorrelatedTargets(),
            CombatStartPosition = _combatStartPosition,
            CombatStartZone = _combatStartZone
        };

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
