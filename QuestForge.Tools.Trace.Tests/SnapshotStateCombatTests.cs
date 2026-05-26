using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for Slice B: SnapshotState nibble-level bidirectional combat correlation.
/// GWT-O1'..O8' from COMBAT_NIBBLE_DETECTION_PLAN.md §5.5.
///
/// These tests REPLACE the byte-level GWT-O1..O6 tests (deleted). Byte tests asserted:
///   - int-keyed KillCorrelatedTargets[varIndex]
///   - questVariable (whole-byte) expects
///   - GetQuestSequence (SequenceVariableIndex -1) correlation
///   - kill-before-bump ordering only
/// All of the above are REMOVED per the plan's "What is REMOVED" table and Settled decisions.
///
/// Tests fail until Builder rewrites SnapshotState to:
///   - use NibbleKey-keyed _killCorrelatedTargets
///   - add _recentNibbleBumps buffer (bidirectional correlation)
///   - emit bumps on strict nibble increment (low: (n&0x0F)>(p&0x0F), high: (n>>4)>(p>>4))
///   - remove GetQuestSequence correlation block
///   - clear _recentNibbleBumps in ResetPendingKeyItemDeltas
///   - project NibbleKey-keyed dictionary in BuildKillCorrelatedTargets/ToSnapshot
/// </summary>
public sealed class SnapshotStateCombatTests
{
    private static readonly QuestId CombatQuest = new(65847u);

    // Timestamps placed at deliberate offsets to exercise the 500 ms window.
    private static readonly DateTimeOffset T_Zero = T0;
    private static readonly DateTimeOffset T_250  = T0.AddMilliseconds(250);
    private static readonly DateTimeOffset T_500  = T0.AddMilliseconds(500);
    private static readonly DateTimeOffset T_600  = T0.AddMilliseconds(600);
    private static readonly DateTimeOffset T_900  = T0.AddMilliseconds(900);
    private static readonly DateTimeOffset T_1100 = T0.AddMilliseconds(1100);

    // -------------------------------------------------------------------------
    // Formatting helpers
    // -------------------------------------------------------------------------

    private static string FormatKct(IReadOnlyDictionary<NibbleKey, KillCorrelation>? kct)
    {
        if (kct is null) return "(null)";
        return string.Join("; ", kct.Select(kv =>
            $"[({kv.Key.VarIndex},{kv.Key.Half})]=({string.Join(",", kv.Value.DataIds)},{kv.Value.FinalValue})"));
    }

    // -------------------------------------------------------------------------
    // Event builders (millisecond-precise)
    // -------------------------------------------------------------------------

    private static ObservationEvent ObsAt(string method, JsonElement? argument, JsonElement? value, DateTimeOffset at)
        => new(RunId: "aaa", Method: method, Argument: argument, Value: value, At: at);

    private static JsonElement InCombatValue(bool inCombat)
        => JsonSerializer.SerializeToElement(new { value = inCombat });

    private static JsonElement EnemyKilledValue(uint dataId)
        => JsonSerializer.SerializeToElement(new { dataId });

    private static JsonElement QuestVariablesValue(IReadOnlyList<int> variables)
        => JsonSerializer.SerializeToElement(variables);

    // -------------------------------------------------------------------------
    // GWT-O1': bump-before-kill correlates (ev.At = production order)
    //
    // The FIRST GetQuestVariables (at t=0) is the baseline-only observation.
    // Per Slice A: baseline-first means no correlation on the first obs even if kills
    // are buffered — the engine first-obs sets the baseline without producing a bump.
    // Rounds 1-3: GetQuestVariables (bump) THEN EnemyKilled — the real in-game order.
    // Bidirectional: the kill arrives AFTER its bump; the kill's correlate-on-arrival
    // pass looks backward into _recentNibbleBumps and matches.
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_BumpBeforeKill_CorrelatesWithinWindow_GwtO1Prime()
    {
        /*
         * RED: Will fail until Builder adds bidirectional nibble correlation in SnapshotState.
         *
         * CONTRACT (GWT-O1'):
         *   1. Apply InCombat{true} at t=0.
         *   2. Apply GetQuestVariables [0,0,…] at t=0 → BASELINE ONLY (first obs sets baseline,
         *      no bump produced even though prevVars==null). No kill is correlated.
         *   3. Round 1 at t=250: GetQuestVariables [0x01,0,…] THEN EnemyKilled{347}.
         *      Bump (V0,Low): 0→1; kill 347 in window [−250,750] → correlated.
         *   4. Round 2 at t=500: GetQuestVariables [0x02,…] THEN EnemyKilled{347}.
         *   5. Round 3 at t=900: GetQuestVariables [0x03,…] THEN EnemyKilled{347}.
         *   => KillCorrelatedTargets[NibbleKey(0, Low)] == KillCorrelation([347], 3).
         */

        var state = new SnapshotState(CombatQuest);

        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // Baseline-only first observation (no prior baseline → sets baseline, no bump)
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0, 0, 0, 0, 0, 0]), T_Zero));

        // Round 1: bump first, then kill (in-game production order)
        var r_bump1 = state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), T_250));
        var r_kill1 = state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));

        // Round 2
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), T_500));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_500));

        // Round 3
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x03, 0, 0, 0, 0, 0]), T_900));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_900));

        var snapshot = state.ToSnapshot(T_900.AddMilliseconds(50));

        Assert.True(r_bump1, "GetQuestVariables must return true (recognised)");
        Assert.True(r_kill1, "EnemyKilled must return true (recognised)");

        var kct = snapshot.KillCorrelatedTargets;
        Assert.NotNull(kct);

        var key = new NibbleKey(0, NibbleHalf.Low);
        Assert.True(kct!.TryGetValue(key, out var corr),
            $"Expected KillCorrelatedTargets[({0},Low)]; got: {FormatKct(kct)}");
        Assert.Contains(347u, corr.DataIds);
        Assert.Equal(3, corr.FinalValue);
    }

    // -------------------------------------------------------------------------
    // GWT-O2': Wrong-quest GetQuestVariables ignored — no correlation
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetQuestVariables_WrongQuest_NoCorrelation_GwtO2Prime()
    {
        /*
         * RED: Will fail until Builder filters quest-ID on nibble correlation.
         *
         * CONTRACT (GWT-O2'): Kill at t=250; GetQuestVariables for quest 12345 (wrong).
         * Then no NibbleKey entry. Apply returns true (recognised method).
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));

        var recognised = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(12345u), // wrong quest
            QuestVariablesValue([0x01, 0, 0, 0, 0, 0]),
            T_250));

        var snapshot = state.ToSnapshot(T_500);

        Assert.True(recognised, "GetQuestVariables must be recognised even for wrong quest ID");

        var kct = snapshot.KillCorrelatedTargets;
        Assert.True(kct is null || kct.Count == 0,
            $"Wrong-quest GetQuestVariables must produce no NibbleKey correlation; got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // GWT-O3': Kill outside the symmetric window is NOT correlated (both directions)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_KillOutsideSymmetricWindow_NotCorrelated_GwtO3Prime()
    {
        /*
         * RED: Will fail until Builder enforces |kill.At - bump.At| <= 500 ms on both directions.
         *
         * CONTRACT (GWT-O3'): Two sub-cases, both directions.
         *
         * Direction A (kill-before-bump, gap 600 ms > 500 ms):
         *   Kill 999 at t=0; GetQuestVariables bump at t=600ms.
         *   => No correlation (kill is 600ms before bump).
         *
         * Direction B (bump-before-kill, gap 600 ms > 500 ms):
         *   GetQuestVariables bump at t=0; Kill 999 at t=600ms.
         *   => No correlation (kill is 600ms after bump).
         */

        // Direction A: kill arrives 600ms before bump
        {
            var state = new SnapshotState(CombatQuest);
            state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
            // Establish baseline first
            state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
                QuestVariablesValue([0, 0, 0, 0, 0, 0]), T_Zero));

            state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(999u), T_Zero)); // kill at t=0
            state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), T_600)); // bump at t=600ms

            var snapshot = state.ToSnapshot(T_900);
            var kct = snapshot.KillCorrelatedTargets;
            var key = new NibbleKey(0, NibbleHalf.Low);
            bool noEntry = kct is null || !kct.TryGetValue(key, out var c) || !c.DataIds.Contains(999u);
            Assert.True(noEntry,
                $"Direction A: kill at t=0 must NOT correlate to bump at t=600ms; got: {FormatKct(kct)}");
        }

        // Direction B: bump arrives 600ms before kill
        {
            var state = new SnapshotState(CombatQuest);
            state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
            // Establish baseline first
            state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
                QuestVariablesValue([0, 0, 0, 0, 0, 0]), T_Zero));

            state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), T_Zero)); // bump at t=0
            state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(999u), T_600)); // kill at t=600ms

            var snapshot = state.ToSnapshot(T_900);
            var kct = snapshot.KillCorrelatedTargets;
            var key = new NibbleKey(0, NibbleHalf.Low);
            bool noEntry = kct is null || !kct.TryGetValue(key, out var c) || !c.DataIds.Contains(999u);
            Assert.True(noEntry,
                $"Direction B: bump at t=0 must NOT correlate to kill at t=600ms; got: {FormatKct(kct)}");
        }
    }

    // -------------------------------------------------------------------------
    // GWT-O4': Both nibbles in one write → two NibbleKey entries
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_BothNibblesInOneWrite_TwoNibbleKeyEntries_GwtO4Prime()
    {
        /*
         * RED: Will fail until Builder detects both nibble increments independently.
         *
         * CONTRACT (GWT-O4'): Baseline [0x02,…]; then [0x13,…] arrives (low 2→3 up, high 0→1 up)
         * with kill 347 in window.
         * Then KillCorrelatedTargets contains BOTH:
         *   NibbleKey(0, Low)  → ([347], Final=3)
         *   NibbleKey(0, High) → ([347], Final=1)
         *
         * Note: baseline is established via a first [0x02,…] obs before the bump-kill pair.
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // Establish baseline at [0x02, …]
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), T_Zero));

        // Kill arrives first (at t=250), then both-nibble write (at t=250, same ms)
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x13, 0, 0, 0, 0, 0]), T_250)); // low 2→3, high 0→1

        var snapshot = state.ToSnapshot(T_500);
        var kct = snapshot.KillCorrelatedTargets;
        Assert.NotNull(kct);

        var keyLow  = new NibbleKey(0, NibbleHalf.Low);
        var keyHigh = new NibbleKey(0, NibbleHalf.High);

        Assert.True(kct!.TryGetValue(keyLow, out var corrLow),
            $"Expected (0,Low) entry; got: {FormatKct(kct)}");
        Assert.Contains(347u, corrLow.DataIds);
        Assert.Equal(3, corrLow.FinalValue);

        Assert.True(kct.TryGetValue(keyHigh, out var corrHigh),
            $"Expected (0,High) entry; got: {FormatKct(kct)}");
        Assert.Contains(347u, corrHigh.DataIds);
        Assert.Equal(1, corrHigh.FinalValue);
    }

    // -------------------------------------------------------------------------
    // GWT-O5': High-nibble objective (variable index 1, high nibble)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_HighNibbleObjective_CorrectNibbleKeyEntry_GwtO5Prime()
    {
        /*
         * RED: Will fail until Builder handles high-nibble detection at arbitrary VarIndex.
         *
         * CONTRACT (GWT-O5'): Baseline [0, 0x02, …]; then [0, 0x32, …] arrives
         * (V1 low nibble: 2→2 no change; V1 high nibble: 0→3 up).
         * Kill 338 in window.
         * => KillCorrelatedTargets[NibbleKey(1, High)] == ([338], 3)
         * => No (1, Low) entry (low didn't change).
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // Baseline: V1 = 0x02
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0, 0x02, 0, 0, 0, 0]), T_Zero));

        // Bump-before-kill: bump then kill
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0, 0x32, 0, 0, 0, 0]), T_250)); // V1 high: 0→3
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(338u), T_250));

        var snapshot = state.ToSnapshot(T_500);
        var kct = snapshot.KillCorrelatedTargets;
        Assert.NotNull(kct);

        var keyHigh = new NibbleKey(1, NibbleHalf.High);
        var keyLow  = new NibbleKey(1, NibbleHalf.Low);

        Assert.True(kct!.TryGetValue(keyHigh, out var corrHigh),
            $"Expected (1,High) entry; got: {FormatKct(kct)}");
        Assert.Contains(338u, corrHigh.DataIds);
        Assert.Equal(3, corrHigh.FinalValue);

        // V1 low nibble did NOT change (2→2), so no (1,Low) entry
        Assert.False(kct.TryGetValue(keyLow, out _),
            $"(1,Low) must NOT be present (low nibble 2→2 unchanged); got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // GWT-O6': ResetPendingKeyItemDeltas clears both buffers + correlation, keeps baseline
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetPendingKeyItemDeltas_ClearsBothBuffers_KeepsBaseline_GwtO6Prime()
    {
        /*
         * RED: Will fail until Builder clears _recentNibbleBumps in ResetPendingKeyItemDeltas
         * and preserves _prevQuestVariables.
         *
         * CONTRACT (GWT-O6'):
         *   1. Establish correlated (0,Low)=([347],3) via a 0x00→0x03 window.
         *   2. ResetPendingKeyItemDeltas() → KillCorrelatedTargets empty.
         *   3. Re-emit [0x03,…] with an in-window kill → NO correlation
         *      (baseline preserved at 0x03; low 3→3 is NOT a bump).
         *   4. Emit [0x04,…] (low 3→4 is a real increment) with an in-window kill
         *      → correlates, Final=4.
         *
         * Specifically tests that the bump buffer is also cleared (otherwise the
         * pre-reset bumps would erroneously match post-reset kills).
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // Establish baseline at [0x00]
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x00, 0, 0, 0, 0, 0]), T_Zero));

        // Build correlated window: bump first (t=250), kill same (t=250)
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x03, 0, 0, 0, 0, 0]), T_250));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));

        var before = state.ToSnapshot(T_500);
        Assert.NotNull(before.KillCorrelatedTargets);

        // Reset
        state.ResetPendingKeyItemDeltas();

        var afterReset = state.ToSnapshot(T_500);
        var kctReset = afterReset.KillCorrelatedTargets;
        Assert.True(kctReset is null || kctReset.Count == 0,
            $"After Reset, KillCorrelatedTargets must be empty; got: {FormatKct(kctReset)}");

        // Re-emit same value (0x03 → 0x03: no delta) + in-window kill → no correlation
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_600));
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x03, 0, 0, 0, 0, 0]), T_600));

        var afterSame = state.ToSnapshot(T_900);
        var kctSame = afterSame.KillCorrelatedTargets;
        Assert.True(kctSame is null || kctSame.Count == 0,
            $"Same-value re-emit after Reset must not correlate; got: {FormatKct(kctSame)}");

        // Real increment 0x03→0x04 + in-window kill → correlates
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x04, 0, 0, 0, 0, 0]), T_900));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_900));

        var afterIncrement = state.ToSnapshot(T_1100);
        var kctIncrement = afterIncrement.KillCorrelatedTargets;
        var keyLow = new NibbleKey(0, NibbleHalf.Low);
        KillCorrelation corrInc = default!;
        Assert.True(kctIncrement != null && kctIncrement.TryGetValue(keyLow, out corrInc),
            $"Real increment 3→4 after Reset must correlate; got: {FormatKct(kctIncrement)}");
        Assert.Equal(4, corrInc.FinalValue);
        Assert.Contains(347u, corrInc.DataIds);
    }

    // -------------------------------------------------------------------------
    // GWT-O7': InCombat false→true records CombatStartZone and CombatStartPosition
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InCombatTransition_RecordsCombatStartZoneAndPosition_GwtO7Prime()
    {
        /*
         * RED: Will fail until Builder records position/zone on InCombat false→true.
         *
         * CONTRACT (GWT-O7'): GetPlayerZone{value:148}, GetPlayerPosition{x:10,y:0,z:20},
         *   then InCombat{value:true}.
         * Then ToSnapshot.CombatStartZone == 148 and CombatStartPosition == (10,0,20).
         */

        var state = new SnapshotState(CombatQuest);

        state.Apply(Obs("GetPlayerZone",     null, ZoneValue(148u),            offsetSeconds: 0.1));
        state.Apply(Obs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), offsetSeconds: 0.2));

        var recognised = state.Apply(ObsAt("InCombat", null, InCombatValue(true),
            T_Zero.AddMilliseconds(300)));

        var snapshot = state.ToSnapshot(T_500);

        Assert.True(recognised, "InCombat must be a recognised method (returns true)");
        Assert.Equal(148, snapshot.CombatStartZone);
        Assert.NotNull(snapshot.CombatStartPosition);
        Assert.Equal(10f, snapshot.CombatStartPosition!.Value.X, precision: 2);
        Assert.Equal(0f,  snapshot.CombatStartPosition.Value.Y,  precision: 2);
        Assert.Equal(20f, snapshot.CombatStartPosition.Value.Z,  precision: 2);
    }

    // -------------------------------------------------------------------------
    // GWT-O8': Sequence advance does NOT correlate (removed block)
    //
    // Asserts that the old GetQuestSequence correlation block (SequenceVariableIndex = -1)
    // is GONE. Kills in window + a GetQuestSequence advance must produce no NibbleKey entry.
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_SequenceAdvance_DoesNotCorrelate_GwtO8Prime()
    {
        /*
         * RED: Will fail until Builder removes the GetQuestSequence correlation block.
         *
         * CONTRACT (GWT-O8'): Kills at t=250/t=500; then GetQuestSequence arg=65847 value=3
         * (sequence advances 0→3). No GetQuestVariables bump in window.
         * Then KillCorrelatedTargets is null/empty — the old SequenceVariableIndex=-1 entry
         * must not appear.
         *
         * Also verifies Apply(GetQuestSequence) still returns true (recognised method).
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(348u), T_500));

        // Sequence advances — must NOT produce a correlation entry
        var seqValue = JsonSerializer.SerializeToElement(3);
        var recognised = state.Apply(ObsAt("GetQuestSequence",
            QuestIdArg(65847u), seqValue, T_500.AddMilliseconds(50)));

        var snapshot = state.ToSnapshot(T_900);
        var kct = snapshot.KillCorrelatedTargets;

        Assert.True(recognised, "GetQuestSequence must remain a recognised method (returns true)");
        Assert.True(kct is null || kct.Count == 0,
            $"GetQuestSequence advance must NOT produce any NibbleKey correlation; got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // Baseline-only first observation: pre-existing non-zero variable + no kill
    // does NOT produce spurious correlation (covers D7 / GWT-L8' offline parity)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_FirstGetQuestVariables_NonZeroNoKill_NoSpuriousCorrelation()
    {
        /*
         * RED: Will fail until Builder treats first GetQuestVariables as baseline-only.
         *
         * CONTRACT (offline D7 parity): InCombat true at t=0; no kills buffered.
         * First GetQuestVariables [0x02,…] at t=250 — baseline set, no bump event.
         * Then a kill at t=400 (no variable change follows).
         * Then GetQuestVariables [0x02,…] unchanged at t=500 — no delta.
         * => KillCorrelatedTargets null/empty (the kill has no matching nibble bump).
         */

        var state = new SnapshotState(CombatQuest);
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // First-ever GetQuestVariables: already non-zero, no prior baseline
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), T_250));

        // Kill after baseline set
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(999u),
            T_Zero.AddMilliseconds(400)));

        // Same value again — no delta
        state.Apply(ObsAt("GetQuestVariables", QuestIdArg(65847u),
            QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), T_500));

        var snapshot = state.ToSnapshot(T_900);
        var kct = snapshot.KillCorrelatedTargets;
        Assert.True(kct is null || kct.Count == 0,
            $"First GetQuestVariables must be baseline-only; kill 999 must not correlate; got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // Positive: InCombat bool projection in ToSnapshot (unchanged behaviour)
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Positive: bare-bool InCombat value shape accepted (unchanged behaviour)
    // -------------------------------------------------------------------------

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
