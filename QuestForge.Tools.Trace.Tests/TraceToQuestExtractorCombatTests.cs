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
/// RED-phase tests for Slice 3: TraceToQuestExtractor combat-step extraction.
/// GWT-E1..E3 from COMBAT_AUTHORING_DETECTION_PLAN.md §4.6.
/// All tests fail until Builder adds the combat branch and fixes the wait-skip guard.
/// </summary>
public sealed class TraceToQuestExtractorCombatTests
{
    private const uint QuestId65847 = 65847u;

    // -------------------------------------------------------------------------
    // Shared helpers for building combat trace events at precise millisecond offsets
    // -------------------------------------------------------------------------

    // T0 is defined in TraceTestHelpers as a static field; we derive from it.
    private static readonly DateTimeOffset BaseTime = T0;

    private static ObservationEvent ObsMs(string method, JsonElement? argument, JsonElement? value, double ms)
        => new(RunId: "aaa", Method: method, Argument: argument, Value: value,
               At: BaseTime.AddMilliseconds(ms));

    private static JsonElement InCombatValue(bool v)
        => JsonSerializer.SerializeToElement(new { value = v });

    private static JsonElement EnemyKilledValue(uint dataId)
        => JsonSerializer.SerializeToElement(new { dataId });

    private static JsonElement QuestVariablesValue(IReadOnlyList<int> variables)
        => JsonSerializer.SerializeToElement(variables);

    /// <summary>
    /// Build a synthetic combat window with three kill+bump pairs around a decision.
    /// Quest 65847; enemy dataId 347; variable index 0 rises 0→3.
    ///
    /// Timeline (all ms from T0):
    ///   0      RunStart
    ///   100    GetPlayerZone 148
    ///   200    GetPlayerPosition (10,0,20)
    ///   300    InCombat true
    ///   500    EnemyKilled 347
    ///   500    GetQuestVariables [1,0,0,0,0,0]
    ///   800    EnemyKilled 347
    ///   800    GetQuestVariables [2,0,0,0,0,0]
    ///   1100   EnemyKilled 347
    ///   1100   GetQuestVariables [3,0,0,0,0,0]
    ///   [decision + action in between]
    ///   2000   RunEnd
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildCombatTrace(
        string decisionActionType = "wait",
        bool includeSubmitted = false)
    {
        var events = new List<TraceEvent>
        {
            new RunStartEvent("aaa", QuestId65847, 1u, BaseTime),

            // Pre-roll zone + position
            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),

            // InCombat starts
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // Kill + variable bump round 1 at t=500ms
            ObsMs("EnemyKilled",       null,               EnemyKilledValue(347u),              500),
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847), QuestVariablesValue([1,0,0,0,0,0]), 500),

            // Kill + variable bump round 2 at t=800ms (gap from prev = 300ms < 500ms)
            ObsMs("EnemyKilled",       null,               EnemyKilledValue(347u),              800),
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847), QuestVariablesValue([2,0,0,0,0,0]), 800),

            // Kill + variable bump round 3 at t=1100ms (gap from prev = 300ms < 500ms)
            ObsMs("EnemyKilled",       null,               EnemyKilledValue(347u),             1100),
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847), QuestVariablesValue([3,0,0,0,0,0]), 1100),

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
    /// Build a trace with enemy kills but NO variable bump — should NOT produce a CombatStep.
    /// The kill is purely out-of-window relative to any variable change.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildUncorrelatedKillsTrace()
    {
        return
        [
            new RunStartEvent("aaa", QuestId65847, 1u, BaseTime),

            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // Kill at t=500ms
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 500),

            // Variable bump at t=1200ms — kill is 700ms before, outside 500ms window
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847), QuestVariablesValue([1,0,0,0,0,0]), 1200),

            // A navigate decision so the extractor has something to process
            new DecisionEvent("aaa", null, "navigate", BaseTime.AddMilliseconds(1300)),
            new ActionSubmittedEvent("aaa", "Navigate", NavParams(10f, 0f, 20f, 148), BaseTime.AddMilliseconds(1400)),
            new ActionCompletedEvent("aaa", "Navigate", "Arrived", BaseTime.AddMilliseconds(1500)),

            new RunEndEvent("aaa", "done", BaseTime.AddMilliseconds(2000))
        ];
    }

    // -------------------------------------------------------------------------
    // GWT-E1: End-to-end combat extraction produces CombatStep
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatWindow_ProducesCombatStep_WithCorrectDataIdsAndExpect()
    {
        /*
         * RED: Will fail until Builder adds the combat branch to TraceToQuestExtractor
         * and SnapshotState has the GetQuestVariables / EnemyKilled / InCombat Apply cases.
         *
         * CONTRACT (GWT-E1): Given a synthetic trace for quest 65847 with:
         *   - InCombat true at t=300ms
         *   - Three (EnemyKilled 347 + GetQuestVariables [k,0,...]) pairs at t=500/800/1100ms
         *   - A decision+action (navigate) in the window
         * Then Extract yields a CombatStep with:
         *   - KillEnemyDataIds == [347]
         *   - Spawn == OverworldEnemies
         *   - Expect.Predicate == "questVariable(65847, 0) >= 3"
         * AND Todos contains a string mentioning "Spawn" review.
         */

        var events = BuildCombatTrace(decisionActionType: "navigate", includeSubmitted: true);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var combatStep = allSteps.OfType<CombatStep>().FirstOrDefault();
        Assert.NotNull(combatStep);

        Assert.Contains(347u, combatStep!.KillEnemyDataIds);
        Assert.Equal(CombatSpawn.OverworldEnemies, combatStep.Spawn);

        var expect = Assert.IsType<PredicateExpect>(combatStep.Expect);
        Assert.Equal("questVariable(65847, 0) >= 3", expect.Predicate);

        // Spawn-review TODO must be present
        Assert.Contains(draft.Todos, t =>
            t.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("combat", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // GWT-E2: Combat window with only a 'wait' decision is NOT skipped
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatWindowWithWaitDecision_StillEmitsCombatStep()
    {
        /*
         * RED: Will fail until Builder fixes the wait-skip guard to not drop windows
         * whose post-window snapshot has non-empty KillCorrelatedTargets.
         *
         * CONTRACT (GWT-E2): Given the combat window's only decision has ActionType=="wait";
         * Then the extractor still emits the CombatStep.
         * (The existing guard `actionTypeLower == "wait" → continue` must be bypassed when
         * inference returns StepType=="combat".)
         */

        // Build a trace where the decision is "wait" (no submitted/completed events needed —
        // the extractor must infer combat even without an ActionSubmittedEvent)
        var events = BuildCombatTrace(decisionActionType: "wait", includeSubmitted: false);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var combatStep = allSteps.OfType<CombatStep>().FirstOrDefault();
        Assert.NotNull(combatStep);
        Assert.Contains(347u, combatStep!.KillEnemyDataIds);
    }

    // -------------------------------------------------------------------------
    // GWT-E3: Kills with no in-window variable bump produce NO CombatStep
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_KillsWithNoInWindowVariableBump_NoCombatStep()
    {
        /*
         * RED: Currently "passes accidentally" because the extractor drops all combat windows.
         * After Builder adds the combat branch, this must STILL pass (no spurious CombatStep).
         *
         * CONTRACT (GWT-E3): Given kills where the only variable bump is 700ms after the kill
         * (outside the 500ms window); Then no CombatStep is emitted.
         * The navigate step drives the window; combat is absent.
         */

        var events = BuildUncorrelatedKillsTrace();
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        Assert.DoesNotContain(allSteps, s => s is CombatStep);
    }

    // -------------------------------------------------------------------------
    // Additional: CombatStep Location.Zone comes from CombatStartZone
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatStep_LocationZone_FromCombatStartZone()
    {
        /*
         * RED: Will fail until Builder passes CombatStartZone into CombatStep.Location.
         *
         * CONTRACT: When InCombat transitions in zone 148, the resulting CombatStep
         * must have Location.Zone == 148.
         */

        var events = BuildCombatTrace(decisionActionType: "navigate", includeSubmitted: true);
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
    // Live-vs-offline parity: offline CombatStep must populate Zone / RequiredZone
    // exactly as the live StepFactory does, so a trace replayed offline reconstructs
    // the same step the live authoring path records. Guards the Reviewer's parity fix
    // (extractor now builds combat steps through the shared StepFactory).
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_CombatStep_PopulatesZoneAndRequiredZone_MatchingLiveStepFactory()
    {
        var events = BuildCombatTrace(decisionActionType: "navigate", includeSubmitted: true);
        var extractor = new TraceToQuestExtractor();

        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();

        Assert.NotNull(combatStep);
        // StepFactory derives Zone/RequiredZone from the after-snapshot zone (148) as a string.
        Assert.Equal("148", combatStep!.Zone);
        Assert.Equal("148", combatStep.RequiredZone);
    }
}
