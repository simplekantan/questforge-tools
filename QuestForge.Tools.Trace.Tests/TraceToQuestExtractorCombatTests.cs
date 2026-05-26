using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for Slice B: TraceToQuestExtractor nibble combat extraction.
/// GWT-E1'..E4' from COMBAT_NIBBLE_DETECTION_PLAN.md §5.6.
///
/// These tests REPLACE the byte-level GWT-E1..E3 tests (deleted). Byte tests asserted:
///   - "questVariable(65847, 0) >= 3" (whole-byte expect) → REMOVED (Settled #1)
///   - No baseline-first ordering in the trace → REMOVED (Slice A baseline contract)
/// The new tests assert:
///   - "questVariableLow(65847, 0) >= 3" (nibble expect from StepInferenceEngine Rule 2.2)
///   - Baseline-first GetQuestVariables before the kill+bump rounds
///   - Bidirectional correlation: bump-before-kill order (real in-game order)
///   - GWT-E4': parity — same expect string as StepInferenceEngine.Infer produces directly
///
/// Tests fail until Builder rewrites SnapshotState.Apply (nibble correlation, NibbleKey)
/// and StepInferenceEngine Rule 2.2 (nibble expect, dominant selection).
/// TraceToQuestExtractor control flow is UNCHANGED (verified: Builder does not need to
/// touch TraceToQuestExtractor.cs beyond confirming the combat branch still fires).
/// </summary>
public sealed class TraceToQuestExtractorCombatTests
{
    private const uint QuestId65847 = 65847u;

    private static readonly DateTimeOffset BaseTime = T0;

    // -------------------------------------------------------------------------
    // Event builders
    // -------------------------------------------------------------------------

    private static ObservationEvent ObsMs(string method, JsonElement? argument, JsonElement? value, double ms)
        => new(RunId: "aaa", Method: method, Argument: argument, Value: value,
               At: BaseTime.AddMilliseconds(ms));

    private static JsonElement InCombatValue(bool v)
        => JsonSerializer.SerializeToElement(new { value = v });

    private static JsonElement EnemyKilledValue(uint dataId)
        => JsonSerializer.SerializeToElement(new { dataId });

    private static JsonElement QuestVariablesValue(IReadOnlyList<int> variables)
        => JsonSerializer.SerializeToElement(variables);

    // -------------------------------------------------------------------------
    // Trace builders
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build a synthetic combat trace for quest 65847, enemy 347, V0 low nibble 0→3.
    ///
    /// Timeline (ms from T0):
    ///   0      RunStart 65847
    ///   100    GetPlayerZone 148
    ///   200    GetPlayerPosition (10,0,20)
    ///   300    InCombat true
    ///   310    GetQuestVariables [0x00,…]   ← BASELINE-ONLY (first obs, no bump)
    ///   500    GetQuestVariables [0x01,…]   ← bump (low 0→1)
    ///   500    EnemyKilled 347              ← kill; bidirectional: matches bump@500
    ///   800    GetQuestVariables [0x02,…]   ← bump (low 1→2)
    ///   800    EnemyKilled 347
    ///  1100    GetQuestVariables [0x03,…]   ← bump (low 2→3)
    ///  1100    EnemyKilled 347
    ///  1200    Decision (actionType)
    ///  [optional submitted/completed]
    ///  2000    RunEnd
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildNibbleCombatTrace(
        string decisionActionType = "wait",
        bool includeSubmitted = false)
    {
        var events = new List<TraceEvent>
        {
            new RunStartEvent("aaa", QuestId65847, 1u, BaseTime),

            // Pre-roll: zone + position
            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),

            // InCombat starts
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // BASELINE-ONLY first GetQuestVariables (Slice A contract: no bump from null→0)
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x00, 0, 0, 0, 0, 0]), 310),

            // Round 1: bump-before-kill at t=500ms
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), 500),
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 500),

            // Round 2: bump-before-kill at t=800ms (gap from prev 300ms < 500ms window)
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), 800),
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 800),

            // Round 3: bump-before-kill at t=1100ms
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x03, 0, 0, 0, 0, 0]), 1100),
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 1100),

            // Decision in the combat window
            new DecisionEvent("aaa", null, decisionActionType, BaseTime.AddMilliseconds(1200)),
        };

        if (includeSubmitted)
        {
            events.Add(new ActionSubmittedEvent(
                RunId: "aaa",
                ActionType: decisionActionType,
                Parameters: null,
                At: BaseTime.AddMilliseconds(1250)));
            events.Add(new ActionCompletedEvent(
                RunId: "aaa",
                ActionType: decisionActionType,
                Outcome: "ok",
                At: BaseTime.AddMilliseconds(1300)));
        }

        events.Add(new RunEndEvent("aaa", "done", BaseTime.AddMilliseconds(2000)));
        return events;
    }

    /// <summary>
    /// Builds a trace with kills where the only variable bump is 700ms after the kill
    /// (gap > 500ms window) → no correlation → no CombatStep.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildUncorrelatedKillsTrace()
    {
        return
        [
            new RunStartEvent("aaa", QuestId65847, 1u, BaseTime),

            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // Baseline first
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x00, 0, 0, 0, 0, 0]), 310),

            // Kill at t=500ms
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 500),

            // Variable bump at t=1200ms — gap from kill = 700ms > 500ms → no correlation
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), 1200),

            // Navigate decision so the extractor has something to process
            new DecisionEvent("aaa", null, "navigate", BaseTime.AddMilliseconds(1300)),
            new ActionSubmittedEvent("aaa", "Navigate", NavParams(10f, 0f, 20f, 148),
                BaseTime.AddMilliseconds(1400)),
            new ActionCompletedEvent("aaa", "Navigate", "Arrived",
                BaseTime.AddMilliseconds(1500)),

            new RunEndEvent("aaa", "done", BaseTime.AddMilliseconds(2000))
        ];
    }

    // -------------------------------------------------------------------------
    // GWT-E1': End-to-end nibble combat extraction produces CombatStep with nibble expect
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_NibbleCombatWindow_ProducesCombatStep_WithNibbleExpect_GwtE1Prime()
    {
        /*
         * RED: Will fail until Builder rewrites SnapshotState (nibble correlation) and
         * StepInferenceEngine Rule 2.2 (nibble expect).
         *
         * CONTRACT (GWT-E1'): Trace for quest 65847, enemy 347, baseline-first then
         * three bump-before-kill rounds; a 'wait' decision in the window; RunEnd.
         * Then Extract yields a CombatStep with:
         *   - KillEnemyDataIds == [347]
         *   - Spawn == OverworldEnemies
         *   - Expect.Predicate == "questVariableLow(65847, 0) >= 3"  ← nibble, NOT byte
         *   - Todos contains a string mentioning "Spawn" review
         *
         * Key: "questVariableLow" not "questVariable" — this is the decisive assertion
         * that distinguishes nibble (correct) from byte (wrong, deleted).
         */

        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var combatStep = allSteps.OfType<CombatStep>().FirstOrDefault();
        Assert.NotNull(combatStep);

        Assert.Contains(347u, combatStep!.KillEnemyDataIds);
        Assert.Equal(CombatSpawn.OverworldEnemies, combatStep.Spawn);

        var expect = Assert.IsType<PredicateExpect>(combatStep.Expect);
        // Nibble expect — "questVariableLow", NOT the deleted "questVariable"
        Assert.Equal("questVariableLow(65847, 0) >= 3", expect.Predicate);

        // Spawn-review TODO must be present
        Assert.Contains(draft.Todos, t =>
            t.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("combat", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // GWT-E2': Combat 'wait' decision window is NOT skipped
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatWindowWithWaitDecision_StillEmitsCombatStep_GwtE2Prime()
    {
        /*
         * RED: Will fail until Builder's combat branch fires before the wait-skip guard.
         *
         * CONTRACT (GWT-E2'): The combat window's only decision has ActionType=="wait".
         * The isSkippableAction check must be deferred until after inference returns "combat".
         * The CombatStep is still emitted.
         * (This is the same guard as old GWT-E2 but now asserts nibble correlation fires.)
         */

        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var combatStep = allSteps.OfType<CombatStep>().FirstOrDefault();
        Assert.NotNull(combatStep);
        Assert.Contains(347u, combatStep!.KillEnemyDataIds);

        // Assert nibble expect (not byte)
        var expect = Assert.IsType<PredicateExpect>(combatStep.Expect);
        Assert.StartsWith("questVariableLow", expect.Predicate);
    }

    // -------------------------------------------------------------------------
    // GWT-E3': Uncorrelated kills (no in-window nibble bump) produce no CombatStep
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_KillsWithNoInWindowNibbleBump_NoCombatStep_GwtE3Prime()
    {
        /*
         * CONTRACT (GWT-E3'): Kill at t=500ms; variable bump at t=1200ms (gap 700ms > 500ms).
         * No in-window nibble bump → KillCorrelatedTargets empty → inference falls through
         * to Rule 3 (navigate) → no CombatStep emitted.
         */

        var events = BuildUncorrelatedKillsTrace();
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        Assert.DoesNotContain(allSteps, s => s is CombatStep);
    }

    // -------------------------------------------------------------------------
    // GWT-E4': Parity — extractor's CombatStep.Expect.Predicate matches the string
    // StepInferenceEngine.Infer produces for equivalent live snapshots
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatStep_ExpectPredicate_MatchesStepInferenceEngine_GwtE4Prime()
    {
        /*
         * RED: Will fail until Builder produces the nibble expect via StepInferenceEngine Rule 2.2.
         *
         * CONTRACT (GWT-E4' / D8 parity lock):
         * The extractor routes combat through StepInferenceEngine.Infer (TraceToQuestExtractor.cs:190)
         * and StepFactory.Build (TraceToQuestExtractor.cs:208). Both paths share the same
         * StepInferenceEngine code (ProjectReference). This test pins the parity by:
         *   a) Running Extract on the GWT-E1' trace → get CombatStep.Expect.Predicate.
         *   b) Constructing an equivalent GameStateSnapshot directly with NibbleKey(0,Low)=([347],3)
         *      and calling StepInferenceEngine.Infer to get the expected predicate string.
         *   c) Asserting a == b.
         *
         * Since both a and b go through the same StepInferenceEngine, this is a consistency
         * test: it catches any divergence if the extractor were to hand-roll the expect string.
         */

        // Part a: run the extractor
        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();
        Assert.NotNull(combatStep);
        var extractedPredicateExpect = Assert.IsType<PredicateExpect>(combatStep!.Expect);
        var extractedPredicate = extractedPredicateExpect.Predicate;

        // Part b: call StepInferenceEngine directly with the equivalent after-snapshot
        var activeQuest = new QuestId(QuestId65847);
        var kct = new Dictionary<NibbleKey, KillCorrelation>
        {
            [new NibbleKey(0, NibbleHalf.Low)] = new KillCorrelation([347u], 3)
        };
        var afterSnapshot = new GameStateSnapshot(
            CapturedAt:        BaseTime.AddMilliseconds(2000),
            Zone:              new ZoneId(148u),
            Position:          new WorldPosition(10f, 0f, 20f),
            ActiveQuest:       activeQuest,
            QuestSequence:     0,
            QuestFlags:        0u,
            QuestAccepted:     true,
            QuestCompleted:    false,
            LastNpcInteracted: null,
            LastNpcPosition:   null,
            LastDialoguePrompt: null,
            LastDialogueAnswer: null,
            InventoryHash:     0u,
            LastAttuned:       null)
        {
            InCombat = true,
            KillCorrelatedTargets = kct,
            CombatStartZone = 148,
            CombatStartPosition = new WorldPosition(10f, 0f, 20f)
        };

        var beforeSnapshot = afterSnapshot with
        {
            CapturedAt = BaseTime.AddMilliseconds(300),
            KillCorrelatedTargets = null,
            InCombat = false
        };

        var inference = new StepInferenceEngine().Infer(beforeSnapshot, afterSnapshot);
        Assert.Equal("combat", inference.StepType);
        var liveExpect = inference.SuggestedExpect;

        // Parity assertion
        Assert.Equal(liveExpect, extractedPredicate);
        // Belt-and-braces: both must be the nibble form
        Assert.Equal("questVariableLow(65847, 0) >= 3", extractedPredicate);
    }

    // -------------------------------------------------------------------------
    // CombatStep Location.Zone comes from CombatStartZone (unchanged behaviour, nibble trace)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatStep_LocationZone_FromCombatStartZone()
    {
        /*
         * CONTRACT: InCombat transitions in zone 148 → CombatStep.Location.Zone == 148.
         * Unchanged from old test; re-asserted with the nibble trace builder.
         */

        var events = BuildNibbleCombatTrace(decisionActionType: "navigate", includeSubmitted: true);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();

        Assert.NotNull(combatStep);
        Assert.NotNull(combatStep!.Location);
        Assert.Equal(148, combatStep.Location!.Zone);
    }

    // -------------------------------------------------------------------------
    // CombatStep Zone/RequiredZone parity with StepFactory (unchanged behaviour, nibble trace)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatStep_PopulatesZoneAndRequiredZone_MatchingLiveStepFactory()
    {
        /*
         * CONTRACT: Zone and RequiredZone are populated as "148" (string form from StepFactory).
         * Unchanged from old test; re-asserted with the nibble trace builder.
         */

        var events = BuildNibbleCombatTrace(decisionActionType: "navigate", includeSubmitted: true);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();

        Assert.NotNull(combatStep);
        Assert.Equal("148", combatStep!.Zone);
        Assert.Equal("148", combatStep.RequiredZone);
    }
}
