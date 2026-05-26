using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for Slice 3: SnapshotState combat-correlation additions.
/// GWT-O1..O6 from COMBAT_AUTHORING_DETECTION_PLAN.md §4.5.
/// All tests fail until Builder adds InCombat / EnemyKilled / GetQuestVariables Apply cases,
/// projects combat fields in ToSnapshot, and extends ResetPendingKeyItemDeltas.
/// </summary>
public sealed class SnapshotStateCombatTests
{
    private static readonly QuestId CombatQuest = new(65847u);

    // Timestamps placed at deliberate offsets to exercise the 500 ms window.
    private static readonly DateTimeOffset T_Zero   = T0;
    private static readonly DateTimeOffset T_250    = T0.AddMilliseconds(250);
    private static readonly DateTimeOffset T_500    = T0.AddMilliseconds(500);
    private static readonly DateTimeOffset T_600    = T0.AddMilliseconds(600);
    private static readonly DateTimeOffset T_900    = T0.AddMilliseconds(900);

    private static string FormatKct(IReadOnlyDictionary<int, KillCorrelation>? kct)
    {
        if (kct is null) return "(null)";
        return string.Join("; ", kct.Select(kv =>
            $"[{kv.Key}]=({string.Join(",", kv.Value.DataIds)},{kv.Value.FinalValue})"));
    }

    // -------------------------------------------------------------------------
    // Helpers to build ObservationEvents with precise timestamps
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
    // GWT-O1: EnemyKilled + GetQuestVariables correlate within 500 ms window
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InCombatEnemyKilledAndGetQuestVariables_CorrelateWithinWindow()
    {
        /*
         * RED: Will fail until Builder adds InCombat / EnemyKilled / GetQuestVariables
         * Apply cases with kill-to-variable correlation in SnapshotState.
         *
         * CONTRACT (GWT-O1): Given SnapshotState(65847);
         *   Apply InCombat{value:true} at t=0;
         *   Apply EnemyKilled{dataId:347} at t=250ms;
         *   Apply GetQuestVariables arg=65847 value=[1,0,0,0,0,0] at t=250ms;
         *   Apply EnemyKilled{dataId:347} at t=500ms;
         *   Apply GetQuestVariables arg=65847 value=[2,0,...] at t=500ms;
         *   Apply EnemyKilled{dataId:347} at t=900ms;
         *   Apply GetQuestVariables arg=65847 value=[3,0,...] at t=900ms;
         * Then ToSnapshot(t).KillCorrelatedTargets[0] == KillCorrelation([347], 3).
         * Each Apply(InCombat/EnemyKilled/GetQuestVariables) returns true.
         */

        var state = new SnapshotState(CombatQuest);

        // InCombat true at t=0
        var r1 = state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));

        // Kill + variable bump round 1 at t=250
        var r2 = state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));
        var r3 = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([1, 0, 0, 0, 0, 0]),
            T_250));

        // Kill + variable bump round 2 at t=500
        var r4 = state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_500));
        var r5 = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([2, 0, 0, 0, 0, 0]),
            T_500));

        // Kill + variable bump round 3 at t=900
        var r6 = state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_900));
        var r7 = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([3, 0, 0, 0, 0, 0]),
            T_900));

        var snapshot = state.ToSnapshot(T_900.AddMilliseconds(50));

        Assert.True(r1, "InCombat Apply should return true (recognised method)");
        Assert.True(r2, "EnemyKilled Apply should return true (recognised method)");
        Assert.True(r3, "GetQuestVariables Apply should return true (recognised method)");
        Assert.True(r4, "EnemyKilled Apply (round 2) should return true");
        Assert.True(r5, "GetQuestVariables Apply (round 2) should return true");
        Assert.True(r6, "EnemyKilled Apply (round 3) should return true");
        Assert.True(r7, "GetQuestVariables Apply (round 3) should return true");

        Assert.NotNull(snapshot.KillCorrelatedTargets);
        Assert.True(snapshot.KillCorrelatedTargets!.TryGetValue(0, out var corr0),
            $"Expected KillCorrelatedTargets[0]; got: {FormatKct(snapshot.KillCorrelatedTargets)}");
        Assert.Contains(347u, corr0.DataIds);
        Assert.Equal(3, corr0.FinalValue);
    }

    // -------------------------------------------------------------------------
    // GWT-O2: GetQuestVariables for WRONG quest does not correlate
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetQuestVariables_WrongQuest_NoCorrelation_ButReturnsTrue()
    {
        /*
         * RED: Will fail until Builder filters GetQuestVariables by quest ID.
         *
         * CONTRACT (GWT-O2): Given SnapshotState(65847);
         *   Kill at t=250ms;
         *   GetQuestVariables for quest 12345 (wrong) at t=250ms;
         * Then KillCorrelatedTargets is null/empty. Apply returns true (recognised).
         */

        var state = new SnapshotState(CombatQuest);

        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));

        var recognised = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(12345u), // wrong quest
            QuestVariablesValue([1, 0, 0, 0, 0, 0]),
            T_250));

        var snapshot = state.ToSnapshot(T_500);

        Assert.True(recognised, "GetQuestVariables must be recognised (return true) even for wrong quest ID");
        var kct = snapshot.KillCorrelatedTargets;
        Assert.True(kct is null || kct.Count == 0,
            $"Wrong-quest GetQuestVariables must produce no correlation; got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // GWT-O3: EnemyKilled outside 500 ms window is NOT correlated
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_EnemyKilledOutsideWindow_NotCorrelated()
    {
        /*
         * RED: Will fail until Builder evicts kills older than 500 ms.
         *
         * CONTRACT (GWT-O3): Given a kill at t=0 and a variable bump at t=600ms
         *   (gap = 600ms > 500ms window);
         * Then KillCorrelatedTargets is null/empty — the 999 data-id never appears.
         */

        var state = new SnapshotState(CombatQuest);

        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(999u), T_Zero)); // at t=0
        state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([1, 0, 0, 0, 0, 0]),
            T_600)); // bump at t=600ms — kill is 600ms before, outside the window

        var snapshot = state.ToSnapshot(T_900);

        var kct = snapshot.KillCorrelatedTargets;
        Assert.True(kct is null || kct.Count == 0 ||
                    (kct.TryGetValue(0, out var c) && !c.DataIds.Contains(999u)),
            $"Kill at t=0 must not be correlated to bump at t=600ms (window=500ms); got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // GWT-O4: InCombat false→true records CombatStartZone and CombatStartPosition
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InCombatTransition_RecordsCombatStartZoneAndPosition()
    {
        /*
         * RED: Will fail until Builder records position/zone on InCombat false→true.
         *
         * CONTRACT (GWT-O4): Given GetPlayerZone{value:148} then GetPlayerPosition{x:10,y:0,z:20}
         *   then InCombat{value:true};
         * Then ToSnapshot.CombatStartZone == 148 and CombatStartPosition == (10, 0, 20).
         */

        var state = new SnapshotState(CombatQuest);

        state.Apply(Obs("GetPlayerZone",     null, ZoneValue(148u),           offsetSeconds: 0.1));
        state.Apply(Obs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), offsetSeconds: 0.2));

        var recognised = state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero.AddMilliseconds(300)));

        var snapshot = state.ToSnapshot(T_500);

        Assert.True(recognised, "InCombat must be a recognised method (returns true)");
        Assert.Equal(148, snapshot.CombatStartZone);
        Assert.NotNull(snapshot.CombatStartPosition);
        Assert.Equal(10f, snapshot.CombatStartPosition!.Value.X, precision: 2);
        Assert.Equal(0f,  snapshot.CombatStartPosition.Value.Y,  precision: 2);
        Assert.Equal(20f, snapshot.CombatStartPosition.Value.Z,  precision: 2);
    }

    // -------------------------------------------------------------------------
    // GWT-O5: ResetPendingKeyItemDeltas clears combat window, preserves variable baseline
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetPendingKeyItemDeltas_ClearsCombatWindow_KeepsVariableBaseline()
    {
        /*
         * RED: Will fail until Builder extends ResetPendingKeyItemDeltas to also clear
         * _recentKills and _killCorrelatedTargets (but NOT _prevQuestVariables).
         *
         * CONTRACT (GWT-O5 / mirror of GWT-L6):
         *   1. Build up a correlated window (kill + variable bump → KillCorrelatedTargets[0]={347},3).
         *   2. Call ResetPendingKeyItemDeltas() — KillCorrelatedTargets must be cleared.
         *   3. Add a new kill in-window, then apply GetQuestVariables with the SAME value [3,...].
         *   4. Because baseline is preserved at 3, the new delta is 0 → no new correlation.
         *      (Proves baseline survived reset and prevents phantom bumps.)
         */

        var state = new SnapshotState(CombatQuest);

        // Step 1: establish correlation
        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_250));
        state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([3, 0, 0, 0, 0, 0]),
            T_250));

        // Confirm correlated before reset
        var beforeReset = state.ToSnapshot(T_500);
        Assert.NotNull(beforeReset.KillCorrelatedTargets);

        // Step 2: reset (simulates end-of-decision window)
        state.ResetPendingKeyItemDeltas();

        // Confirm cleared after reset
        var afterReset = state.ToSnapshot(T_500);
        var kctAfter = afterReset.KillCorrelatedTargets;
        Assert.True(kctAfter is null || kctAfter.Count == 0,
            $"After ResetPendingKeyItemDeltas, KillCorrelatedTargets must be empty; got: {FormatKct(kctAfter)}");

        // Step 3: new kill + same variable value (no real bump — baseline at 3)
        state.Apply(ObsAt("EnemyKilled", null, EnemyKilledValue(347u), T_600));
        state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([3, 0, 0, 0, 0, 0]), // same value → delta == 0
            T_600));

        var afterSame = state.ToSnapshot(T_900);
        var kctSame = afterSame.KillCorrelatedTargets;
        Assert.True(kctSame is null || kctSame.Count == 0,
            $"Same-value GetQuestVariables after reset must not produce new correlation; got: {FormatKct(kctSame)}");
    }

    // -------------------------------------------------------------------------
    // GWT-O6 (offline L8 mirror): First-ever GetQuestVariables with non-zero value
    //         and no preceding kill must NOT spuriously correlate (baseline-only)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_FirstGetQuestVariables_NonZeroNoKill_NoSpuriousCorrelation()
    {
        /*
         * RED: Will fail until Builder treats the first GetQuestVariables as baseline-only
         * when no kill precedes it within the window.
         *
         * CONTRACT (GWT-O6 / offline mirror of GWT-L8):
         *   Given SnapshotState(65847) — _prevQuestVariables is unset (null);
         *   When the first GetQuestVariables arrives with value=[2,0,0,0,0,0] and NO kill
         *     was observed in the 500ms window before it;
         *   Then KillCorrelatedTargets is null/empty — pre-existing non-zero variable
         *     must NOT be treated as a "bump" from nothing.
         *
         * This covers the resumed-quest scenario: the player already completed sub-objective 2
         * before the trace began; the first observation just sets the baseline.
         */

        var state = new SnapshotState(CombatQuest);

        // First-ever GetQuestVariables: variable already at 2, no kill in window
        var recognised = state.Apply(ObsAt("GetQuestVariables",
            QuestIdArg(65847u),
            QuestVariablesValue([2, 0, 0, 0, 0, 0]),
            T_250));

        var snapshot = state.ToSnapshot(T_500);

        Assert.True(recognised, "GetQuestVariables must be recognised even when it is the first observation");

        var kct = snapshot.KillCorrelatedTargets;
        Assert.True(kct is null || kct.Count == 0,
            $"First GetQuestVariables with non-zero value and no preceding kill must not correlate; got: {FormatKct(kct)}");
    }

    // -------------------------------------------------------------------------
    // Additional positive: InCombat bool projection in ToSnapshot
    // -------------------------------------------------------------------------

    [Fact]
    public void ToSnapshot_ProjectsInCombatField()
    {
        /*
         * RED: Will fail until Builder projects InCombat into ToSnapshot output.
         *
         * CONTRACT: After Apply(InCombat{value:true}), snapshot.InCombat == true.
         *           After Apply(InCombat{value:false}) subsequently, snapshot.InCombat == false.
         */

        var state = new SnapshotState(CombatQuest);
        var snapshotBefore = state.ToSnapshot(T_Zero);
        Assert.False(snapshotBefore.InCombat, "Fresh SnapshotState must have InCombat == false");

        state.Apply(ObsAt("InCombat", null, InCombatValue(true), T_Zero));
        var snapshotTrue = state.ToSnapshot(T_250);
        Assert.True(snapshotTrue.InCombat, "After InCombat true, snapshot.InCombat must be true");

        state.Apply(ObsAt("InCombat", null, InCombatValue(false), T_500));
        var snapshotFalse = state.ToSnapshot(T_600);
        Assert.False(snapshotFalse.InCombat, "After InCombat false, snapshot.InCombat must be false");
    }

    // -------------------------------------------------------------------------
    // Additional positive: bare-bool InCombat value shape (no "value" wrapper)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InCombat_BareBoolShape_IsRecognised()
    {
        /*
         * RED: Will fail until Builder handles the bare-bool value shape for InCombat.
         *
         * CONTRACT: InCombat value may arrive as a bare JSON boolean (true) rather than
         *   {"value":true}. Both shapes must be accepted.
         */

        var state = new SnapshotState(CombatQuest);

        // Bare bool (not object-wrapped)
        var bareTrueValue = JsonSerializer.SerializeToElement(true);
        var recognised = state.Apply(ObsAt("InCombat", null, bareTrueValue, T_Zero));

        Assert.True(recognised, "InCombat with bare bool value must be recognised");
        var snapshot = state.ToSnapshot(T_250);
        Assert.True(snapshot.InCombat, "InCombat with bare true must set InCombat == true");
    }
}
