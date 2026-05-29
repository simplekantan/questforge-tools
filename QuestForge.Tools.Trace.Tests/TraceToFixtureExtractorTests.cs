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

    // =========================================================================
    // FX-NEW-1/2/3/4 — New action types in transitions (feat/extract-fixtures-update)
    // =========================================================================

    [Fact]
    public void Extract_HandoverDecision_AppearsInTransitions()
    {
        /*
         * CONTRACT: Given RunStart → Decision(null, "handover") → RunEnd("done"),
         *           When  Extract,
         *           Then  transitions contains exactly one entry with ActionType == "handover".
         *
         * "handover" is NOT a terminal action (IsTerminalAction returns false for it),
         * so it must appear in ExpectedTransitions.
         *
         * GREEN: no code change needed — IsTerminalAction does not filter this action type.
         * Added here as a regression test to lock the behaviour.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(),
            Decision(null, "handover", offsetSeconds: 1),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("handover", fixture.ExpectedTransitions[0].ActionType);
    }

    [Fact]
    public void Extract_UseAethernetDecision_AppearsInTransitions()
    {
        /*
         * CONTRACT: Given RunStart → Decision(null, "useaethernet") → RunEnd("done"),
         *           When  Extract,
         *           Then  transitions contains exactly one entry with ActionType == "useaethernet".
         *
         * "useaethernet" is NOT a terminal action (IsTerminalAction returns false for it),
         * so it must appear in ExpectedTransitions.
         *
         * GREEN: no code change needed — IsTerminalAction does not filter this action type.
         * Added here as a regression test to lock the behaviour.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(),
            Decision(null, "useaethernet", offsetSeconds: 1),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("useaethernet", fixture.ExpectedTransitions[0].ActionType);
    }

    [Fact]
    public void Extract_AttuneDecision_AppearsInTransitions()
    {
        /*
         * CONTRACT: Given RunStart → Decision(null, "attune") → RunEnd("done"),
         *           When  Extract,
         *           Then  transitions contains exactly one entry with ActionType == "attune".
         *
         * "attune" is NOT a terminal action (IsTerminalAction returns false for it),
         * so it must appear in ExpectedTransitions.
         *
         * GREEN: no code change needed — IsTerminalAction does not filter this action type.
         * Added here as a regression test to lock the behaviour.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(),
            Decision(null, "attune", offsetSeconds: 1),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Single(fixture.ExpectedTransitions);
        Assert.Equal("attune", fixture.ExpectedTransitions[0].ActionType);
    }

    [Fact]
    public void Extract_FullQuestWithNewActionTypes_AllThreeAppearsInTransitions()
    {
        /*
         * CONTRACT: Given RunStart → Decision("s1","attune") → Decision("s2","handover")
         *                → Decision("s3","useaethernet") → RunEnd("done"),
         *           When  Extract,
         *           Then  transitions contains all three in order:
         *                 [0].ActionType == "attune",
         *                 [1].ActionType == "handover",
         *                 [2].ActionType == "useaethernet".
         *
         * None of these action types are terminal, so all three appear in transitions.
         *
         * GREEN: no code change needed — IsTerminalAction does not filter any of these.
         * Added here as a regression test to lock the behaviour.
         */

        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(),
            Decision("s1", "attune",        offsetSeconds: 1),
            Decision("s2", "handover",      offsetSeconds: 2),
            Decision("s3", "useaethernet",  offsetSeconds: 3),
            End("done")
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal(3, fixture.ExpectedTransitions.Count);
        Assert.Equal("attune",       fixture.ExpectedTransitions[0].ActionType);
        Assert.Equal("handover",     fixture.ExpectedTransitions[1].ActionType);
        Assert.Equal("useaethernet", fixture.ExpectedTransitions[2].ActionType);
    }

    // =========================================================================
    // Dual-RunEnd resolution (feat/fixture-replay-harness)
    // =========================================================================

    /// <summary>
    /// T-done-then-ended (RED): engine emits "done" then EngineHost emits "ended".
    /// Extract must prefer the real engine terminal outcome "done" over the cleanup marker.
    /// Currently FAILS because Extract uses LastOrDefault and returns "ended".
    /// </summary>
    [Fact]
    public void Extract_DoneThenEnded_PrefersDone()
    {
        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(runId: "r1"),
            Decision("step-1", "navigate", runId: "r1", offsetSeconds: 1),
            End(outcome: "done",   runId: "r1", offsetSeconds: 10),
            End(outcome: "ended",  runId: "r1", offsetSeconds: 10.003)
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal("done", fixture.TerminalOutcome);
    }

    /// <summary>
    /// T-awaituser-then-ended (RED): engine emits "awaitUser" then EngineHost emits "ended".
    /// Extract must prefer the real engine terminal outcome "awaitUser" over the cleanup marker.
    /// Currently FAILS because Extract uses LastOrDefault and returns "ended".
    /// </summary>
    [Fact]
    public void Extract_AwaitUserThenEnded_PrefersAwaitUser()
    {
        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(runId: "r1"),
            Decision("step-1", "navigate", runId: "r1", offsetSeconds: 1),
            End(outcome: "awaitUser", runId: "r1", offsetSeconds: 10),
            End(outcome: "ended",     runId: "r1", offsetSeconds: 10.003)
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal("awaitUser", fixture.TerminalOutcome);
    }

    /// <summary>
    /// T-only-ended (regression guard): a manual stop with no done/awaitUser engine terminal.
    /// A single "ended" event must be preserved as-is — the fix must not over-correct.
    /// Must PASS both before and after the builder's fix.
    /// </summary>
    [Fact]
    public void Extract_OnlyEnded_PreservesEnded()
    {
        // Arrange
        var extractor = new TraceToFixtureExtractor();
        var events = new TraceEvent[]
        {
            Start(runId: "r1"),
            Decision("step-1", "navigate", runId: "r1", offsetSeconds: 1),
            End(outcome: "ended", runId: "r1", offsetSeconds: 10)
        };

        // Act
        var result = extractor.Extract(events);

        // Assert
        var fixture = Assert.IsType<Result<FixtureModel>.Success>(result).Value;
        Assert.Equal("ended", fixture.TerminalOutcome);
    }

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

    // =========================================================================
    // Catch-up: filename suggestions for newer step shapes (use-action, use-emote,
    // teleport, purchase-item) — without these, new shapes silently fall through
    // to "simple-linear-acceptance.json", a misleading default.
    // =========================================================================

    [Fact]
    public void SuggestFilename_WithUseAction_ReturnsWithUseAction()
    {
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["step:talk", "step:travel", "step:use-action"],
            QuestFile: "quests/arr/cls/65586-mrd-axe-in-the-stone.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        Assert.Equal("with-use-action.json", extractor.SuggestFilename(fixture));
    }

    [Fact]
    public void SuggestFilename_WithUseEmote_ReturnsWithUseEmote()
    {
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["step:talk", "step:travel", "step:use-emote"],
            QuestFile: "quests/arr/sid/12345-cheer-the-recruit.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        Assert.Equal("with-use-emote.json", extractor.SuggestFilename(fixture));
    }

    [Fact]
    public void SuggestFilename_WithTeleport_ReturnsWithTeleport()
    {
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["step:talk", "step:teleport", "step:travel"],
            QuestFile: "quests/arr/sid/22222-go-far.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        Assert.Equal("with-teleport.json", extractor.SuggestFilename(fixture));
    }

    [Fact]
    public void SuggestFilename_WithPurchaseItem_ReturnsWithPurchaseItem()
    {
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["step:purchase-item", "step:talk", "step:travel"],
            QuestFile: "quests/arr/gc/33333-buy-cesti.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        Assert.Equal("with-purchase-item.json", extractor.SuggestFilename(fixture));
    }

    // Distinguishing-capability fallback: a fixture with multiple new shapes picks
    // by priority. use-action > use-emote (combat actions are more distinguishing
    // than emotes for fixture identity).
    [Fact]
    public void SuggestFilename_MultipleNewShapes_PicksByPriority()
    {
        var extractor = new TraceToFixtureExtractor();
        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: "TODO",
            InitialState: "fresh",
            Capabilities: ["step:talk", "step:travel", "step:use-action", "step:use-emote"],
            QuestFile: "quests/UNKNOWN.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");

        // No exact match → fallback by priority — use-action wins
        Assert.Equal("with-use-action.json", extractor.SuggestFilename(fixture));
    }
}
