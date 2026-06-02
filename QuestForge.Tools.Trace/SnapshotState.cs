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

    // Purchase span state — mirrors SnapshotAggregator purchase span fields exactly.
    private bool _shopOpen;
    private bool _purchaseSpanStarted;
    private long? _purchaseBaselineGil;
    private int? _purchaseBaselineSeals;
    private long _currentGil;
    private int _currentSeals;
    private readonly Dictionary<uint, int> _spanItemDeltas = new();

    // G5b — last non-null GC axes observed during the open span; mirror of
    // SnapshotAggregator._spanActiveGcCategory / _spanActiveGcRankTier.
    // "Last seen non-null wins" — a null observation does NOT clear a prior value.
    private int? _spanActiveGcCategory;
    private int? _spanActiveGcRankTier;

    // Combat correlation state — mirrors SnapshotAggregator (target-based span model).
    private bool _inCombat;
    private WorldPosition? _combatStartPosition;
    private int _combatStartZone;

    // Tracks the most-recently targeted BattleNpc regardless of combat state.
    // Persists across ResetPendingKeyItemDeltas so a pre-combat target is still available
    // to seed the next span even if Reset runs between targeting and InCombat{true}.
    private uint? _lastBattleNpcTarget;

    // Distinct hostile targets acquired while _inCombat. Cleared on each false→true InCombat transition.
    private readonly HashSet<uint> _spanBattleNpcTargets = new();

    // Nibble bumps that occurred while _inCombat. Key → latest (highest) value reached.
    // Cleared on each false→true InCombat transition.
    private readonly Dictionary<NibbleKey, int> _spanNibbleBumps = new();

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
        if (ev.Data.Value.HasValue && ev.Data.Value.Value.ValueKind == JsonValueKind.Object
            && ev.Data.Value.Value.TryGetProperty("failure", out _))
            return true; // recognised but skipped

        switch (ev.Data.Method)
        {
            case "GetPlayerZone":
                if (ev.Data.Value.HasValue
                    && ev.Data.Value.Value.TryGetProperty("value", out var zv)
                    && zv.TryGetUInt32(out var zoneVal))
                    Zone = new ZoneId(zoneVal);
                return true;

            case "GetPlayerPosition":
                if (ev.Data.Value.HasValue && ev.Data.Value.Value.ValueKind == JsonValueKind.Object)
                {
                    var root = ev.Data.Value.Value;
                    var x = root.TryGetProperty("x", out var xp) && xp.TryGetSingle(out var xv) ? xv : 0f;
                    var y = root.TryGetProperty("y", out var yp) && yp.TryGetSingle(out var yv) ? yv : 0f;
                    var z = root.TryGetProperty("z", out var zp) && zp.TryGetSingle(out var zv2) ? zv2 : 0f;
                    Position = new WorldPosition(x, y, z);
                }
                return true;

            case "GetTarget":
            {
                // Only the hostile-object shape is recognised (D1).
                // Plain numbers and other shapes fall through to return false (unrecognised).
                if (!ev.Data.Value.HasValue) return false;
                var val = ev.Data.Value.Value;
                if (val.ValueKind != JsonValueKind.Object) return false;
                if (!val.TryGetProperty("kind", out var kindEl)) return false;
                if (kindEl.GetString() != "hostile") return false;
                if (!val.TryGetProperty("baseId", out var baseIdEl)) return false;
                uint baseId;
                try { baseId = baseIdEl.GetUInt32(); } catch { return false; }

                // Mirror OnBattleNpcTargeted: set unconditionally; add to span if in combat.
                _lastBattleNpcTarget = baseId;
                if (_inCombat && baseId != 0)
                    _spanBattleNpcTargets.Add(baseId);
                return true;
            }

            case "InCombat":
            {
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
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
                    // Mirror OnInCombatChanged: capture → clear → seed.
                    _combatStartPosition = Position;
                    _combatStartZone = (int)Zone.Value;
                    _spanBattleNpcTargets.Clear();
                    _spanNibbleBumps.Clear();
                    if (_lastBattleNpcTarget is { } t)
                        _spanBattleNpcTargets.Add(t);
                }
                _inCombat = inCombat;
                return true;
            }

            case "EnemyKilled":
                // Recognised no-op (D2): real traces emit this; keep it recognised so the
                // unrecognised-method count is not inflated. No span mutation.
                return true;

            case "CurrencyBalance":
            {
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
                if (val.ValueKind != JsonValueKind.Object) return true;
                try
                {
                    if (val.TryGetProperty("gil", out var gilEl))
                        _currentGil = gilEl.GetInt64();
                    if (val.TryGetProperty("seals", out var sealsEl))
                        _currentSeals = sealsEl.GetInt32();
                }
                catch { }
                return true;
            }

            case "ShopOpened":
            {
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
                bool open;
                JsonElement? objPayload = null;
                if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                    open = val.GetBoolean();
                else if (val.ValueKind == JsonValueKind.Object
                    && val.TryGetProperty("value", out var bv)
                    && (bv.ValueKind == JsonValueKind.True || bv.ValueKind == JsonValueKind.False))
                {
                    open = bv.GetBoolean();
                    objPayload = val;
                }
                else
                    return true;

                // Parse optional GC sibling keys (G5b); null on missing key, JSON null, or wrong type.
                var cat  = objPayload.HasValue ? ReadOptionalInt(objPayload.Value, "activeGcCategory")  : null;
                var tier = objPayload.HasValue ? ReadOptionalInt(objPayload.Value, "activeGcRankTier")   : null;

                if (open && !_shopOpen)
                {
                    // Clear previous span's GC axes before starting a fresh span.
                    _spanActiveGcCategory  = null;
                    _spanActiveGcRankTier  = null;
                    _purchaseSpanStarted   = true;
                    _purchaseBaselineGil   = _currentGil;
                    _purchaseBaselineSeals = _currentSeals;
                    _spanItemDeltas.Clear();
                }

                // Last-seen non-null wins.
                if (cat  is not null) _spanActiveGcCategory = cat;
                if (tier is not null) _spanActiveGcRankTier = tier;

                _shopOpen = open;
                return true;
            }

            case "VendorItemCount":
            {
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
                if (val.ValueKind != JsonValueKind.Object) return true;
                if (!_purchaseSpanStarted) return true;
                try
                {
                    if (val.TryGetProperty("itemId", out var idEl) && val.TryGetProperty("count", out var cntEl))
                    {
                        var itemId = idEl.GetUInt32();
                        var count  = cntEl.GetInt32();
                        _spanItemDeltas[itemId] = _spanItemDeltas.GetValueOrDefault(itemId) + count;
                    }
                }
                catch { }
                return true;
            }

            case "GetQuestVariables":
            {
                if (!QuestArgMatches(ev) || !ev.Data.Value.HasValue) return true;

                // Parse value: array shape [v0..v5] or object-wrapped {"value":[...]}
                var val = ev.Data.Value.Value;
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

                // Mirror OnQuestVariablesUpdated: gate bumps on _inCombat (D3).
                for (int i = 0; i < newVars.Length; i++)
                {
                    var n = newVars[i];
                    var p = i < prev.Length ? prev[i] : (byte)0;

                    var lowUp  = (n & 0x0F) > (p & 0x0F);
                    var highUp = (n >> 4)   > (p >> 4);

                    if (lowUp && _inCombat)
                    {
                        var key = new NibbleKey(i, NibbleHalf.Low);
                        _spanNibbleBumps[key] = n & 0x0F;
                    }

                    if (highUp && _inCombat)
                    {
                        var key = new NibbleKey(i, NibbleHalf.High);
                        _spanNibbleBumps[key] = n >> 4;
                    }
                }

                return true;
            }

            case "GetQuestSequence":
                if (QuestArgMatches(ev) && ev.Data.Value.HasValue
                    && ev.Data.Value.Value.TryGetInt32(out var seq))
                    QuestSequence = seq;
                return true;

            case "GetQuestFlags":
                if (QuestArgMatches(ev) && ev.Data.Value.HasValue
                    && ev.Data.Value.Value.TryGetUInt32(out var flags))
                    QuestFlags = flags;
                return true;

            case "IsQuestAccepted":
                if (QuestArgMatches(ev) && ev.Data.Value.HasValue
                    && ev.Data.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    QuestAccepted = ev.Data.Value.Value.GetBoolean();
                return true;

            case "IsQuestComplete":
                if (QuestArgMatches(ev) && ev.Data.Value.HasValue
                    && ev.Data.Value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    QuestCompleted = ev.Data.Value.Value.GetBoolean();
                return true;

            case "IsAetheryteAttuned":
            {
                // Parse argument: {"value": uint} → aetheryteId
                if (!ev.Data.Argument.HasValue) return true;
                var arg = ev.Data.Argument.Value;
                uint argId = 0;
                if (arg.ValueKind == JsonValueKind.Number)
                    { try { argId = arg.GetUInt32(); } catch { } }
                else if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var av))
                    { try { argId = av.GetUInt32(); } catch { } }

                if (argId == 0) return true;

                // Parse value: integer 1, boolean true → set LastAttuned; 0 or false → no-op
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
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
                if (!ev.Data.Value.HasValue) return true;
                var val = ev.Data.Value.Value;
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
                if (!ev.Data.Argument.HasValue || !ev.Data.Value.HasValue) return true;
                var arg = ev.Data.Argument.Value;
                uint itemId = 0;
                if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var av))
                    { try { itemId = av.GetUInt32(); } catch { return true; } }
                else if (arg.ValueKind == JsonValueKind.Number)
                    { try { itemId = arg.GetUInt32(); } catch { return true; } }

                if (itemId == 0) return true;

                // Parse value: {"value": int} or plain int; guard against failure shape
                var val = ev.Data.Value.Value;
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
                if (!ev.Data.Value.HasValue) return true;
                var root = ev.Data.Value.Value;
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
    /// Clears span sets (_spanBattleNpcTargets, _spanNibbleBumps) but PRESERVES
    /// _prevQuestVariables and _lastBattleNpcTarget (cross-window continuity). Mirrors ResetDeltas.
    /// Also clears the purchase span so PurchaseDetected is null for the next window;
    /// preserves _currentGil/_currentSeals as the latest-known balances.
    /// </summary>
    public void ResetPendingKeyItemDeltas()
    {
        _pendingKeyItemsAdded = null;
        _pendingKeyItemsRemoved = null;
        _spanBattleNpcTargets.Clear();
        _spanNibbleBumps.Clear();
        // _prevQuestVariables and _lastBattleNpcTarget intentionally preserved.
        _spanItemDeltas.Clear();
        _purchaseSpanStarted   = false;
        _purchaseBaselineGil   = null;
        _purchaseBaselineSeals = null;
        _spanActiveGcCategory  = null;
        _spanActiveGcRankTier  = null;
        // _currentGil and _currentSeals intentionally preserved as latest-known balances.

        // Offline mirror of the questforge#93 (G6) live fix: _shopOpen reflects the trace's
        // view of the addon state, NOT the span lifecycle. The extractor calls this between
        // each emitted purchase-item step; if the trace's recording window contains multiple
        // purchases at the same vendor with no ShopOpened{false} between them, the OLD
        // force-clear (_shopOpen=false) silenced every subsequent VendorItemCount and the
        // extractor emitted only the first step. If shop is still open at reset time,
        // restart the span at the current balances so the next VendorItemCount registers.
        // GC axes are left null and refill from the next ShopOpened payload (or stay null
        // for the trace's remainder if there is no further ShopOpened).
        if (_shopOpen)
        {
            _purchaseSpanStarted   = true;
            _purchaseBaselineGil   = _currentGil;
            _purchaseBaselineSeals = _currentSeals;
        }
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
        if (!ev.Data.Argument.HasValue) return false;
        var arg = ev.Data.Argument.Value;

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

    // Mirror SnapshotAggregator.BuildPurchaseDetected.
    private PurchaseDetection? BuildPurchaseDetected()
    {
        if (!_purchaseSpanStarted) return null;
        return new PurchaseDetection(
            ShopWasOpen:      true,
            ItemDeltas:       new Dictionary<uint, int>(_spanItemDeltas),
            GilDropped:       Math.Max(0L, (_purchaseBaselineGil   ?? _currentGil)   - _currentGil),
            SealsDropped:     Math.Max(0,  (_purchaseBaselineSeals ?? _currentSeals)  - _currentSeals),
            ActiveGcCategory: _spanActiveGcCategory,
            ActiveGcRankTier: _spanActiveGcRankTier);
    }

    private static int? ReadOptionalInt(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        if (prop.ValueKind != JsonValueKind.Number) return null;
        return prop.TryGetInt32(out var v) ? v : (int?)null;
    }

    // Mirror SnapshotAggregator.BuildKillCorrelatedTargets (D5).
    private IReadOnlyDictionary<NibbleKey, KillCorrelation>? BuildKillCorrelatedTargets()
    {
        if (_spanNibbleBumps.Count == 0 || _spanBattleNpcTargets.Count == 0) return null;
        var dataIds = _spanBattleNpcTargets.OrderBy(id => id).ToList();
        var result = new Dictionary<NibbleKey, KillCorrelation>(_spanNibbleBumps.Count);
        foreach (var (key, value) in _spanNibbleBumps)
            result[key] = new KillCorrelation(dataIds, value);
        return result.Count > 0 ? result : null;
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
            CombatStartZone = _combatStartZone,
            PurchaseDetected = BuildPurchaseDetected()
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
            CombatStartZone = _combatStartZone,
            PurchaseDetected = BuildPurchaseDetected()
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
