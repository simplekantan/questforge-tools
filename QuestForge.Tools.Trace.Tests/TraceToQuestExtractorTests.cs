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
}
