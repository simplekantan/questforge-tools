using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="TraceToQuestExtractor"/>.
/// Scenarios 14–20 from PHASE_10_PLAN.md §12.3, plus Scenario 18 (TurnInStep).
/// All tests are RED: they will fail until Builder implements TraceToQuestExtractor.Extract.
/// </summary>
public sealed class TraceToQuestExtractorTests
{
    // -------------------------------------------------------------------------
    // Scenario 14 — Navigate action produces TravelStep with correct Destination
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_NavigateAction_ProducesTravelStep_WithCorrectDestination()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given a trace with RunStart(66130), GetPlayerZone=182 observation,
         *           Decision(null,"navigate"), ActionSubmitted("Navigate", navParams(44.7,4.0,-148.7,zone=182)),
         *           ActionCompleted("Navigate","Arrived"), Decision(null,"done"), RunEnd("done"),
         *           When  Extract,
         *           Then  Definition.Sequences[0].Steps[0] is TravelStep
         *                 with Destination.Zone == 182 and Destination.Position == Position3(44.7f,4.0f,-148.7f).
         *
         * BUILDER GUIDANCE:
         *   - Parse parameters.destination.{x,y,z} and parameters.zone.
         *   - Construct TravelStep with TravelDestination(zone, new Position3(x,y,z)).
         *   - The "done" decision is filtered out (no step emitted for it).
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Obs("GetPlayerPosition",argument: null, value: PositionValue(10f, 0f, 10f), offsetSeconds: 0.2),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(44.7f, 4.0f, -148.7f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Obs("GetPlayerPosition", argument: null, value: PositionValue(44.7f, 4.0f, -148.7f), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.NotEmpty(draft.Definition.Sequences);

        var steps = draft.Definition.Sequences[0].Steps;
        Assert.NotEmpty(steps);

        var travelStep = Assert.IsType<TravelStep>(steps[0]);
        Assert.Equal(182, travelStep.Destination.Zone);
        Assert.NotNull(travelStep.Destination.Position);
        Assert.Equal(44.7f, travelStep.Destination.Position.X, precision: 2);
        Assert.Equal(4.0f,  travelStep.Destination.Position.Y, precision: 2);
        Assert.Equal(-148.7f, travelStep.Destination.Position.Z, precision: 2);
    }

    // -------------------------------------------------------------------------
    // Scenario 15 — Interact action produces TalkStep with correct NpcId
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_InteractAction_ProducesTalkStep_WithCorrectNpcId()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given trace with ActionSubmitted("Interact", parameters={"target": 1014875}),
         *           When  Extract,
         *           Then  produced step is TalkStep with Target.NpcId == 1014875u.
         *
         * BUILDER GUIDANCE:
         *   - Parse parameters.target (uint) for the NPC ID.
         *   - Default step type for Interact is TalkStep (unless inference returns accept/turn-in).
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Obs("IsQuestAccepted",  argument: QuestIdArg(66130u), value: BoolValue(false), offsetSeconds: 0.2),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(1014875u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2),
            // no quest-state change after → inference returns Empty → defaults to TalkStep
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var steps = draft.Definition.Sequences[0].Steps;
        Assert.NotEmpty(steps);

        var talkStep = Assert.IsType<TalkStep>(steps[0]);
        Assert.NotNull(talkStep.Target);
        Assert.Equal(1014875u, talkStep.Target!.NpcId);
    }

    // -------------------------------------------------------------------------
    // Scenario 16 — Sequence advance observation populates Expect on TalkStep
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_QuestSequenceAdvance_PopulatesExpectOnTalkStep()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given before.QuestSequence == 0, after.QuestSequence == 1 (from GetQuestSequence
         *           observation after ActionCompleted for an Interact action),
         *           When  Extract,
         *           Then  the resulting TalkStep.Expect is a PredicateExpect whose Predicate
         *                 equals "questSequence(66130) >= 1".
         *
         * BUILDER GUIDANCE:
         *   - After the Interact's ActionCompleted, apply the GetQuestSequence=1 observation.
         *   - Build "after" snapshot with QuestSequence == 1.
         *   - StepInferenceEngine Rule 3 fires → SuggestedExpect = "questSequence(66130) >= 1".
         *   - Wrap in PredicateExpect and set on the step.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetQuestSequence", argument: QuestIdArg(66130u), value: IntValue(0), offsetSeconds: 0.1),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(1003987u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2),
            Obs("GetQuestSequence", argument: QuestIdArg(66130u), value: IntValue(1), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var steps = draft.Definition.Sequences[0].Steps;
        Assert.NotEmpty(steps);

        var expect = Assert.IsType<PredicateExpect>(steps[0].Expect);
        Assert.Equal("questSequence(66130) >= 1", expect.Predicate);
    }

    // -------------------------------------------------------------------------
    // Scenario 17 — Zone change after Navigate sets TravelStep zone from parameters
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ZoneChange_TravelStep_ZoneFromParameters()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given trace observes GetPlayerZone=182 before Navigate and GetPlayerZone=128
         *           after Navigate completes; parameters.zone == 182 (the destination zone),
         *           When  Extract,
         *           Then  TravelStep.Destination.Zone == 182 (from parameters.zone, preferred).
         *
         * BUILDER GUIDANCE:
         *   - parameters.zone takes precedence over after.Zone when present.
         *   - If parameters.zone is absent, fall back to after.Zone.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(0f, 0f, 0f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(128u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var travelStep = Assert.IsType<TravelStep>(draft.Definition.Sequences[0].Steps[0]);
        Assert.Equal(182, travelStep.Destination.Zone);
    }

    // -------------------------------------------------------------------------
    // Scenario 18 — QuestCompleted observation after Interact → TurnInStep
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_QuestCompletedAfterInteract_ProducesTurnInStep()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given trace observes IsQuestComplete(66130)=true after the last
         *           Interact's ActionCompleted,
         *           When  Extract,
         *           Then  the last step is a TurnInStep whose Expect.Predicate == "isQuestComplete(66130)".
         *
         * BUILDER GUIDANCE:
         *   - StepInferenceEngine Rule 1 fires when after.QuestCompleted && !before.QuestCompleted.
         *   - TraceToQuestExtractor maps inference.StepType == "turn-in" → TurnInStep.
         *   - Set Expect = new PredicateExpect { Predicate = inference.SuggestedExpect }.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Obs("IsQuestComplete",  argument: QuestIdArg(66130u), value: BoolValue(false), offsetSeconds: 0.2),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(1003988u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2),
            Obs("IsQuestComplete", argument: QuestIdArg(66130u), value: BoolValue(true), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var turnInStep = Assert.IsType<TurnInStep>(allSteps.Last());
        var expect = Assert.IsType<PredicateExpect>(turnInStep.Expect);
        Assert.Equal("isQuestComplete(66130)", expect.Predicate);
    }

    // -------------------------------------------------------------------------
    // Scenario 19 — Sequence grouping by before.QuestSequence
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_SequenceGrouping_GroupsByQuestSequenceAtDecisionTime()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given five decisions whose before.QuestSequence values are 0,0,1,1,1,
         *           When  Extract,
         *           Then  Definition.Sequences.Length == 2,
         *                 first group Sequence == 0 with 2 steps,
         *                 second group Sequence == 1 with 3 steps.
         *
         * BUILDER GUIDANCE:
         *   - groupKey = before.QuestSequence at decision time.
         *   - Stable group-by: consecutive decisions with same groupKey → same QuestSequence.
         *   - Each QuestSequence.Sequence == groupKey value.
         */

        // Arrange — five navigate decisions in two groups (seq 0 then seq 1)
        var arg = QuestIdArg(66130u);
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetQuestSequence", argument: arg, value: IntValue(0), offsetSeconds: 0.05),
            // Group 0: two decisions
            Decision("s1", "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(1f, 0f, 1f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Decision("s2", "navigate", offsetSeconds: 2.1),
            Submitted("Navigate", NavParams(2f, 0f, 2f, zone: 182), offsetSeconds: 2.6),
            Completed("Navigate", "Arrived", offsetSeconds: 3),
            // Sequence advances to 1
            Obs("GetQuestSequence", argument: arg, value: IntValue(1), offsetSeconds: 3.1),
            // Group 1: three decisions
            Decision("s3", "navigate", offsetSeconds: 4),
            Submitted("Navigate", NavParams(3f, 0f, 3f, zone: 182), offsetSeconds: 4.5),
            Completed("Navigate", "Arrived", offsetSeconds: 5),
            Decision("s4", "navigate", offsetSeconds: 5.1),
            Submitted("Navigate", NavParams(4f, 0f, 4f, zone: 182), offsetSeconds: 5.6),
            Completed("Navigate", "Arrived", offsetSeconds: 6),
            Decision("s5", "navigate", offsetSeconds: 6.1),
            Submitted("Navigate", NavParams(5f, 0f, 5f, zone: 182), offsetSeconds: 6.6),
            Completed("Navigate", "Arrived", offsetSeconds: 7),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal(2, draft.Definition.Sequences.Length);

        var seq0 = draft.Definition.Sequences[0];
        Assert.Equal(0, seq0.Sequence);
        Assert.Equal(2, seq0.Steps.Length);

        var seq1 = draft.Definition.Sequences[1];
        Assert.Equal(1, seq1.Sequence);
        Assert.Equal(3, seq1.Steps.Length);
    }

    // -------------------------------------------------------------------------
    // Scenario 20 — Consecutive decisions in same sequence stay grouped
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ThreeDecisions_SameQuestSequence_ProducesSingleGroup()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given three consecutive decisions all with before.QuestSequence == 2,
         *           When  Extract,
         *           Then  a single QuestSequence { Sequence = 2, Steps.Length = 3 }.
         *
         * BUILDER GUIDANCE:
         *   - Same groupKey → same QuestSequence object in the output.
         *   - Do not create a new group unless groupKey changes.
         */

        // Arrange
        var arg = QuestIdArg(66130u);
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetQuestSequence", argument: arg, value: IntValue(2), offsetSeconds: 0.05),
            Decision("s1", "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(1f, 0f, 1f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Decision("s2", "navigate", offsetSeconds: 2.1),
            Submitted("Navigate", NavParams(2f, 0f, 2f, zone: 182), offsetSeconds: 2.6),
            Completed("Navigate", "Arrived", offsetSeconds: 3),
            Decision("s3", "navigate", offsetSeconds: 3.1),
            Submitted("Navigate", NavParams(3f, 0f, 3f, zone: 182), offsetSeconds: 3.6),
            Completed("Navigate", "Arrived", offsetSeconds: 4),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var seq = Assert.Single(draft.Definition.Sequences);
        Assert.Equal(2, seq.Sequence);
        Assert.Equal(3, seq.Steps.Length);
    }

    // -------------------------------------------------------------------------
    // Additional: empty trace → no-run-start failure
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_EmptyTrace_ReturnsFailure_NoRunStart()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: Given empty event list,
         *           When  Extract([]),
         *           Then  result is Failure with Reason == "no-run-start".
         */

        // Arrange
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract([]);

        // Assert
        var failure = Assert.IsType<Result<QuestDraftResult>.Failure>(result);
        Assert.Equal("no-run-start", failure.Reason);
    }

    // =========================================================================
    // Tests 22–34: parity improvement tests (RED phase)
    // =========================================================================

    // -------------------------------------------------------------------------
    // Test 22 — Navigate then Interact: NPC position comes from navigate destination
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_NavigateThenInteract_NpcPositionFromNavigateDestination()
    {
        /*
         * RED: Will fail until Builder calls snapshot.RecordNavigateDestination during
         * Navigate action processing and uses LastNpcPosition for subsequent TalkStep.
         *
         * CONTRACT: Given a trace with Navigate to (10, 0, 20) then Interact with NPC 9999,
         *           When  Extract,
         *           Then  the TalkStep produced for the Interact has
         *                 Target.Position == Position3(10, 0, 20)
         *                 (NOT the default zero position).
         *
         * BUILDER GUIDANCE:
         *   - In the Navigate branch, call snapshot.RecordNavigateDestination(new WorldPosition(x,y,z)).
         *   - In the Interact branch, read snapshot.LastNpcPosition for Target.Position.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(10f, 0f, 20f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Decision(null, "interact", offsetSeconds: 2.1),
            Submitted("Interact", InteractParams(9999u), offsetSeconds: 2.6),
            Completed("Interact", "ok", offsetSeconds: 3),
            Decision(null, "done", offsetSeconds: 4),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var talkStep = allSteps.OfType<TalkStep>().FirstOrDefault();
        Assert.NotNull(talkStep);
        Assert.NotNull(talkStep!.Target);
        Assert.Equal(10f, talkStep.Target!.Position.X, precision: 2);
        Assert.Equal(0f,  talkStep.Target.Position.Y,  precision: 2);
        Assert.Equal(20f, talkStep.Target.Position.Z,  precision: 2);
    }

    // -------------------------------------------------------------------------
    // Test 23 — HandOver action produces HandOverItemStep with items from parameters
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_HandOverAction_ProducesHandOverItemStep_ItemsFromParameters()
    {
        /*
         * RED: Will fail until Builder adds "handover" action type handling to
         * TraceToQuestExtractor, producing HandOverItemStep.
         *
         * CONTRACT: Given a trace with Decision("hand-over") and ActionSubmitted("HandOver",
         *           parameters={"target":1234, "items":[501, 502]}),
         *           When  Extract,
         *           Then  the produced step is HandOverItemStep with
         *                 Items == [501u, 502u] and Target.NpcId == 1234u.
         *
         * BUILDER GUIDANCE:
         *   - Add "handover" (case-insensitive) to the action type switch.
         *   - Parse parameters.items (uint[]) for HandOverItemStep.Items.
         *   - Parse parameters.target (uint) for the NPC ID.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "handover", offsetSeconds: 1),
            Submitted("HandOver", HandOverParams(1234u, 501u, 502u), offsetSeconds: 1.5),
            Completed("HandOver", "ok", offsetSeconds: 2),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var handOver = allSteps.OfType<HandOverItemStep>().FirstOrDefault();
        Assert.NotNull(handOver);
        Assert.Equal(1234u, handOver!.Target.NpcId);
        Assert.Contains(501u, handOver.Items);
        Assert.Contains(502u, handOver.Items);
    }

    // -------------------------------------------------------------------------
    // Test 24 — HandOver with no items in params falls back to InventoryChanged
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_HandOverAction_ItemsFromInventoryChangedEvent_WhenParamsLackItems()
    {
        /*
         * RED: Will fail until Builder extracts item IDs from InventoryChangedEvent
         * when HandOver parameters.items is absent/empty.
         *
         * CONTRACT: Given a trace with HandOver parameters that have no "items" field,
         *           but an InventoryChangedEvent emitted between Completed and next Decision
         *           shows Lost=[{ItemId:999, Qty:1}],
         *           When  Extract,
         *           Then  HandOverItemStep.Items == [999u].
         *
         * BUILDER GUIDANCE:
         *   - During the advance-snapshot phase (between Completed and next Decision),
         *     also collect any InventoryChangedEvents.
         *   - If parameters.items is absent/empty, fall back to Lost items from
         *     the collected InventoryChangedEvents.
         */

        // Arrange — parameters have no "items" array
        var noItemsParams = System.Text.Json.JsonSerializer.SerializeToElement(new { target = 1234u });

        var opts = TraceEventJsonContext.Default.Options;
        var inventoryLine = System.Text.Json.JsonSerializer.Serialize<TraceEvent>(
            InventoryChanged(
                gained:        [],
                lost:          [(999u, 1)],
                newHash:       7u,
                runId:         "aaa",
                offsetSeconds: 2.5),
            opts);

        var traceParts = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "handover", offsetSeconds: 1),
            Submitted("HandOver", noItemsParams, offsetSeconds: 1.5),
            Completed("HandOver", "ok", offsetSeconds: 2));

        var jsonl = traceParts + "\n" + inventoryLine + "\n" + MakeTrace(
            Decision(null, "done", offsetSeconds: 3),
            End("done"));

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var handOver = allSteps.OfType<HandOverItemStep>().FirstOrDefault();
        Assert.NotNull(handOver);
        Assert.Contains(999u, handOver!.Items);
    }

    // -------------------------------------------------------------------------
    // Test 25 — HandOver with no items anywhere emits step with empty Items and TODO
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_HandOverAction_NoItemsAnywhere_EmitsStepWithEmptyItemsAndTodo()
    {
        /*
         * RED: Will fail until Builder handles the no-items fallback gracefully.
         *
         * CONTRACT: Given a trace with HandOver but no items in parameters and no
         *           InventoryChangedEvent,
         *           When  Extract,
         *           Then  the HandOverItemStep is still produced (not skipped),
         *                 Items is empty [],
         *                 AND Todos contains a string mentioning "items" for the step.
         *
         * BUILDER GUIDANCE:
         *   - Never skip an action that was successfully submitted; always emit a step.
         *   - When items cannot be inferred, emit Items = [] and record a TODO.
         */

        // Arrange — no items in params, no InventoryChanged events
        var noItemsParams = System.Text.Json.JsonSerializer.SerializeToElement(new { target = 5678u });

        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "handover", offsetSeconds: 1),
            Submitted("HandOver", noItemsParams, offsetSeconds: 1.5),
            Completed("HandOver", "ok", offsetSeconds: 2),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var handOver = allSteps.OfType<HandOverItemStep>().FirstOrDefault();
        Assert.NotNull(handOver);
        Assert.Empty(handOver!.Items);

        // A TODO about items must be recorded
        Assert.Contains(draft.Todos,
            t => t.Contains("item", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Test 26 — HandOver NPC position comes from preceding Navigate
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_HandOverAction_TargetNpcPositionFromPreviousNavigate()
    {
        /*
         * RED: Will fail until Builder propagates RecordNavigateDestination to HandOver.
         *
         * CONTRACT: Given Navigate to (5, 0, 10) then HandOver to NPC 1234,
         *           When  Extract,
         *           Then  HandOverItemStep.Target.Position == Position3(5, 0, 10).
         *
         * BUILDER GUIDANCE:
         *   - Same pattern as TalkStep: read snapshot.LastNpcPosition in the handover branch.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(5f, 0f, 10f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Decision(null, "handover", offsetSeconds: 2.1),
            Submitted("HandOver", HandOverParams(1234u, 801u), offsetSeconds: 2.6),
            Completed("HandOver", "ok", offsetSeconds: 3),
            Decision(null, "done", offsetSeconds: 4),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var handOver = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<HandOverItemStep>()
            .FirstOrDefault();
        Assert.NotNull(handOver);
        Assert.Equal(5f,  handOver!.Target.Position.X, precision: 2);
        Assert.Equal(0f,  handOver.Target.Position.Y,  precision: 2);
        Assert.Equal(10f, handOver.Target.Position.Z,  precision: 2);
    }

    // -------------------------------------------------------------------------
    // Test 27 — UseAethernet action produces TravelStep with RouteHint.Aethernet
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetAction_ProducesTravelStep_WithRouteHintAethernet()
    {
        /*
         * RED: Will fail until Builder adds "useaethernet" action type handling.
         *
         * CONTRACT: Given a trace with Decision("useaethernet") and ActionSubmitted
         *           ("UseAethernet", {"destinationShardId": 77}),
         *           and a zone change after (GetPlayerZone=201),
         *           When  Extract,
         *           Then  the produced step is TravelStep with
         *                 Destination.Zone == 201 AND RouteHint.Aethernet.To == 77u.
         *
         * BUILDER GUIDANCE (Issue #25):
         *   - Add "useaethernet" (case-insensitive) to the action switch.
         *   - Parse parameters.destinationShardId (uint) → AethernetRouteHint.To.
         *   - Parse optional parameters.sourceShardId (uint) → AethernetRouteHint.From.
         *   - Destination.Zone from after.Zone (zone observed after completion).
         *   - RouteHint = new RouteHint(Aethernet: new AethernetRouteHint(From: ..., To: destShardId)).
         *
         * TODO BUILDER: Old assertion was `Assert.Contains(77u, travel.RouteHint.Aethernet!)`
         * using array containment. Now Aethernet is AethernetRouteHint; assert .To == 77u.
         * The old array form is now a compile error (RouteHint.Aethernet is AethernetRouteHint?).
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", UseAethernetParams(77u), offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(201u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var travel = allSteps.OfType<TravelStep>().LastOrDefault();
        Assert.NotNull(travel);
        Assert.Equal(201, travel!.Destination.Zone);
        Assert.NotNull(travel.RouteHint);
        // RED: AethernetRouteHint does not exist yet — compile error expected when
        // asserting .To property (Aethernet is still string[]? in tools-side schema)
        Assert.NotNull(travel.RouteHint!.Aethernet);
        Assert.Equal(77u, travel.RouteHint!.Aethernet!.To);
    }

    // -------------------------------------------------------------------------
    // Test 28 — UseAethernet with unknown shard still produces TravelStep with TODO
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetAction_UnknownShardId_TravelStep_WithTodo()
    {
        /*
         * RED: Will fail until Builder handles missing destinationShardId gracefully.
         *
         * CONTRACT: Given a UseAethernet action where parameters.destinationShardId is 0
         *           or absent, and the zone does not change after the action,
         *           When  Extract,
         *           Then  a TravelStep is still produced AND Todos contains a string
         *                 mentioning "aethernet" (case-insensitive).
         *
         * BUILDER GUIDANCE (Issue #25):
         *   - When destinationShardId == 0 or missing, emit TravelStep with
         *     RouteHint = null (no confirmed shard to teleport to).
         *   - Record a TODO about the unknown aethernet destination.
         *   - Do NOT emit AethernetRouteHint with To=0; that would fail the validator rule
         *     "structural/route-hint-aethernet-to-missing".
         */

        // Arrange — parameters with destinationShardId=0 (sentinel for unknown)
        var unknownParams = System.Text.Json.JsonSerializer.SerializeToElement(
            new { destinationShardId = 0u });

        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", unknownParams, offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            // No zone change — shard stayed in same zone
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        Assert.Contains(allSteps, s => s is TravelStep);
        Assert.Contains(draft.Todos,
            t => t.Contains("aethernet", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Test 29 (B22) — Interact on aetheryte WITH AethernetShardTargeted → AttunementStep
    //                 (unified trace — authoring mode observation IS present)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_InteractOnAetheryte_WithAethernetShardTargeted_ProducesAttunementStep()
    {
        /*
         * RED: Will fail until Builder handles the attunement inference path in extractor.
         *
         * B22 — UNIFIED TRACE CASE: The AethernetShardTargeted observation fires in
         * authoring/unified traces via AuthoringHost.PollTargetNpc. This IS present here.
         *
         * CONTRACT: Given a trace where:
         *           1. AethernetShardTargeted(shardId=8) fires before the Interact,
         *           2. Interact(target=8) is submitted,
         *           3. IsAetheryteAttuned(8, value=1) fires after Completed,
         *           When  Extract,
         *           Then  the produced step is AttunementStep with Target.Value == 8u
         *                 (not a TalkStep).
         *
         * BUILDER GUIDANCE:
         *   - In the Interact branch, after advance: check if inference.InferredFrom ==
         *     InferredFrom.AttunementChange → emit AttunementStep instead of TalkStep.
         *   - SnapshotState must forward AethernetShardTargeted and IsAetheryteAttuned
         *     to GameStateSnapshot.LastAethernetShardInteracted and .LastAttuned so that
         *     StepInferenceEngine.Rule2.5 fires correctly.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            // Authoring: aethernet shard targeted before interact
            ObsAethernetShardTargeted(shardId: 8u, offsetSeconds: 0.4),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(8u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2),
            // After interact: attuned = 1
            ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1, offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var attune = allSteps.OfType<AttunementStep>().FirstOrDefault();
        Assert.NotNull(attune);
        Assert.Equal(8u, attune!.Target.Value);
    }

    // -------------------------------------------------------------------------
    // Test 30 (B23) — Interact on aetheryte WITHOUT AethernetShardTargeted → fallback
    //                 (pure engine-run trace — authoring observation NOT present)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_InteractOnAetheryte_WithoutAethernetShardTargeted_FallbackAttunement()
    {
        /*
         * RED: Will fail until Builder handles the attunement fallback in extractor.
         *
         * B23 — PURE ENGINE-RUN TRACE: AethernetShardTargeted does NOT fire (it is an
         * authoring-only observation from AuthoringHost.PollTargetNpc). Instead, only
         * IsAetheryteAttuned transitions from 0 to 1 after the Interact.
         *
         * CONTRACT: Given a trace where:
         *           1. No AethernetShardTargeted event,
         *           2. IsAetheryteAttuned(8, value=0) before the Interact,
         *           3. Interact(target=8) submitted,
         *           4. IsAetheryteAttuned(8, value=1) fires after Completed,
         *           When  Extract,
         *           Then  the produced step is AttunementStep with Target.Value == 8u.
         *
         * BUILDER GUIDANCE:
         *   - When AethernetShardTargeted is absent, detect the attunement via:
         *     before.LastAttuned != after.LastAttuned, derive the aetheryte ID from
         *     after.LastAttuned (newly set by IsAetheryteAttuned=1 in the after window).
         *   - StepInferenceEngine.Rule2.5 requires LastAethernetShardInteracted — the
         *     extractor must synthesize it from the Interact target NPC ID when the
         *     observation is absent but IsAetheryteAttuned changes.
         */

        // Arrange — no AethernetShardTargeted observation
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            // Not attuned yet (before)
            ObsIsAetheryteAttuned(aetheryteId: 8u, value: 0, offsetSeconds: 0.3),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(8u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2),
            // Attuned after (IsAetheryteAttuned=1)
            ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1, offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var attune = allSteps.OfType<AttunementStep>().FirstOrDefault();
        Assert.NotNull(attune);
        Assert.Equal(8u, attune!.Target.Value);
    }

    // -------------------------------------------------------------------------
    // Test 31 — AttunementStep location comes from preceding Navigate
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_AttunementStep_LocationFromPreviousNavigate()
    {
        /*
         * RED: Will fail until Builder propagates RecordNavigateDestination to AttunementStep.
         *
         * CONTRACT: Given Navigate to (100, 0, 200) then Interact on aetheryte 8
         *           (with AethernetShardTargeted + IsAetheryteAttuned=1 after),
         *           When  Extract,
         *           Then  AttunementStep.Location.Position == Position3(100, 0, 200).
         *
         * BUILDER GUIDANCE:
         *   - In the AttunementStep construction branch, read snapshot.LastNpcPosition
         *     for Location.Position (same pattern as TalkStep/HandOverItemStep).
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(100f, 0f, 200f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            ObsAethernetShardTargeted(shardId: 8u, offsetSeconds: 2.1),
            Decision(null, "interact", offsetSeconds: 2.2),
            Submitted("Interact", InteractParams(8u), offsetSeconds: 2.7),
            Completed("Interact", "ok", offsetSeconds: 3),
            ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1, offsetSeconds: 3.5),
            Decision(null, "done", offsetSeconds: 4),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var attune = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<AttunementStep>()
            .FirstOrDefault();
        Assert.NotNull(attune);
        Assert.NotNull(attune!.Location);
        Assert.Equal(100f, attune.Location!.Position.X, precision: 2);
        Assert.Equal(0f,   attune.Location.Position.Y,  precision: 2);
        Assert.Equal(200f, attune.Location.Position.Z,  precision: 2);
    }

    // -------------------------------------------------------------------------
    // Test 32 — HandOver action type is case-insensitive ("HandOver", "HANDOVER")
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_HandOverActionType_CaseInsensitive()
    {
        /*
         * RED: Will fail until Builder uses ToLowerInvariant() on the submitted action type.
         *
         * CONTRACT: Given a trace where ActionSubmitted has ActionType == "HANDOVER"
         *           (uppercase), When  Extract,
         *           Then  a HandOverItemStep is produced.
         *
         * BUILDER GUIDANCE:
         *   - Normalize via submittedTypeLower = submitted.ActionType.ToLowerInvariant()
         *     before the switch/if-chain. This is already done for navigate/interact.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "handover", offsetSeconds: 1),
            Submitted("HANDOVER", HandOverParams(1111u, 601u), offsetSeconds: 1.5),
            Completed("HANDOVER", "ok", offsetSeconds: 2),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        Assert.Contains(allSteps, s => s is HandOverItemStep);
    }

    // -------------------------------------------------------------------------
    // Test 33 — UseAethernet action type is case-insensitive
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetActionType_CaseInsensitive()
    {
        /*
         * RED: Will fail until Builder uses ToLowerInvariant() on the submitted type.
         *
         * CONTRACT: Given ActionSubmitted has ActionType == "UseAethernet" (mixed case),
         *           When  Extract,
         *           Then  a TravelStep (with aethernet route hint or base zone travel)
         *                 is produced — not skipped as an unrecognised type.
         *
         * BUILDER GUIDANCE:
         *   - "useaethernet".Equals(submittedTypeLower) catches all casings.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", UseAethernetParams(55u), offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(130u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        Assert.Contains(allSteps, s => s is TravelStep);
    }

    // -------------------------------------------------------------------------
    // Test 34 — Key item deltas from one decision do not leak into the next
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_TwoDecisions_KeyItemDeltasDoNotLeakBetween()
    {
        /*
         * RED: Will fail until Builder calls snapshot.ResetPendingKeyItemDeltas
         * between decisions.
         *
         * CONTRACT: Given two sequential decisions D1 (talk, gains item 101) and
         *           D2 (talk, no inventory change):
         *           When  Extract,
         *           Then  the step for D2 does NOT have KeyItemsAdded containing 101u
         *                 (the delta from D1 must not bleed into the D2 inference window).
         *
         *           Verified by checking that D2 produces a TalkStep (not pickup-item),
         *           since if 101 leaked into D2's "after" snapshot as KeyItemsAdded,
         *           StepInferenceEngine.Rule2.3 would incorrectly fire.
         *
         * BUILDER GUIDANCE:
         *   - After building the "after" snapshot for decision i (and thus the step),
         *     call snapshot.ResetPendingKeyItemDeltas() so the next decision's "before"
         *     snapshot sees a clean slate of pending deltas.
         */

        // Arrange
        var opts = TraceEventJsonContext.Default.Options;
        var inventoryLine = System.Text.Json.JsonSerializer.Serialize<TraceEvent>(
            InventoryChanged(
                gained:        [(101u, 1)],
                lost:          [],
                newHash:       5u,
                runId:         "aaa",
                offsetSeconds: 2.5),
            opts);

        // D1: Interact (talk) → gains item 101
        // D2: Interact (talk) → no inventory change → should produce TalkStep
        var tracePart1 = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Obs("GetQuestSequence", argument: QuestIdArg(66130u), value: IntValue(1), offsetSeconds: 0.2),
            Decision(null, "interact", offsetSeconds: 1),
            Submitted("Interact", InteractParams(1001u), offsetSeconds: 1.5),
            Completed("Interact", "ok", offsetSeconds: 2));

        // InventoryChanged fires between D1 Completed and D2 Decision
        var tracePart2 = MakeTrace(
            Decision(null, "interact", offsetSeconds: 3),
            Submitted("Interact", InteractParams(1002u), offsetSeconds: 3.5),
            Completed("Interact", "ok", offsetSeconds: 4),
            // No inventory change after D2
            Decision(null, "done", offsetSeconds: 5),
            End("done"));

        var jsonl = tracePart1 + "\n" + inventoryLine + "\n" + tracePart2;

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        // D2 step must NOT be a PickupItemStep (which would indicate a delta leak)
        var lastInteractStep = allSteps.LastOrDefault(s => s is TalkStep or PickupItemStep);
        Assert.IsType<TalkStep>(lastInteractStep);
    }

    // -------------------------------------------------------------------------
    // Additional: SchemaVersion on extracted QuestDefinition is "1.0.0"
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ValidTrace_SchemaVersionIs100()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: The extracted QuestDefinition always has SchemaVersion == "1.0.0".
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Decision(null, "done", offsetSeconds: 1),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("1.0.0", draft.Definition.SchemaVersion);
    }

    // -------------------------------------------------------------------------
    // Additional: Todos list includes standard TODO fields
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ProducesTodosForManualFields()
    {
        /*
         * RED: Will fail until Builder implements TraceToQuestExtractor.Extract.
         *
         * CONTRACT: The Todos list must include at minimum entries for:
         *           "name", "expansion", "category", "lastVerifiedPatch".
         *
         * BUILDER GUIDANCE:
         *   - Collect TODO strings during QuestDefinition assembly.
         *   - Print at the end so the author has a checklist.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Decision(null, "done", offsetSeconds: 1),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Contains(draft.Todos, t => t.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(draft.Todos, t => t.Contains("expansion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(draft.Todos, t => t.Contains("category", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(draft.Todos, t => t.Contains("lastVerifiedPatch", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Issue #25: Tests for sourceShardId parameter in UseAethernet trace events
    // and the new AethernetRouteHint type in TraceToQuestExtractor
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Test 35 — UseAethernet with sourceShardId → AethernetRouteHint.From is populated
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetAction_WithSourceShardId_SetsRouteHintFrom()
    {
        /*
         * RED: Will fail until Builder:
         *   1. Adds AethernetRouteHint to QuestForge.Schema
         *   2. Changes RouteHint.Aethernet from string[]? to AethernetRouteHint?
         *   3. Updates TraceToQuestExtractor useaethernet branch to read parameters.sourceShardId
         *
         * CONTRACT: Given UseAethernet action with parameters containing both
         *           sourceShardId=125 and destinationShardId=77,
         *           When  Extract,
         *           Then  TravelStep.RouteHint.Aethernet.From == 125u
         *                 AND TravelStep.RouteHint.Aethernet.To == 77u.
         *
         * BUILDER GUIDANCE:
         *   In the useaethernet branch, after parsing destinationShardId:
         *     uint sourceShardId = 0;
         *     if (p.TryGetProperty("sourceShardId", out var src))
         *         try { sourceShardId = src.GetUInt32(); } catch { }
         *   Then construct:
         *     new AethernetRouteHint(
         *         From: sourceShardId == 0 ? null : sourceShardId,
         *         To: destShardId)
         */

        // Arrange — UseAethernetParams updated to include sourceShardId
        // RED: UseAethernetParamsWithSource helper does not exist yet
        var paramsWithSource = UseAethernetParamsWithSource(sourceShardId: 125u, destinationShardId: 77u);

        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", paramsWithSource, offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(201u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();
        var travel = allSteps.OfType<TravelStep>().LastOrDefault();
        Assert.NotNull(travel);
        Assert.NotNull(travel!.RouteHint);
        // RED: AethernetRouteHint does not exist — compile error expected on .From and .To
        Assert.NotNull(travel.RouteHint!.Aethernet);
        Assert.Equal(77u, travel.RouteHint!.Aethernet!.To);
        Assert.Equal(125u, travel.RouteHint.Aethernet.From);
    }

    // -------------------------------------------------------------------------
    // Test 36 — UseAethernet without sourceShardId → AethernetRouteHint.From is null
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetAction_WithoutSourceShardId_FromIsNull()
    {
        /*
         * RED: Will fail until Builder adds AethernetRouteHint and updates extractor.
         *
         * CONTRACT: Given UseAethernet action with only destinationShardId=77 (no sourceShardId),
         *           When  Extract,
         *           Then  TravelStep.RouteHint.Aethernet.From is null
         *                 AND TravelStep.RouteHint.Aethernet.To == 77u.
         *
         * BUILDER GUIDANCE: sourceShardId is optional; when absent or 0, From = null.
         */

        // Arrange — only destination, no source
        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", UseAethernetParams(77u), offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(201u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var travel = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<TravelStep>()
            .LastOrDefault();
        Assert.NotNull(travel);
        Assert.NotNull(travel!.RouteHint?.Aethernet);
        // RED: AethernetRouteHint does not exist — compile error expected
        Assert.Equal(77u, travel.RouteHint!.Aethernet!.To);
        Assert.Null(travel.RouteHint.Aethernet.From);
    }

    // -------------------------------------------------------------------------
    // Test 37 — UseAethernet with sourceShardId=0 treats it as null (sentinel)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetAction_SourceShardIdZero_TreatedAsNull()
    {
        /*
         * RED: Will fail until Builder adds AethernetRouteHint and updates extractor.
         *
         * CONTRACT: Given UseAethernet action with sourceShardId=0 and destinationShardId=77,
         *           When  Extract,
         *           Then  AethernetRouteHint.From is null (0 is the unset sentinel).
         *
         * BUILDER GUIDANCE: When sourceShardId == 0, set From = null. Only non-zero
         * source shard IDs are meaningful.
         */

        // Arrange
        var paramsWithZeroSource = UseAethernetParamsWithSource(sourceShardId: 0u, destinationShardId: 77u);

        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", paramsWithZeroSource, offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(201u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var travel = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<TravelStep>()
            .LastOrDefault();
        Assert.NotNull(travel);
        Assert.NotNull(travel!.RouteHint?.Aethernet);
        // RED: AethernetRouteHint does not exist — compile error expected
        Assert.Equal(77u, travel.RouteHint!.Aethernet!.To);
        Assert.Null(travel.RouteHint.Aethernet.From);
    }

    // -------------------------------------------------------------------------
    // Test 38 — UseAethernetParams helper: existing signature (no source) stays valid
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_UseAethernetParams_NoSourceShardId_StillProducesValidStep()
    {
        /*
         * CONTRACT: The existing UseAethernetParams helper (no sourceShardId) must
         * continue to work — no breaking change to existing test helpers.
         *
         * Verifies that the addition of optional sourceShardId parsing does not break
         * traces recorded before Issue #25 (where only destinationShardId was emitted).
         *
         * RED: Will fail until Builder adds AethernetRouteHint and updates extractor.
         */

        var jsonl = MakeTrace(
            Start(questId: 66130u),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Decision(null, "useaethernet", offsetSeconds: 1),
            Submitted("UseAethernet", UseAethernetParams(33u), offsetSeconds: 1.5),
            Completed("UseAethernet", "ok", offsetSeconds: 2),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(130u), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );

        var events = TraceEventParser.ReadText(jsonl);
        var result = new TraceToQuestExtractor().Extract(events);

        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var travel = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<TravelStep>()
            .LastOrDefault();
        Assert.NotNull(travel);
        // RED: compile error on .Aethernet.To until AethernetRouteHint exists
        Assert.Equal(33u, travel!.RouteHint!.Aethernet!.To);
        Assert.Null(travel.RouteHint.Aethernet.From);
    }

    // -------------------------------------------------------------------------
    // Test 39 — UseAethernetParamsWithSource helper exists in TraceTestHelpers
    // -------------------------------------------------------------------------

    [Fact]
    public void TraceTestHelpers_UseAethernetParamsWithSource_ProducesExpectedShape()
    {
        /*
         * RED: Will fail until Builder adds UseAethernetParamsWithSource helper to
         * TraceTestHelpers.cs.
         *
         * CONTRACT: Given UseAethernetParamsWithSource(sourceShardId: 125, destinationShardId: 77),
         *           When the element is inspected,
         *           Then it contains both "sourceShardId":125 and "destinationShardId":77.
         *
         * BUILDER GUIDANCE: Add to TraceTestHelpers.cs:
         *   internal static JsonElement UseAethernetParamsWithSource(
         *       uint sourceShardId, uint destinationShardId)
         *       => JsonSerializer.SerializeToElement(new { sourceShardId, destinationShardId });
         */

        // RED: UseAethernetParamsWithSource helper does not exist yet
        var element = UseAethernetParamsWithSource(sourceShardId: 125u, destinationShardId: 77u);

        Assert.True(element.TryGetProperty("sourceShardId", out var srcProp));
        Assert.Equal(125u, srcProp.GetUInt32());
        Assert.True(element.TryGetProperty("destinationShardId", out var dstProp));
        Assert.Equal(77u, dstProp.GetUInt32());
    }
}
