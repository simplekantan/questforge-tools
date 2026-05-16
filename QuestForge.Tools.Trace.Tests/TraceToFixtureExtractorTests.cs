using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Tools.Trace.Fixture;
using QuestForge.Tools.Trace.Parsing;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="TraceToFixtureExtractor"/>.
/// Scenarios 1–7 from PHASE_10_PLAN.md §12.1.
/// All tests are RED: they will fail until Builder implements Extract.
/// </summary>
public sealed class TraceToFixtureExtractorTests
{
    // -------------------------------------------------------------------------
    // Scenario 1 — Empty trace returns no-run-start failure
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_EmptyEventList_ReturnsFailure_NoRunStart()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given an empty event list,
         *           When  Extract([]) is called,
         *           Then  result is Failure with Reason == "no-run-start".
         *
         * BUILDER GUIDANCE:
         *   - First step of the algorithm: find the RunStartEvent.
         *   - If not found, return Result.Fail<FixtureModel>("no-run-start", "trace contains no run.start event").
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();

        // Act
        var result = extractor.Extract([]);

        // Assert
        var failure = Assert.IsType<Result<FixtureModel>.Failure>(result);
        Assert.Equal("no-run-start", failure.Reason);
    }

    // -------------------------------------------------------------------------
    // Scenario 2 — RunStart but no RunEnd → TerminalOutcome is null
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_RunStartNoRunEnd_ReturnsSuccess_TerminalOutcomeNull()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given trace with RunStart, one Decision(navigate,"step-1"), no RunEnd,
         *           When  Extract is called,
         *           Then  result is Success, TerminalOutcome == null,
         *                 and one transition ("step-1", "navigate") is present.
         *
         * BUILDER GUIDANCE:
         *   - If no RunEndEvent is found, set terminalOutcome = null.
         *   - "navigate" is already lowercase — no transformation needed.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new[]
        {
            (TraceEvent)Start(),
            Decision("step-1", "navigate", offsetSeconds: 1)
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var success = Assert.IsType<Result<FixtureModel>.Success>(result);
        Assert.Null(success.Value.TerminalOutcome);
        Assert.Single(success.Value.ExpectedTransitions);
        Assert.Equal("step-1",   success.Value.ExpectedTransitions[0].StepId);
        Assert.Equal("navigate", success.Value.ExpectedTransitions[0].ActionType);
    }

    // -------------------------------------------------------------------------
    // Scenario 3 — Single Navigate + RunEnd("done") → correct transitions and outcome
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_SingleNavigate_RunEndDone_ProducesCorrectFixture()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given RunStart(quest=42) → Decision("travel-to-x","navigate") → RunEnd("done"),
         *           When  Extract,
         *           Then  expectedTransitions == [("travel-to-x","navigate")],
         *                 terminalOutcome == "done".
         *
         * BUILDER GUIDANCE:
         *   - "done" decision is filtered OUT from transitions (it is a terminal action).
         *   - RunEnd.Outcome is stored as-is ("done" is already lowercase).
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(questId: 42),
            Decision("travel-to-x", "navigate", offsetSeconds: 1),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var success = Assert.IsType<Result<FixtureModel>.Success>(result);
        var fixture = success.Value;

        Assert.Equal("done", fixture.TerminalOutcome);
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("travel-to-x", fixture.ExpectedTransitions[0].StepId);
        Assert.Equal("navigate",    fixture.ExpectedTransitions[0].ActionType);
    }

    // -------------------------------------------------------------------------
    // Scenario 4 — Consecutive identical decisions deduplicate
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_ConsecutiveIdenticalDecisions_CollapseToOne()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given RunStart → Decision("s1","navigate")×4 → Decision("s1","interact")×2 → RunEnd,
         *           When  Extract,
         *           Then  expectedTransitions == [("s1","navigate"), ("s1","interact")].
         *
         * BUILDER GUIDANCE:
         *   - Only append a transition if it differs from the LAST appended transition.
         *   - Comparison: both stepId (null-aware) and actionType must match.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new List<TraceEvent>
        {
            Start(),
            Decision("s1", "navigate", offsetSeconds: 1),
            Decision("s1", "navigate", offsetSeconds: 2),
            Decision("s1", "navigate", offsetSeconds: 3),
            Decision("s1", "navigate", offsetSeconds: 4),
            Decision("s1", "interact", offsetSeconds: 5),
            Decision("s1", "interact", offsetSeconds: 6),
            End()
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal(2, fixture.ExpectedTransitions.Count);
        Assert.Equal(("s1", "navigate"), (fixture.ExpectedTransitions[0].StepId, fixture.ExpectedTransitions[0].ActionType));
        Assert.Equal(("s1", "interact"), (fixture.ExpectedTransitions[1].StepId, fixture.ExpectedTransitions[1].ActionType));
    }

    // -------------------------------------------------------------------------
    // Scenario 5 — Consecutive DIFFERENT transitions are all preserved
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_SixDistinctConsecutiveDecisions_AllPreserved()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given 6 decisions (A,nav)(A,int)(B,nav)(B,int)(B,nav)(B,int) between RunStart/RunEnd,
         *           When  Extract,
         *           Then  expectedTransitions has all 6 entries (no adjacent pair is identical).
         *
         * BUILDER GUIDANCE:
         *   - Deduplication is only for CONSECUTIVE identical pairs.
         *   - (A,nav) followed by (A,int) are different → both kept.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new List<TraceEvent>
        {
            Start(),
            Decision("A", "navigate", offsetSeconds: 1),
            Decision("A", "interact", offsetSeconds: 2),
            Decision("B", "navigate", offsetSeconds: 3),
            Decision("B", "interact", offsetSeconds: 4),
            Decision("B", "navigate", offsetSeconds: 5),
            Decision("B", "interact", offsetSeconds: 6),
            End()
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal(6, fixture.ExpectedTransitions.Count);
    }

    // -------------------------------------------------------------------------
    // Scenario 6 — RunId mismatch filter: only matching runId decisions are kept
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_RunIdMismatch_FiltersOutForeignRunDecisions()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given RunStart(runId="aaa") → Decision(runId="bbb","s-other","navigate")
         *                → Decision(runId="aaa","s-mine","navigate") → RunEnd(runId="aaa","done"),
         *           When  Extract,
         *           Then  only the runId="aaa" decision appears in transitions.
         *
         * BUILDER GUIDANCE:
         *   - After locating RunStart, capture RunStart.RunId.
         *   - Skip any DecisionEvent whose RunId != captured RunId.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(runId: "aaa"),
            Decision("s-other", "navigate", runId: "bbb", offsetSeconds: 1),
            Decision("s-mine",  "navigate", runId: "aaa", offsetSeconds: 2),
            End(runId: "aaa")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("s-mine", fixture.ExpectedTransitions[0].StepId);
    }

    // -------------------------------------------------------------------------
    // Scenario 7 — questFile resolution from disk (uses Fixtures directory)
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_QuestFileResolution_FindsFileOnDisk_ReturnsForwardSlashPath()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract
         *      with quest-file resolution logic.
         *
         * CONTRACT: Given trace with RunStart(quest=66130) and a questDataRoot pointing at
         *           the test Fixtures directory containing quests/arr/msq/66130-coming-to-uldah.json,
         *           When  Extract,
         *           Then  fixture.QuestFile == "quests/arr/msq/66130-coming-to-uldah.json"
         *                 (forward slashes, relative to questDataRoot).
         *
         * BUILDER GUIDANCE:
         *   - Search questDataRoot/quests/ recursively for {questId}-*.json files.
         *   - Return the path relative to questDataRoot with Path.GetFullPath-normalised separators
         *     converted to forward slashes.
         */

        // Arrange — locate the Fixtures directory relative to the test binary output directory.
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var extractor = new TraceToFixtureExtractor(questDataRoot: fixturesDir);

        var events = new TraceEvent[]
        {
            Start(questId: 66130u),
            Decision("travel-to-wymond", "navigate", offsetSeconds: 1),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal("quests/arr/msq/66130-coming-to-uldah.json", fixture.QuestFile);
    }

    // -------------------------------------------------------------------------
    // Additional: "done" action type is NOT emitted as a transition
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_DoneDecision_IsNotAddedToTransitions()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.Extract.
         *
         * CONTRACT: Given RunStart → Decision("step-1","navigate") → Decision(null,"done") → RunEnd("done"),
         *           When  Extract,
         *           Then  only the navigate transition is in expectedTransitions.
         *                 The "done" decision is skipped (it is a terminal action, not a step).
         *
         * BUILDER GUIDANCE:
         *   - In the decision loop, skip when actionType.ToLowerInvariant() is "done" or "awaituser".
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(),
            Decision("step-1", "navigate", offsetSeconds: 1),
            Decision(null, "done", offsetSeconds: 5),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("navigate", fixture.ExpectedTransitions[0].ActionType);
    }

    // -------------------------------------------------------------------------
    // Additional: SuggestFilename returns "simple-linear-acceptance.json" for travel+talk
    // -------------------------------------------------------------------------

    [Fact]
    public void SuggestFilename_TravelPlusTalk_ReturnsSimpleLinearAcceptance()
    {
        /*
         * RED: Will fail until Builder implements TraceToFixtureExtractor.SuggestFilename.
         *
         * CONTRACT: Given a fixture whose capabilities include step:travel and step:talk,
         *           When  SuggestFilename(fixture),
         *           Then  returns "simple-linear-acceptance.json".
         *
         * BUILDER GUIDANCE:
         *   - Extract the step: tags from capabilities, sort, look up in the static table.
         *   - The table entry ["step:travel","step:talk"] → "simple-linear-acceptance.json".
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["predicate:playerNear", "step:talk", "step:travel"],
            QuestFile: "quests/arr/msq/66130-coming-to-uldah.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        // Act
        var filename = extractor.SuggestFilename(fixture);

        // Assert
        Assert.Equal("simple-linear-acceptance.json", filename);
    }
}
