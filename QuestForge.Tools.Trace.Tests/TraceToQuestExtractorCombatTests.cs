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
/// RED-phase tests for PR-B: TraceToQuestExtractor end-to-end combat extraction
/// rebuilt on the target-based span model.
///
/// REWRITTEN: E1'..E4' trace builders now use GetTarget{hostile} instead of EnemyKilled
/// as the attribution driver. EnemyKilled may still appear in the stream as a no-op.
/// E3' reframed as "no target in span → no CombatStep" (not "outside window").
/// E4' is the LIVE==OFFLINE parity lock (GWT-O-E4').
///
/// TraceToQuestExtractor.cs is UNCHANGED — it already routes combat through
/// StepInferenceEngine.Infer. Once SnapshotState.BuildKillCorrelatedTargets is
/// target-based, the correct expect/step-id come out for free.
/// </summary>
public sealed class TraceToQuestExtractorCombatTests
{
    private const uint QuestId65847 = 65847u;

    private static readonly DateTimeOffset BaseTime = T0;

    // ─── Event builders ───────────────────────────────────────────────────────

    private static ObservationEvent ObsMs(string method, JsonElement? argument, JsonElement? value, double ms)
        => new() { RunId = "aaa", Data = new ObservationEvent.ObservationData { Method = method, Argument = argument, Value = value } };

    private static JsonElement InCombatValue(bool v)
        => JsonSerializer.SerializeToElement(new { value = v });

    private static JsonElement HostileTargetValue(uint baseId)
        => JsonSerializer.SerializeToElement(new { baseId, kind = "hostile" });

    private static JsonElement EnemyKilledValue(uint dataId)
        => JsonSerializer.SerializeToElement(new { dataId });

    private static JsonElement QuestVariablesValue(IReadOnlyList<int> variables)
        => JsonSerializer.SerializeToElement(variables);

    // ─── Trace builders ───────────────────────────────────────────────────────

    /// <summary>
    /// Standard target-based combat trace for quest 65847, enemy 347, V0 low nibble 0→3.
    ///
    /// Timeline (ms from T0):
    ///   0      RunStart 65847
    ///   100    GetPlayerZone 148
    ///   200    GetPlayerPosition (10,0,20)
    ///   250    GetTarget{baseId:347,hostile}   ← PRE-COMBAT target (seeds span at combat-start)
    ///   300    InCombat{true}                  ← seeds span with 347
    ///   310    GetQuestVariables [0x00,…]       ← BASELINE-ONLY (first obs)
    ///   500    GetQuestVariables [0x01,…]       ← bump (low 0→1), in-combat span
    ///   600    GetQuestVariables [0x02,…]       ← bump (low 1→2)
    ///  1000    GetQuestVariables [0x03,…]       ← bump (low 2→3)
    ///  1100    EnemyKilled{347}                 ← no-op (recognised but no attribution)
    ///  1200    Decision (actionType)
    ///  [optional submitted/completed]
    ///  2000    RunEnd
    ///
    /// The pre-combat GetTarget is the ONLY GetTarget — mirrors real observer dedup semantics.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildNibbleCombatTrace(
        string decisionActionType = "wait",
        bool includeSubmitted = false)
    {
        var events = new List<TraceEvent>
        {
            new RunStartEvent { RunId = "aaa", Data = new RunStartEvent.RunStartData { QuestId = QuestId65847 } },

            // Zone + position
            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),

            // Pre-combat target — single forward before InCombat (dedup semantics)
            ObsMs("GetTarget", null, HostileTargetValue(347u), 250),

            // InCombat seeds span with 347
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // Baseline-only first GetQuestVariables
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x00, 0, 0, 0, 0, 0]), 310),

            // Three bumps in-combat (all gated → in span)
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), 500),
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x02, 0, 0, 0, 0, 0]), 600),
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x03, 0, 0, 0, 0, 0]), 1000),

            // EnemyKilled as recognised no-op (real traces emit it; it must not break correlation)
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 1100),

            // Decision
            new DecisionEvent { RunId = "aaa", Data = new DecisionEvent.DecisionData { StepId = null, ActionType = decisionActionType } },
        };

        if (includeSubmitted)
        {
            events.Add(new ActionSubmittedEvent
            {
                RunId = "aaa",
                Data = new ActionSubmittedEvent.ActionSubmittedData { ActionType = decisionActionType, Parameters = null }
            });
            events.Add(new ActionCompletedEvent
            {
                RunId = "aaa",
                Data = new ActionCompletedEvent.ActionCompletedData { ActionType = decisionActionType, Outcome = "ok" }
            });
        }

        events.Add(new RunEndEvent { RunId = "aaa", Data = new RunEndEvent.RunEndData { Outcome = "done" } });
        return events;
    }

    /// <summary>
    /// Trace with EnemyKilled but NO GetTarget{hostile}:
    /// span has no target → KillCorrelatedTargets null → inference falls through to navigate.
    /// A navigate decision is included so the extractor has something to emit.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildNoTargetCombatTrace()
    {
        return
        [
            new RunStartEvent { RunId = "aaa", Data = new RunStartEvent.RunStartData { QuestId = QuestId65847 } },

            ObsMs("GetPlayerZone",     null, ZoneValue(148u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 200),

            // InCombat{true} — no prior GetTarget{hostile}, so _lastBattleNpcTarget is null
            ObsMs("InCombat", null, InCombatValue(true), 300),

            // Baseline
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x00, 0, 0, 0, 0, 0]), 310),

            // Kill arrives (no-op under target model)
            ObsMs("EnemyKilled", null, EnemyKilledValue(347u), 500),

            // Nibble bumps IN combat — but no target in span
            ObsMs("GetQuestVariables", QuestIdArg(QuestId65847),
                QuestVariablesValue([0x01, 0, 0, 0, 0, 0]), 600),

            // Navigate decision so extractor has an action boundary to process
            new DecisionEvent { RunId = "aaa", Data = new DecisionEvent.DecisionData { StepId = null, ActionType = "navigate" } },
            new ActionSubmittedEvent { RunId = "aaa", Data = new ActionSubmittedEvent.ActionSubmittedData { ActionType = "Navigate", Parameters = NavParams(10f, 0f, 20f, 148) } },
            new ActionCompletedEvent { RunId = "aaa", Data = new ActionCompletedEvent.ActionCompletedData { ActionType = "Navigate", Outcome = "Arrived" } },

            new RunEndEvent { RunId = "aaa", Data = new RunEndEvent.RunEndData { Outcome = "done" } }
        ];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-E1' — end-to-end target combat → CombatStep with nibble expect
    //
    // Trace: target pre-combat; InCombat{true}; baseline; three in-combat bumps;
    //        a 'wait' decision; RunEnd.
    // Then CombatStep with KillEnemyDataIds contains 347; Spawn==OverworldEnemies;
    //      Expect.Predicate == "questVariableLow(65847, 0) >= 3"; Spawn-review TODO present.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_E1Prime_TargetCombatTrace_ProducesCombatStep_WithNibbleExpect()
    {
        // GIVEN
        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var combatStep = allSteps.OfType<CombatStep>().FirstOrDefault();
        Assert.NotNull(combatStep);

        Assert.Contains(347u, combatStep!.KillEnemyDataIds);
        Assert.Equal(CombatSpawn.OverworldEnemies, combatStep.Spawn);

        var expect = Assert.IsType<PredicateExpect>(combatStep.Expect);
        Assert.Equal("questVariableLow(65847, 0) >= 3", expect.Predicate);

        Assert.Contains(draft.Todos, t =>
            t.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("combat", StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-E2' — combat 'wait' decision still emits CombatStep (not skipped)
    //
    // Same trace, decision 'wait'. The combat branch fires BEFORE the wait-skip guard.
    // Then CombatStep emitted; Expect starts with "questVariableLow".
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_E2Prime_WaitDecision_CombatSpanStillEmitsCombatStep()
    {
        // GIVEN
        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences.SelectMany(s => s.Steps)
            .OfType<CombatStep>().FirstOrDefault();

        Assert.NotNull(combatStep);
        Assert.Contains(347u, combatStep!.KillEnemyDataIds);
        var expect = Assert.IsType<PredicateExpect>(combatStep.Expect);
        Assert.StartsWith("questVariableLow", expect.Predicate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-E3' — no target in span → no CombatStep (replaces O3' "outside window")
    //
    // Trace: InCombat{true}; baseline; EnemyKilled{347} (no GetTarget{hostile});
    //        nibble bump in-combat; navigate decision + submitted/completed; RunEnd.
    // Then no CombatStep (KCT null → inference falls through to navigate/Rule 3).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_E3Prime_NoTargetInSpan_NoCombatStep()
    {
        // GIVEN
        var events = BuildNoTargetCombatTrace();

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        Assert.DoesNotContain(allSteps, s => s is CombatStep);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-E4' — LIVE==OFFLINE parity lock (mirror E4')
    //
    // Part a: run extractor on the E1' trace → CombatStep.Expect.Predicate + Id.
    // Part b: build equivalent GameStateSnapshot directly with
    //         KillCorrelatedTargets={(0,Low):([347],3)}, InCombat, zone 148;
    //         call StepInferenceEngine.Infer(before, after).
    // Then inference.StepType=="combat"; extracted predicate == inference.SuggestedExpect;
    //      both == "questVariableLow(65847, 0) >= 3"; CombatStep.Id == "defeat-347".
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_E4Prime_LiveOfflineParity_SameExpectAndId()
    {
        // PART A: run extractor
        var events = BuildNibbleCombatTrace(decisionActionType: "wait", includeSubmitted: false);
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();
        Assert.NotNull(combatStep);

        var extractedExpect = Assert.IsType<PredicateExpect>(combatStep!.Expect);
        var extractedPredicate = extractedExpect.Predicate;
        var extractedId = combatStep.Id;

        // PART B: call StepInferenceEngine directly with equivalent snapshots
        var activeQuest = new QuestId(QuestId65847);
        var kct = new Dictionary<NibbleKey, KillCorrelation>
        {
            [new NibbleKey(0, NibbleHalf.Low)] = new KillCorrelation([347u], 3)
        };

        var afterSnapshot = new GameStateSnapshot(
            CapturedAt:         BaseTime.AddMilliseconds(2000),
            Zone:               new ZoneId(148u),
            Position:           new WorldPosition(10f, 0f, 20f),
            ActiveQuest:        activeQuest,
            QuestSequence:      0,
            QuestFlags:         0u,
            QuestAccepted:      true,
            QuestCompleted:     false,
            LastNpcInteracted:  null,
            LastNpcPosition:    null,
            LastDialoguePrompt: null,
            LastDialogueAnswer: null,
            InventoryHash:      0u,
            LastAttuned:        null)
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

        // THEN — parity assertions
        Assert.Equal(liveExpect, extractedPredicate);
        Assert.Equal("questVariableLow(65847, 0) >= 3", extractedPredicate);
        Assert.Equal("defeat-347", extractedId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-O-E-LOCZONE — CombatStep.Location.Zone from CombatStartZone (keep, rebuilt trace)
    //
    // GWT-O-E1' trace → CombatStep.Location.Zone == 148.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_E_LocZone_CombatStepLocationZone_FromCombatStartZone()
    {
        // GIVEN
        var events = BuildNibbleCombatTrace(decisionActionType: "navigate", includeSubmitted: true);

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var combatStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<CombatStep>()
            .FirstOrDefault();

        Assert.NotNull(combatStep);
        Assert.NotNull(combatStep!.Location);
        Assert.Equal(148, combatStep.Location!.Zone);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Zone/RequiredZone parity with StepFactory (keep, rebuilt trace)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_CombatStep_PopulatesZoneAndRequiredZone_MatchingLiveStepFactory()
    {
        // GIVEN
        var events = BuildNibbleCombatTrace(decisionActionType: "navigate", includeSubmitted: true);

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
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
