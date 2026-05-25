using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Fixture;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED PHASE: Group F — co-emission behavioral tests (F9a, F9b) plus flag-parsing tests (F9, F9b-flag, F10).
/// Per FIXTURE_REPLAY_HARNESS_PLAN.md §5 Group F.
///
/// F9a / F9b are the NEW behavioral tests targeting FilterToFixtureRun, which does not exist yet.
/// These produce CS0117 compile errors until the Builder adds the method.
///
/// The existing flag-parsing tests (F9, F9b-flag, F10) are kept because CliArgs.WithTrace already
/// exists and they remain valid GREEN regression guards for flag parsing.
///
/// F9c (file co-emit writes filtered trace) and F10 (no-trace suppresses file) are NOT added here
/// because RunExtractFixture lives in qf-trace (a separate CLI project, not referenced by this test
/// project) and adding InternalsVisibleTo was explicitly prohibited. The builder must gate
/// RunExtractFixture on cliArgs.WithTrace; FilterToFixtureRun unit tests (F9a/F9b) cover the logic.
///
/// Expected RED: CS0117 on TraceToFixtureExtractor.FilterToFixtureRun (does not exist yet).
/// </summary>
public sealed class FixtureHarnessCliTests
{
    // =====================================================================
    // F9a — FilterToFixtureRun drops foreign runs and noise, keeps first run only
    // =====================================================================

    /// <summary>
    /// F9a — FilterToFixtureRun returns only events whose RunId equals the FIRST RunStartEvent's RunId.
    /// Noise (runId="inspect") and a second run (runId="B") must be excluded.
    /// </summary>
    [Fact]
    public void F9a_FilterToFixtureRun_DropsNoiseAndForeignRuns_KeepsFirstRunOnly()
    {
        // Given a mixed event list:
        //   - run A (first run.start): run.start + 2 observations + 1 decision + run.end
        //   - noise:  ObservationEvent with runId="inspect" (passive-mode noise)
        //   - run B (second run.start): run.start + 1 observation + run.end
        //   All interleaved to prove the filter doesn't rely on contiguous grouping.
        //
        // When FilterToFixtureRun(events),
        // Then result contains ONLY the 5 runId="A" events, in document order.
        //      NO inspect or B events appear.
        //
        // RED: TraceToFixtureExtractor.FilterToFixtureRun does not exist → CS0117.

        var runAStart   = Start(runId: "A", questId: 65644);
        var runAObs1    = Obs("GetPlayerZone", null, ZoneValue(182), runId: "A", offsetSeconds: 0.1);
        var inspectNoise = Obs("GetPlayerZone", null, ZoneValue(999), runId: "inspect", offsetSeconds: 0.2);
        var runAObs2    = Obs("IsQuestComplete", QuestIdArg(65644), BoolValue(false), runId: "A", offsetSeconds: 0.3);
        var runADecision = Decision("travel-to-npc", "navigate", runId: "A", offsetSeconds: 1.0);
        var runBStart   = Start(runId: "B", questId: 65644);
        var runBObs     = Obs("GetPlayerZone", null, ZoneValue(182), runId: "B", offsetSeconds: 1.5);
        var runAEnd     = End(outcome: "done", runId: "A", offsetSeconds: 2.0);
        var runBEnd     = End(outcome: "done", runId: "B", offsetSeconds: 3.0);

        var events = new List<TraceEvent>
        {
            runAStart,
            runAObs1,
            inspectNoise,
            runAObs2,
            runADecision,
            runBStart,
            runBObs,
            runAEnd,
            runBEnd,
        };

        // CS0117: TraceToFixtureExtractor does not contain a definition for 'FilterToFixtureRun'
        var result = TraceToFixtureExtractor.FilterToFixtureRun(events);

        // Assert: exactly the 5 run-A events, in original document order
        Assert.Equal(5, result.Count);
        Assert.Same(runAStart,    result[0]);
        Assert.Same(runAObs1,     result[1]);
        Assert.Same(runAObs2,     result[2]);
        Assert.Same(runADecision, result[3]);
        Assert.Same(runAEnd,      result[4]);

        // Confirm neither noise nor run-B events leaked through
        Assert.DoesNotContain(result, e => e is ObservationEvent obs && obs.RunId == "inspect");
        Assert.DoesNotContain(result, e => e is RunStartEvent rs  && rs.RunId  == "B");
        Assert.DoesNotContain(result, e => e is RunEndEvent   re  && re.RunId  == "B");
    }

    // =====================================================================
    // F9b — FilterToFixtureRun with no RunStartEvent → empty list
    // =====================================================================

    /// <summary>
    /// F9b — When the event list contains no RunStartEvent, FilterToFixtureRun returns an empty list
    /// (no RunId to select, so no events qualify).
    /// </summary>
    [Fact]
    public void F9b_FilterToFixtureRun_NoRunStart_ReturnsEmptyList()
    {
        // Given a list with only ObservationEvents and a DecisionEvent — no RunStartEvent,
        // When FilterToFixtureRun,
        // Then result is empty (no run.start → no runId to filter by).
        //
        // RED: TraceToFixtureExtractor.FilterToFixtureRun does not exist → CS0117.

        var events = new List<TraceEvent>
        {
            Obs("GetPlayerZone", null, ZoneValue(182), runId: "orphan", offsetSeconds: 0.1),
            Decision("step-x", "navigate", runId: "orphan", offsetSeconds: 1.0),
            Obs("IsQuestComplete", QuestIdArg(66130), BoolValue(false), runId: "orphan", offsetSeconds: 1.5),
        };

        // CS0117: FilterToFixtureRun does not exist yet
        var result = TraceToFixtureExtractor.FilterToFixtureRun(events);

        Assert.Empty(result);
    }
    /// <summary>F9 — extract-fixture without --no-trace defaults WithTrace to true.</summary>
    [Fact]
    public void F9_CliArgs_WithTrace_DefaultsToTrue_ForExtractFixture()
    {
        // Given args ["extract-fixture", "run.jsonl"] (no --no-trace flag),
        // When CliArgsParser.Parse,
        // Then args.WithTrace == true (co-emission is ON by default for extract-fixture).
        //
        // RED: CliArgs.WithTrace does not exist → CS1061 compile error.

        var args = CliArgsParser.Parse(["extract-fixture", "run.jsonl"]);

        // CS1061: 'CliArgs' does not contain a definition for 'WithTrace'
        Assert.True(args.WithTrace);
    }

    /// <summary>F10 — --no-trace flag suppresses co-emission (sets WithTrace to false).</summary>
    [Fact]
    public void F10_CliArgs_NoTraceFlag_SetsWithTraceToFalse()
    {
        // Given args ["extract-fixture", "run.jsonl", "--no-trace"],
        // When CliArgsParser.Parse,
        // Then args.WithTrace == false AND ParseError == null (recognised flag).
        //
        // RED: CliArgs.WithTrace does not exist → CS1061. Also --no-trace is
        //      currently an unknown flag → ParseError would be set (second assertion also RED).

        var args = CliArgsParser.Parse(["extract-fixture", "run.jsonl", "--no-trace"]);

        // CS1061: WithTrace
        Assert.False(args.WithTrace);
        Assert.Null(args.ParseError);
    }

    /// <summary>F9b — explicit --with-trace flag is recognised (no parse error).</summary>
    [Fact]
    public void F9b_CliArgs_ExplicitWithTraceFlag_IsRecognised()
    {
        // Given args ["extract-fixture", "run.jsonl", "--with-trace"],
        // When CliArgsParser.Parse,
        // Then args.WithTrace == true and ParseError == null.
        //
        // RED: CliArgs.WithTrace does not exist → CS1061. Also --with-trace is
        //      currently an unknown flag → ParseError is set (second assertion also RED).

        var args = CliArgsParser.Parse(["extract-fixture", "run.jsonl", "--with-trace"]);

        // CS1061: WithTrace
        Assert.True(args.WithTrace);
        Assert.Null(args.ParseError);
    }
}
