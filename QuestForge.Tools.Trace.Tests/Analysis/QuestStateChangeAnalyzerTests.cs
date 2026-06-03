using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Analysis;
using QuestForge.Tools.Trace.Cli;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests.Analysis;

public class QuestStateChangeAnalyzerTests
{
    private readonly QuestStateChangeAnalyzer _analyzer = new();

    private static string FormatChanges(QuestStateChangeReport report)
        => string.Join("; ", report.Changes.Select(c =>
            $"[{c.Kind}] {c.PreviousValue}->{c.NewValue} (detail={c.Detail}, step={c.AfterStepId}, action={c.AfterActionType})"));

    // =========================================================================
    // T01 -- Happy path: sequence changes with decision correlation
    // =========================================================================

    [Fact]
    public void T01_SequenceChange_CorrelatedWithDecision()
    {
        // CONTRACT: Given a trace with RunStart(65644), a Decision, baseline seq=0,
        //           then seq=1, When Analyze is called, Then one change is emitted
        //           with Kind="sequence", PreviousValue="0", NewValue="1",
        //           Detail=null, AfterStepId="accept-quest", AfterActionType="Interact".

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Decision("accept-quest", "Interact"),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Equal(65644u, report.QuestId);
        Assert.Single(report.Changes);
        var c = report.Changes[0];
        Assert.Equal("sequence", c.Kind);
        Assert.Equal("0", c.PreviousValue);
        Assert.Equal("1", c.NewValue);
        Assert.Null(c.Detail);
        Assert.Equal("accept-quest", c.AfterStepId);
        Assert.Equal("Interact", c.AfterActionType);
    }

    // =========================================================================
    // T02 -- Happy path: flags change with bit diff
    // =========================================================================

    [Fact]
    public void T02_FlagsChange_BitDiffDetail()
    {
        // CONTRACT: Given baseline flags=0 then flags=4, When Analyze,
        //           Then one change with Kind="flags", Detail="bit 2 set".

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Decision("travel-to-camp", "Navigate"),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(0)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(4)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        var c = report.Changes[0];
        Assert.Equal("flags", c.Kind);
        Assert.Equal("0", c.PreviousValue);
        Assert.Equal("4", c.NewValue);
        Assert.Equal("bit 2 set", c.Detail);
        Assert.Equal("travel-to-camp", c.AfterStepId);
    }

    // =========================================================================
    // T03 -- Happy path: variables change
    // =========================================================================

    [Fact]
    public void T03_VariablesChange_ArrayDiff()
    {
        // CONTRACT: Given baseline vars=[0,0,0,0,0,0] then [1,0,0,0,0,0],
        //           When Analyze, Then one change with Kind="variables",
        //           PreviousValue="[0,0,0,0,0,0]", NewValue="[1,0,0,0,0,0]", Detail=null.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Decision("use-item", "UseItem"),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new int[] { 0, 0, 0, 0, 0, 0 })),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new int[] { 1, 0, 0, 0, 0, 0 })),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        var c = report.Changes[0];
        Assert.Equal("variables", c.Kind);
        Assert.Equal("[0,0,0,0,0,0]", c.PreviousValue);
        Assert.Equal("[1,0,0,0,0,0]", c.NewValue);
        Assert.Null(c.Detail);
        Assert.Equal("use-item", c.AfterStepId);
    }

    // =========================================================================
    // T04 -- First observation is baseline, no change emitted
    // =========================================================================

    [Fact]
    public void T04_FirstObservation_EstablishesBaseline_NoChange()
    {
        // CONTRACT: Given RunStart + single GetQuestSequence observation,
        //           When Analyze, Then zero changes (baseline only).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Equal(65644u, report.QuestId);
        Assert.Empty(report.Changes);
    }

    // =========================================================================
    // T05 -- No RunStartEvent: QuestId=null, empty changes
    // =========================================================================

    [Fact]
    public void T05_NoRunStart_QuestIdNull_EmptyChanges()
    {
        // CONTRACT: Given events without RunStartEvent,
        //           When Analyze, Then QuestId=null, zero changes.

        var events = new List<TraceEvent>
        {
            Decision("s1", "Navigate"),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Null(report.QuestId);
        Assert.Empty(report.Changes);
    }

    // =========================================================================
    // T06 -- Quest ID filtering: other quests ignored
    // =========================================================================

    [Fact]
    public void T06_ObservationsForOtherQuest_Ignored()
    {
        // CONTRACT: Given RunStart(65644), baseline seq=0 for 65644, seq=5 for 99999,
        //           then seq=1 for 65644, When Analyze, Then one change (0->1 for 65644).
        //           The observation for quest 99999 is ignored.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(99999), IntValue(5)),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        var c = report.Changes[0];
        Assert.Equal("0", c.PreviousValue);
        Assert.Equal("1", c.NewValue);
    }

    // =========================================================================
    // T07 -- Repeated same value: no duplicate change
    // =========================================================================

    [Fact]
    public void T07_RepeatedSameValue_NoDuplicateChange()
    {
        // CONTRACT: Given baseline seq=0, then seq=0 again, then seq=1,
        //           When Analyze, Then exactly one change (0->1).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("1", report.Changes[0].NewValue);
    }

    // =========================================================================
    // T08 -- Multiple flags bits set and cleared
    // =========================================================================

    [Fact]
    public void T08_FlagsBits_SetAndCleared()
    {
        // CONTRACT: Given flags 5 (bits 0,2) -> 18 (bits 1,4),
        //           Then Detail = "bits 1,4 set; bits 0,2 cleared".
        //           Set = 18 & ~5 = 18 (bits 1,4). Cleared = 5 & ~18 = 5 (bits 0,2).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(5)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(18)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        var c = report.Changes[0];
        Assert.Equal("5", c.PreviousValue);
        Assert.Equal("18", c.NewValue);
        Assert.Equal("bits 1,4 set; bits 0,2 cleared", c.Detail);
    }

    // =========================================================================
    // T09 -- No decision before first change: null correlation
    // =========================================================================

    [Fact]
    public void T09_NoDecisionBeforeChange_NullCorrelation()
    {
        // CONTRACT: Given no DecisionEvent before the observation that triggers a change,
        //           When Analyze, Then AfterStepId=null, AfterActionType=null.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Null(report.Changes[0].AfterStepId);
        Assert.Null(report.Changes[0].AfterActionType);
    }

    // =========================================================================
    // T10 -- Multiple decisions: each change correlates with most recent
    // =========================================================================

    [Fact]
    public void T10_MultipleDecisions_EachChangeCorrelatesWithMostRecent()
    {
        // CONTRACT: Given baseline seq=0, Decision(s1/Interact), seq=1,
        //           Decision(s2/Navigate), seq=2, When Analyze,
        //           Then first change (0->1) has AfterStepId="s1",
        //           second change (1->2) has AfterStepId="s2".

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Decision("s1", "Interact"),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
            Decision("s2", "Navigate"),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(2)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Equal(2, report.Changes.Count);
        Assert.Equal("s1", report.Changes[0].AfterStepId);
        Assert.Equal("Interact", report.Changes[0].AfterActionType);
        Assert.Equal("s2", report.Changes[1].AfterStepId);
        Assert.Equal("Navigate", report.Changes[1].AfterActionType);
    }

    // =========================================================================
    // T11 -- Mixed state types: all 3 reported in chronological order
    // =========================================================================

    [Fact]
    public void T11_MixedStateTypes_AllThreeReportedChronologically()
    {
        // CONTRACT: Given sequence, flags, and variables all changing,
        //           When Analyze, Then three changes in event-list order.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            // Baselines
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(0)),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new int[] { 0, 0, 0, 0, 0, 0 })),
            // Changes
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(4)),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new int[] { 1, 0, 0, 0, 0, 0 })),
        };

        var report = _analyzer.Analyze(events);

        Assert.Equal(3, report.Changes.Count);
        Assert.Equal("sequence", report.Changes[0].Kind);
        Assert.Equal("flags", report.Changes[1].Kind);
        Assert.Equal("variables", report.Changes[2].Kind);
    }

    // =========================================================================
    // T12 -- Seq field is TraceEvent.Seq, not quest sequence value
    // =========================================================================

    [Fact]
    public void T12_SeqField_IsTraceEventSeq_NotQuestSequenceValue()
    {
        // CONTRACT: Given observation at TraceEvent.Seq=12 that changes quest seq 0->1,
        //           When Analyze, Then change.Seq == 12 (not 1).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)) with { Seq = 5 },
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)) with { Seq = 12 },
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal(12, report.Changes[0].Seq);
    }

    // =========================================================================
    // T13 -- Value-wrapped GetQuestSequence: {"value": N} shape parsed
    // =========================================================================

    [Fact]
    public void T13_ValueWrappedSequence_ParsedCorrectly()
    {
        // CONTRACT: Given GetQuestSequence values as {"value":0} then {"value":1},
        //           When Analyze, Then one change (0->1).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = 0 })),
            Obs("GetQuestSequence", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = 1 })),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("sequence", report.Changes[0].Kind);
        Assert.Equal("0", report.Changes[0].PreviousValue);
        Assert.Equal("1", report.Changes[0].NewValue);
    }

    // =========================================================================
    // T14 -- Plain-number quest argument (old trace format)
    // =========================================================================

    [Fact]
    public void T14_PlainNumberArgument_MatchesQuestId()
    {
        // CONTRACT: Given Argument as plain 65644 (not {"value":65644}),
        //           When Analyze, Then quest ID matches and change is recorded.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", PlainNumber(65644), IntValue(0)),
            Obs("GetQuestSequence", PlainNumber(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("0", report.Changes[0].PreviousValue);
        Assert.Equal("1", report.Changes[0].NewValue);
    }

    // =========================================================================
    // T15 -- Variables: value-wrapped shape
    // =========================================================================

    [Fact]
    public void T15_VariablesValueWrapped_ParsedCorrectly()
    {
        // CONTRACT: Given GetQuestVariables values as {"value":[0,0,0,0,0,0]}
        //           then {"value":[1,0,0,0,0,0]}, When Analyze, Then one change.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = new int[] { 0, 0, 0, 0, 0, 0 } })),
            Obs("GetQuestVariables", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = new int[] { 1, 0, 0, 0, 0, 0 } })),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("variables", report.Changes[0].Kind);
        Assert.Equal("[0,0,0,0,0,0]", report.Changes[0].PreviousValue);
        Assert.Equal("[1,0,0,0,0,0]", report.Changes[0].NewValue);
    }

    // =========================================================================
    // T16 -- FormatStateChanges: no run.start
    // =========================================================================

    [Fact]
    public void T16_FormatStateChanges_NoRunStart()
    {
        // CONTRACT: Given report with QuestId=null,
        //           When FormatStateChanges, Then output is the "no run.start" message.

        var report = new QuestStateChangeReport(null, []);

        var output = OutputFormatters.FormatStateChanges(report);

        Assert.Equal("No run.start found; cannot determine quest ID.", output);
    }

    // =========================================================================
    // T17 -- FormatStateChanges: no changes
    // =========================================================================

    [Fact]
    public void T17_FormatStateChanges_NoChanges()
    {
        // CONTRACT: Given report with QuestId=65644 and empty changes,
        //           When FormatStateChanges, Then output shows quest ID and "(none)".

        var report = new QuestStateChangeReport(65644, []);

        var output = OutputFormatters.FormatStateChanges(report);

        Assert.Equal("Quest 65644 state changes:\n  (none)", output);
    }

    // =========================================================================
    // T18 -- FormatStateChanges: full output with change details
    // =========================================================================

    [Fact]
    public void T18_FormatStateChanges_FullOutput()
    {
        // CONTRACT: Given report with one sequence change (seq=12, 0->1, after s1/Interact),
        //           When FormatStateChanges, Then output contains quest header,
        //           seq number, kind, value transition, and decision correlation.

        var report = new QuestStateChangeReport(65644, [
            new QuestStateChange(
                Seq: 12,
                Kind: "sequence",
                PreviousValue: "0",
                NewValue: "1",
                Detail: null,
                AfterStepId: "s1",
                AfterActionType: "Interact"),
        ]);

        var output = OutputFormatters.FormatStateChanges(report);

        Assert.Contains("Quest 65644 state changes:", output);
        Assert.Contains("seq 12", output);
        Assert.Contains("sequence", output);
        Assert.Contains("0->1", output);
        Assert.Contains("after: s1 / Interact", output);
    }

    // =========================================================================
    // T19 -- CliArgsParser: "state-changes" recognized
    // =========================================================================

    [Fact]
    public void T19_CliArgsParser_StateChangesRecognized()
    {
        // CONTRACT: Given args ["state-changes", "trace.jsonl"],
        //           When Parse, Then Subcommand == StateChanges, TracePath == "trace.jsonl".

        var result = CliArgsParser.Parse(["state-changes", "trace.jsonl"]);

        Assert.Equal(CliSubcommand.StateChanges, result.Subcommand);
        Assert.Equal("trace.jsonl", result.TracePath);
    }

    // =========================================================================
    // T20 -- Flags value-wrapped shape
    // =========================================================================

    [Fact]
    public void T20_FlagsValueWrapped_ParsedCorrectly()
    {
        // CONTRACT: Given GetQuestFlags values as {"value":0} then {"value":4},
        //           When Analyze, Then one change with Detail="bit 2 set".

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestFlags", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = 0u })),
            Obs("GetQuestFlags", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { value = 4u })),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("flags", report.Changes[0].Kind);
        Assert.Equal("bit 2 set", report.Changes[0].Detail);
    }

    // =========================================================================
    // T21 -- Failure-shaped observation value is ignored
    // =========================================================================

    [Fact]
    public void T21_FailureShapedValue_Ignored()
    {
        // CONTRACT: Given baseline seq=0, then a failure-shaped observation,
        //           then seq=1, When Analyze, Then one change (0->1).
        //           The failure observation does not update baseline or emit a change.

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(0)),
            Obs("GetQuestSequence", QuestIdArg(65644),
                JsonSerializer.SerializeToElement(new { failure = "timeout" })),
            Obs("GetQuestSequence", QuestIdArg(65644), IntValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("0", report.Changes[0].PreviousValue);
        Assert.Equal("1", report.Changes[0].NewValue);
    }

    // =========================================================================
    // T22 -- Single bit set: singular "bit" label
    // =========================================================================

    [Fact]
    public void T22_SingleBitSet_SingularLabel()
    {
        // CONTRACT: Given flags 0 -> 1 (bit 0 set),
        //           When Analyze, Then Detail is "bit 0 set" (singular).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(0)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(1)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("bit 0 set", report.Changes[0].Detail);
    }

    // =========================================================================
    // T23 -- Multiple bits set: plural "bits" label
    // =========================================================================

    [Fact]
    public void T23_MultipleBitsSet_PluralLabel()
    {
        // CONTRACT: Given flags 0 -> 6 (bits 1,2 set),
        //           When Analyze, Then Detail is "bits 1,2 set" (plural).

        var events = new List<TraceEvent>
        {
            Start(questId: 65644),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(0)),
            Obs("GetQuestFlags", QuestIdArg(65644), UintValue(6)),
        };

        var report = _analyzer.Analyze(events);

        Assert.Single(report.Changes);
        Assert.Equal("bits 1,2 set", report.Changes[0].Detail);
    }
}
