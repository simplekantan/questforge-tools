using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for PR-B: SnapshotState target-based span attribution (offline mirror).
///
/// Replaces the kill→time-window model (GWT-O1'..O8') with a target-based span model that
/// mirrors SnapshotAggregator exactly.
///
/// REWRITTEN: O1'→T1, O2'→T-WRONGQUEST, O4'→T5, O5'→T5-HIGH, O6'→T7'.
/// DELETED: O3' (no "window" concept; replaced by T3').
/// NEW: T2', T2b, T3', T4, T6, T8, T9, T10', T-A, T-B, T-NOOP, T-BASELINE.
/// KEPT (non-combat, unchanged): O7'→T8, O8' (sequence advance), baseline-only, InCombat projection, bare-bool.
///
/// Tests compile against the CURRENT public API of SnapshotState (Apply, ToSnapshot,
/// ResetPendingKeyItemDeltas, RecordInteract, RecordNavigateDestination) and will fail
/// at runtime once the compile blocker (CombatCorrelationWindow) is fixed by the Builder.
/// </summary>
public sealed class SnapshotStateCombatTests
{
    private static readonly QuestId CombatQuest = new(65847u);

    private static readonly DateTimeOffset T_Zero = T0;
    private static readonly DateTimeOffset T_250  = T0.AddMilliseconds(250);
    private static readonly DateTimeOffset T_500  = T0.AddMilliseconds(500);
    private static readonly DateTimeOffset T_600  = T0.AddMilliseconds(600);
    private static readonly DateTimeOffset T_900  = T0.AddMilliseconds(900);
    private static readonly DateTimeOffset T_1100 = T0.AddMilliseconds(1100);

    // ─── Formatting helpers ───────────────────────────────────────────────────

    private static string FormatKct(IReadOnlyDictionary<NibbleKey, KillCorrelation>? kct)
    {
        if (kct is null) return "(null)";
        if (kct.Count == 0) return "(empty)";
        return string.Join("; ", kct.Select(kv =>
            $"[({kv.Key.VarIndex},{kv.Key.Half})]=({string.Join(",", kv.Value.DataIds)},{kv.Value.FinalValue})"));
    }

    private static bool KctIsNullOrEmpty(IReadOnlyDictionary<NibbleKey, KillCorrelation>? kct)
        => kct is null || kct.Count == 0;

    // ─── Event builders ───────────────────────────────────────────────────────

    private static ObservationEvent ObsAt(string method, JsonElement? argument, JsonElement? value, DateTimeOffset at)
        => new() { RunId = "aaa", Data = new ObservationEvent.ObservationData { Method = method, Argument = argument, Value = value } };

    private static JsonElement InCombatValue(bool inCombat)
        => JsonSerializer.SerializeToElement(new { value = inCombat });

    private static JsonElement HostileTargetValue(uint baseId)
        => JsonSerializer.SerializeToElement(new { baseId, kind = "hostile" });

    private static JsonElement PlainNumberTargetValue(uint value)
        => JsonSerializer.SerializeToElement(value);

    private static JsonElement EnemyKilledValue(uint dataId)
        => JsonSerializer.SerializeToElement(new { dataId });

    private static JsonElement QuestVariablesValue(IReadOnlyList<int> variables)
        => JsonSerializer.SerializeToElement(variables);

    // Apply InCombat{true/false}
    private static void ApplyInCombat(SnapshotState state, bool value, DateTimeOffset at)
        => state.Apply(ObsAt("InCombat", null, InCombatValue(value), at));

    // Apply GetTarget{hostile, baseId}
    private static bool ApplyHostileTarget(SnapshotState state, uint baseId, DateTimeOffset at)
        => state.Apply(ObsAt("GetTarget", null, HostileTargetValue(baseId), at));

    // Apply GetQuestVariables
    private static void ApplyQuestVars(SnapshotState state, int[] vars, DateTimeOffset at)
        => state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u), QuestVariablesValue(vars), at));

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T1 — target during span + nibble bump attributes (mirror GwtT1)
    //
    // Given baseline [0,0,0,0,0,0].
    // When InCombat{true}; GetTarget{baseId:338,hostile}; GetQuestVariables [0,0x30,0,0,0,0].
    // Then KillCorrelatedTargets[(1,High)] == ([338], 3); (1,Low) absent.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T1_TargetDuringSpan_NibbleBump_Attributes()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero); // baseline

        // WHEN
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 338u, T_250);
        ApplyQuestVars(state, [0, 0x30, 0, 0, 0, 0], T_500); // V1 high 0→3

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);
        var highKey = new NibbleKey(1, NibbleHalf.High);
        Assert.True(kct!.TryGetValue(highKey, out var corr),
            $"Expected (1,High); got: {FormatKct(kct)}");
        Assert.Equal(3, corr.FinalValue);
        Assert.Contains(338u, corr.DataIds);
        Assert.False(kct.ContainsKey(new NibbleKey(1, NibbleHalf.Low)),
            $"(1,Low) must be absent (only high bumped); got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T2' — pre-combat target seeded (THE bug fix; mirror GwtT2')
    //
    // Given baseline.
    // When GetTarget{baseId:347,hostile} (NOT in combat); InCombat{true} (seeds 347);
    //      GetQuestVariables [0x03,0,...] (V0 low 0→3).
    // Then KillCorrelatedTargets[(0,Low)] == ([347], 3); (0,High) absent.
    // RED: pre-combat target dropped under old model; span empty → KCT null.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T2Prime_TargetBeforeInCombat_SeedsSpan()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero); // baseline

        // WHEN
        ApplyHostileTarget(state, 347u, T_250);  // NOT in combat yet
        ApplyInCombat(state, true, T_500);       // seeds 347 into span
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600); // V0 low 0→3

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);
        var lowKey = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(lowKey, out var corr),
            $"Pre-combat target must be seeded into span; got: {FormatKct(kct)}");
        Assert.Equal(3, corr.FinalValue);
        Assert.Contains(347u, corr.DataIds);
        Assert.False(kct.ContainsKey(new NibbleKey(0, NibbleHalf.High)),
            $"(0,High) must be absent; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T2b — seed + in-combat target swap (mirror GwtT2b)
    //
    // Given baseline.
    // When GetTarget{347} (pre-combat); InCombat{true} (seeds 347);
    //      GetTarget{348} (in-combat); GetQuestVariables [0x03,0,...].
    // Then (0,Low).DataIds == [347, 348] (sorted), FinalValue == 3.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T2b_SeedPlusMidFightTargetSwap_BothInDataIds()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);

        // WHEN
        ApplyHostileTarget(state, 347u, T_250);  // pre-combat, seeds at combat-start
        ApplyInCombat(state, true, T_500);       // seeds 347
        ApplyHostileTarget(state, 348u, T_600);  // in-combat swap
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_900);

        // THEN
        var kct = state.ToSnapshot(T_1100).KillCorrelatedTargets;
        Assert.NotNull(kct);
        var lowKey = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(lowKey, out var corr),
            $"Expected (0,Low); got: {FormatKct(kct)}");
        Assert.Equal(3, corr.FinalValue);
        var sorted = corr.DataIds.OrderBy(x => x).ToList();
        Assert.True(sorted.SequenceEqual(new[] { 347u, 348u }),
            $"DataIds must be [347,348]; got: [{string.Join(",", sorted)}]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T3' — no target before/in span → no attribution (replaces deleted O3')
    //
    // Given baseline.
    // When InCombat{true} (no prior GetTarget{hostile}); GetQuestVariables [0x01,0,...].
    // Then KillCorrelatedTargets is null/empty.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T3Prime_NoTargetInSpan_NoCorrelation()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);

        // WHEN
        ApplyInCombat(state, true, T_250); // no prior GetTarget{hostile}
        ApplyQuestVars(state, [0x01, 0, 0, 0, 0, 0], T_500);

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kct),
            $"No target in span must yield null/empty KCT; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T4 — successive bumps → highest value (mirror GwtT4)
    //
    // Given baseline; InCombat{true}; GetTarget{347} (in-combat).
    // When GetQuestVariables 0x01, then 0x02, then 0x03 (V0 low).
    // Then (0,Low) == ([347], 3).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T4_SuccessiveBumps_HighestValueWins()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 347u, T_250); // in-combat

        // WHEN
        ApplyQuestVars(state, [0x01, 0, 0, 0, 0, 0], T_500);
        ApplyQuestVars(state, [0x02, 0, 0, 0, 0, 0], T_600);
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_900);

        // THEN
        var kct = state.ToSnapshot(T_1100).KillCorrelatedTargets;
        Assert.NotNull(kct);
        var lowKey = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(lowKey, out var corr),
            $"Expected (0,Low); got: {FormatKct(kct)}");
        Assert.Equal(3, corr.FinalValue);
        Assert.Contains(347u, corr.DataIds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T5 — both nibbles in one write, single target (mirror GwtT5; rewrite of O4')
    //
    // Given baseline [0x02,0,...]; InCombat{true}; GetTarget{347}.
    // When GetQuestVariables [0x13,0,...] (low 2→3, high 0→1).
    // Then (0,Low)==([347],3) AND (0,High)==([347],1).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T5_BothNibblesOneWrite_SingleTarget_TwoKeys()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0x02, 0, 0, 0, 0, 0], T_Zero); // baseline at 0x02
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 347u, T_250);

        // WHEN
        ApplyQuestVars(state, [0x13, 0, 0, 0, 0, 0], T_500); // low 2→3, high 0→1

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);

        var lowKey  = new NibbleKey(0, NibbleHalf.Low);
        var highKey = new NibbleKey(0, NibbleHalf.High);

        Assert.True(kct!.TryGetValue(lowKey, out var corrLow),
            $"Expected (0,Low); got: {FormatKct(kct)}");
        Assert.Equal(3, corrLow.FinalValue);
        Assert.Contains(347u, corrLow.DataIds);

        Assert.True(kct.TryGetValue(highKey, out var corrHigh),
            $"Expected (0,High); got: {FormatKct(kct)}");
        Assert.Equal(1, corrHigh.FinalValue);
        Assert.Contains(347u, corrHigh.DataIds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T5-HIGH — high-nibble objective at V1 (mirror GwtT5 high variant; rewrite of O5')
    //
    // Given baseline [0,0x02,0,...]; InCombat{true}; GetTarget{338}.
    // When GetQuestVariables [0,0x32,0,...] (V1 high 0→3, V1 low 2→2 unchanged).
    // Then (1,High)==([338],3); (1,Low) absent.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T5High_HighNibbleAtV1_CorrectKey()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0x02, 0, 0, 0, 0], T_Zero); // baseline: V1 low=2
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 338u, T_250);

        // WHEN
        ApplyQuestVars(state, [0, 0x32, 0, 0, 0, 0], T_500); // V1 high 0→3; low 2→2 unchanged

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);

        var highKey = new NibbleKey(1, NibbleHalf.High);
        var lowKey  = new NibbleKey(1, NibbleHalf.Low);

        Assert.True(kct!.TryGetValue(highKey, out var corrHigh),
            $"Expected (1,High); got: {FormatKct(kct)}");
        Assert.Equal(3, corrHigh.FinalValue);
        Assert.Contains(338u, corrHigh.DataIds);

        Assert.False(kct.ContainsKey(lowKey),
            $"(1,Low) must be absent (low nibble 2→2 unchanged); got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T6 — mixed-pack span, both targets on every nibble (mirror GwtT6)
    //
    // Given baseline; InCombat{true}; GetTarget{347}; GetTarget{49} (swap).
    // When GetQuestVariables [0x03,0,...]; GetQuestVariables [0x03,0x03,...].
    // Then (0,Low) and (1,Low) both present, each DataIds==[49,347] (sorted).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T6_MixedPackSpan_BothTargetsOnEveryNibble()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 347u, T_250);
        ApplyHostileTarget(state, 49u, T_250); // swap

        // WHEN
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_500);
        ApplyQuestVars(state, [0x03, 0x03, 0, 0, 0, 0], T_600);

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);

        var key0Low = new NibbleKey(0, NibbleHalf.Low);
        var key1Low = new NibbleKey(1, NibbleHalf.Low);

        Assert.True(kct!.TryGetValue(key0Low, out var corr0),
            $"Expected (0,Low); got: {FormatKct(kct)}");
        Assert.True(kct.TryGetValue(key1Low, out var corr1),
            $"Expected (1,Low); got: {FormatKct(kct)}");

        var ids0 = corr0.DataIds.OrderBy(x => x).ToList();
        var ids1 = corr1.DataIds.OrderBy(x => x).ToList();
        Assert.True(ids0.SequenceEqual(new[] { 49u, 347u }),
            $"(0,Low) DataIds must be [49,347]; got: [{string.Join(",", ids0)}]");
        Assert.True(ids1.SequenceEqual(new[] { 49u, 347u }),
            $"(1,Low) DataIds must be [49,347]; got: [{string.Join(",", ids1)}]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T7' — ResetPendingKeyItemDeltas clears span; _lastBattleNpcTarget + baseline persist
    // (mirror GwtT7'; rewrite of O6')
    //
    // Part A: reset → span cleared.
    // Part B: InCombat{false/true} re-seeds from persisted 347; same value → no delta.
    // Part C: genuine 3→4 bump → correlates with re-seeded 347.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T7Prime_ResetClearsSpan_LastTargetAndBaselinePersist()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyHostileTarget(state, 347u, T_250);
        ApplyInCombat(state, true, T_500);
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600);

        // Precondition
        var lowKey = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(state.ToSnapshot(T_900).KillCorrelatedTargets?.ContainsKey(lowKey) == true,
            "Precondition: (0,Low) must be correlated before Reset");

        // PART A: reset → span cleared
        state.ResetPendingKeyItemDeltas();
        var kctA = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kctA),
            $"After Reset, KCT must be empty; got: {FormatKct(kctA)}");

        // PART B: new combat span; _lastBattleNpcTarget (347) persists → re-seeded;
        //         baseline preserved at 0x03 → re-emitting 0x03 yields no bump
        ApplyInCombat(state, false, T_900);
        ApplyInCombat(state, true, T_1100);  // re-seeds 347
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_1100); // 3→3 no delta

        var kctB = state.ToSnapshot(T_1100).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kctB),
            $"Re-emitting same value after Reset must not correlate; got: {FormatKct(kctB)}");

        // PART C: genuine 3→4 bump → correlates; 347 is re-seeded
        ApplyQuestVars(state, [0x04, 0, 0, 0, 0, 0], T_1100.AddMilliseconds(100));

        var kctC = state.ToSnapshot(T_1100.AddMilliseconds(200)).KillCorrelatedTargets;
        Assert.NotNull(kctC);
        Assert.True(kctC!.TryGetValue(lowKey, out var corr),
            $"Expected (0,Low) after genuine 3→4 bump; got: {FormatKct(kctC)}");
        Assert.Equal(4, corr.FinalValue);
        Assert.Contains(347u, corr.DataIds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T8 — InCombat false→true records combat start pos/zone (mirror GwtT8 / keep O7')
    //
    // Given GetPlayerZone{148}; GetPlayerPosition{10,0,20}.
    // When InCombat{true}.
    // Then CombatStartZone==148, CombatStartPosition==(10,0,20), InCombat==true.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T8_InCombatTransition_RecordsCombatStartZoneAndPosition()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        state.Apply(Obs("GetPlayerZone",     null, ZoneValue(148u),             offsetSeconds: 0.1));
        state.Apply(Obs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), offsetSeconds: 0.2));

        // WHEN
        var recognised = state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_250));

        // THEN
        var snapshot = state.ToSnapshot(T_500);
        Assert.True(recognised, "InCombat must be recognised (returns true)");
        Assert.True(snapshot.InCombat);
        Assert.Equal(148, snapshot.CombatStartZone);
        Assert.NotNull(snapshot.CombatStartPosition);
        Assert.Equal(10f, snapshot.CombatStartPosition!.Value.X, precision: 2);
        Assert.Equal(0f,  snapshot.CombatStartPosition.Value.Y,  precision: 2);
        Assert.Equal(20f, snapshot.CombatStartPosition.Value.Z,  precision: 2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T9 — resumed-quest first observation, no spurious attribution (mirror GwtT9)
    //
    // Given InCombat{true}; GetTarget{338}.
    // When GetQuestVariables [0,0x30,0,...] as FIRST observation (no prior baseline).
    // Then KillCorrelatedTargets is null (first obs = baseline only).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T9_ResumedQuest_FirstObservation_NoSpuriousAttribution()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyInCombat(state, true, T_Zero);
        ApplyHostileTarget(state, 338u, T_250);

        // WHEN — first GetQuestVariables ever (no prior baseline)
        ApplyQuestVars(state, [0, 0x30, 0, 0, 0, 0], T_500);

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kct),
            $"First obs must be baseline only; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T10' — new span re-seeds from current target, not stale (mirror GwtT10')
    //
    // Span A: GetTarget{347}; InCombat{true}; bump V0 low; InCombat{false}.
    // Between spans: GetTarget{338} (retarget).
    // Span B: InCombat{true} clears span A; seeds 338; bump V1 low.
    // Then (1,Low)==([338],3); (0,Low) absent; 347 not in (1,Low).DataIds.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T10Prime_NewSpan_ReseededFromCurrentTarget_NotStale()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);

        // Span A
        ApplyHostileTarget(state, 347u, T_250);
        ApplyInCombat(state, true, T_500);
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600);
        ApplyInCombat(state, false, T_900);

        // WHEN — retarget between spans; new span
        ApplyHostileTarget(state, 338u, T_1100);
        ApplyInCombat(state, true, T_1100); // clears span A; seeds 338
        // V1 low 0→3; baseline is [0x03,0,...] so V0 no change, V1 is new
        ApplyQuestVars(state, [0x03, 0x03, 0, 0, 0, 0], T_1100.AddMilliseconds(100));

        // THEN
        var kct = state.ToSnapshot(T_1100.AddMilliseconds(200)).KillCorrelatedTargets;
        Assert.NotNull(kct);

        var key1Low = new NibbleKey(1, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(key1Low, out var corr),
            $"Expected (1,Low) from span B; got: {FormatKct(kct)}");
        Assert.Equal(3, corr.FinalValue);
        Assert.Contains(338u, corr.DataIds);

        // Span A's nibble must be gone
        Assert.False(kct.ContainsKey(new NibbleKey(0, NibbleHalf.Low)),
            $"(0,Low) from span A must be cleared; got: {FormatKct(kct)}");
        // Stale target 347 must not appear
        Assert.False(corr.DataIds.Contains(347u),
            $"347 from span A must not appear in span B; got: [{string.Join(",", corr.DataIds)}]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T-A — hostile-object GetTarget is recognised and mutates span (D1)
    //
    // When InCombat{true}; Apply(GetTarget{baseId:347,kind:hostile}).
    // Then Apply returns true; GetQuestVariables bump → (0,Low).DataIds contains 347.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T_A_HostileObjectGetTarget_IsRecognisedAndMutates()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyInCombat(state, true, T_250);

        // WHEN
        var r = ApplyHostileTarget(state, 347u, T_500);
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600);

        // THEN
        Assert.True(r, "GetTarget{hostile} must return true (recognised)");
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.NotNull(kct);
        var lowKey = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(lowKey, out var corr),
            $"Hostile target must attribute bump; got: {FormatKct(kct)}");
        Assert.Contains(347u, corr.DataIds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T-B — plain-number GetTarget is unrecognised, no combat mutation (D1)
    //
    // When InCombat{true}; Apply(GetTarget value:1000927 [plain number]);
    //      GetQuestVariables [0x03,0,...].
    // Then Apply returns false; KillCorrelatedTargets is null/empty.
    //      LastNpcInteracted is unchanged (null).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T_B_PlainNumberGetTarget_IsUnrecognised_NoMutation()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyInCombat(state, true, T_250);

        // WHEN
        var r = state.Apply(ObsAt("GetTarget", null, PlainNumberTargetValue(1000927u), T_500));
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600);

        // THEN
        Assert.False(r, "Plain-number GetTarget must return false (unrecognised)");
        var snapshot = state.ToSnapshot(T_900);
        Assert.True(KctIsNullOrEmpty(snapshot.KillCorrelatedTargets),
            $"Plain-number GetTarget must not attribute; got: {FormatKct(snapshot.KillCorrelatedTargets)}");
        Assert.Null(snapshot.LastNpcInteracted);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T-NOOP — EnemyKilled is a recognised no-op (D2)
    //
    // When InCombat{true}; Apply(EnemyKilled{dataId:347}) (no GetTarget{hostile});
    //      GetQuestVariables [0x03,0,...].
    // Then Apply returns true; KillCorrelatedTargets is null/empty.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T_Noop_EnemyKilled_IsRecognisedNoOp_NoCorrelation()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero);
        ApplyInCombat(state, true, T_250);

        // WHEN
        var r = state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_500));
        ApplyQuestVars(state, [0x03, 0, 0, 0, 0, 0], T_600);

        // THEN
        Assert.True(r, "EnemyKilled must return true (recognised no-op)");
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kct),
            $"EnemyKilled alone must not attribute; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T-BASELINE — first GetQuestVariables is baseline only (keep; renamed)
    //
    // When InCombat{true}; first GetQuestVariables [0x02,0,...] (no prior baseline);
    //      later same [0x02,0,...].
    // Then no bump; KCT null even with a target seeded.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T_Baseline_FirstGetQuestVariables_IsBaselineOnly_NoSpuriousBump()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyInCombat(state, true, T_Zero);
        ApplyHostileTarget(state, 338u, T_250);

        // WHEN — first obs ever, non-zero, no prior baseline
        ApplyQuestVars(state, [0x02, 0, 0, 0, 0, 0], T_500); // baseline-only
        ApplyQuestVars(state, [0x02, 0, 0, 0, 0, 0], T_600); // no delta

        // THEN
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kct),
            $"First obs must be baseline-only; no bump; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-T-WRONGQUEST — wrong-quest GetQuestVariables ignored (mirror O2')
    //
    // When InCombat{true}; GetTarget{347}; GetQuestVariables for quest 12345 [0x01,...].
    // Then Apply returns true; KCT null/empty.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_T_WrongQuest_GetQuestVariables_Ignored()
    {
        // GIVEN
        var state = new SnapshotState(CombatQuest);
        ApplyQuestVars(state, [0, 0, 0, 0, 0, 0], T_Zero); // baseline for correct quest
        ApplyInCombat(state, true, T_250);
        ApplyHostileTarget(state, 347u, T_250);

        // WHEN
        var wrongQuestArg = JsonSerializer.SerializeToElement(new { value = 12345u });
        var r = state.Apply(ObsAt("GetQuestVariables", wrongQuestArg,
            QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), T_500));

        // THEN
        Assert.True(r, "GetQuestVariables must be recognised even for wrong quest ID");
        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;
        Assert.True(KctIsNullOrEmpty(kct),
            $"Wrong-quest GetQuestVariables must not attribute; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KEPT UNCHANGED: Sequence advance does NOT correlate (O8')
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_SequenceAdvance_DoesNotCorrelate_GwtO8Prime()
    {
        var state = new SnapshotState(CombatQuest);
        ApplyInCombat(state, true, T_Zero);

        // No GetTarget{hostile} — confirm no target in span
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(348u), T_500));

        var seqValue = JsonSerializer.SerializeToElement(3);
        var recognised = state.Apply(ObsAt("GetQuestSequence",
            QuestIdArg(65847u), seqValue, T_500.AddMilliseconds(50)));

        var kct = state.ToSnapshot(T_900).KillCorrelatedTargets;

        Assert.True(recognised, "GetQuestSequence must remain recognised (returns true)");
        Assert.True(KctIsNullOrEmpty(kct),
            $"GetQuestSequence advance must produce no NibbleKey correlation; got: {FormatKct(kct)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KEPT UNCHANGED: ToSnapshot projects InCombat field
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToSnapshot_ProjectsInCombatField()
    {
        var state = new SnapshotState(CombatQuest);
        Assert.False(state.ToSnapshot(T_Zero).InCombat, "Fresh state must have InCombat == false");

        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
        Assert.True(state.ToSnapshot(T_250).InCombat, "After InCombat true → snapshot.InCombat true");

        state.Apply(ObsAt("InCombat", null, InCombatValue(false), T_500));
        Assert.False(state.ToSnapshot(T_600).InCombat, "After InCombat false → snapshot.InCombat false");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KEPT UNCHANGED: bare-bool InCombat value shape accepted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_InCombat_BareBoolShape_IsRecognised()
    {
        var state = new SnapshotState(CombatQuest);
        var bareTrueValue = JsonSerializer.SerializeToElement(true);
        var recognised = state.Apply(ObsAt("InCombat", null, bareTrueValue, T_Zero));

        Assert.True(recognised, "InCombat with bare bool must be recognised");
        Assert.True(state.ToSnapshot(T_250).InCombat, "Bare true must set InCombat == true");
    }
}
