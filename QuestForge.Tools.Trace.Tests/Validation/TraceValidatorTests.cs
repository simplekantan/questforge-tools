using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Validation;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Validation;

/// <summary>
/// Tests for <see cref="TraceValidator"/>.
/// Scenarios T1--T27 from TRACE_VALIDATE_PLAN.md.
/// All tests are RED: they will fail until Builder implements TraceValidator.
/// </summary>
public sealed class TraceValidatorTests
{
    private static readonly JsonSerializerOptions Opts = TraceEventJsonContext.Default.Options;

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>Serialize a TraceEvent to a JSONL line.</summary>
    private static string Line(TraceEvent e) =>
        JsonSerializer.Serialize(e, e.GetType(), Opts);

    /// <summary>Build a run.start line with explicit seq/ts/runId.</summary>
    private static string RunStartLine(int seq = 0, long ts = 0, string runId = "aabbccdd") =>
        $"{{\"v\":1,\"seq\":{seq},\"ts\":{ts},\"type\":\"run.start\",\"runId\":\"{runId}\",\"data\":{{\"questId\":66130}}}}";

    /// <summary>Build a run.end line with explicit seq/ts/runId.</summary>
    private static string RunEndLine(int seq = 1, long ts = 100, string runId = "aabbccdd") =>
        $"{{\"v\":1,\"seq\":{seq},\"ts\":{ts},\"type\":\"run.end\",\"runId\":\"{runId}\",\"data\":{{\"outcome\":\"done\"}}}}";

    /// <summary>Build a decision line with explicit seq/ts/runId.</summary>
    private static string DecisionLine(int seq = 1, long ts = 100, string runId = "aabbccdd") =>
        $"{{\"v\":1,\"seq\":{seq},\"ts\":{ts},\"type\":\"decision\",\"runId\":\"{runId}\",\"data\":{{\"stepId\":\"s1\",\"actionType\":\"navigate\"}}}}";

    /// <summary>Build an observation line with explicit seq/ts/runId.</summary>
    private static string ObsLine(int seq = 1, long ts = 50, string runId = "aabbccdd") =>
        $"{{\"v\":1,\"seq\":{seq},\"ts\":{ts},\"type\":\"observation\",\"runId\":\"{runId}\",\"data\":{{\"method\":\"GetZone\",\"argument\":null,\"value\":{{\"value\":181}}}}}}";

    /// <summary>Format all issues for assertion failure messages.</summary>
    private static string FormatIssues(TraceValidationResult result) =>
        string.Join("; ", result.Errors.Concat(result.Warnings)
            .Select(i => $"[{i.Code}] L{i.LineNumber}: {i.Message}"));

    /// <summary>Assert exactly one error with the given code.</summary>
    private static void AssertSingleError(TraceValidationResult result, string code)
    {
        Assert.Single(result.Errors, e => e.Code == code);
        Assert.Equal(1, result.Errors.Count);
    }

    /// <summary>Assert exactly one warning with the given code.</summary>
    private static void AssertSingleWarning(TraceValidationResult result, string code)
    {
        Assert.Single(result.Warnings, w => w.Code == code);
        Assert.Equal(1, result.Warnings.Count);
    }

    // -------------------------------------------------------------------
    // T1 -- Happy path: valid 3-event trace
    // -------------------------------------------------------------------

    [Fact]
    public void T01_ValidTrace_IsClean()
    {
        // CONTRACT: Given a valid trace with run.start (seq=0), decision (seq=1),
        //           run.end (seq=2) -- all with consistent runId, monotonic ts, v=1,
        //           When Validate(lines),
        //           Then result.IsClean == true, zero errors, zero warnings.
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 100),
            RunEndLine(seq: 2, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsClean, FormatIssues(result));
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    // -------------------------------------------------------------------
    // T2 -- TV-W003: empty trace
    // -------------------------------------------------------------------

    [Fact]
    public void T02_EmptyTrace_WarnsW003()
    {
        // CONTRACT: Given an empty list [],
        //           When Validate(lines),
        //           Then exactly one TV-W003 warning, zero errors, IsValid == true.
        var validator = new TraceValidator();
        var result = validator.Validate(Array.Empty<string>());

        Assert.True(result.IsValid, FormatIssues(result));
        AssertSingleWarning(result, "TV-W003");
        Assert.Empty(result.Errors);
    }

    // -------------------------------------------------------------------
    // T3 -- TV-E001: malformed JSON
    // -------------------------------------------------------------------

    [Fact]
    public void T03_MalformedJson_ErrorE001()
    {
        // CONTRACT: Given lines [validRunStart, "not json {{{", validRunEnd],
        //           When Validate(lines),
        //           Then TV-E001 on line 2 with LineNumber == 2.
        //           Also TV-E004 on line 3 (seq 2 but expected 1 -- malformed line
        //           does not advance expectedSeq per TV8).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            "not json {{{",
            RunEndLine(seq: 2, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid, "Trace with malformed JSON should not be valid");
        var e001 = Assert.Single(result.Errors.Where(e => e.Code == "TV-E001"));
        Assert.Equal(2, e001.LineNumber);
        // Also expect TV-E004 on line 3 because malformed line didn't advance seq
        Assert.Contains(result.Errors, e => e.Code == "TV-E004" && e.LineNumber == 3);
    }

    // -------------------------------------------------------------------
    // T4 -- TV-E002: missing v field
    // -------------------------------------------------------------------

    [Fact]
    public void T04_MissingVField_ErrorE002()
    {
        // CONTRACT: Given a line missing the "v" field,
        //           When Validate(lines),
        //           Then error TV-E002 on line 1.
        var lines = new[]
        {
            """{"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E002" && e.LineNumber == 1);
    }

    // -------------------------------------------------------------------
    // T5 -- TV-E002: v is a string not integer
    // -------------------------------------------------------------------

    [Fact]
    public void T05_VIsString_ErrorE002()
    {
        // CONTRACT: Given a line where v is "one" (string, not integer),
        //           When Validate(lines),
        //           Then error TV-E002 on line 1.
        var lines = new[]
        {
            """{"v":"one","seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E002" && e.LineNumber == 1);
    }

    // -------------------------------------------------------------------
    // T5b -- TV-E003: v is 2 (wrong version)
    // -------------------------------------------------------------------

    [Fact]
    public void T05b_WrongVValue_ErrorE003()
    {
        // CONTRACT: Given a line with v=2,
        //           When Validate(lines),
        //           Then error TV-E003 on line 1. Message mentions "expected 1".
        var lines = new[]
        {
            """{"v":2,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        var e003 = Assert.Single(result.Errors.Where(e => e.Code == "TV-E003"));
        Assert.Equal(1, e003.LineNumber);
        Assert.Contains("expected 1", e003.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------
    // T6 -- Blank lines skipped silently
    // -------------------------------------------------------------------

    [Fact]
    public void T06_BlankLinesSkipped()
    {
        // CONTRACT: Given valid run.start (seq 0) on line 1, blank line on line 2,
        //           valid decision (seq 1) on line 3,
        //           When Validate(lines),
        //           Then no errors from the blank line; seq validates correctly.
        //           TV-W002 warning (no run.end) but no seq errors.
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            "",
            DecisionLine(seq: 1, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        Assert.Empty(result.Errors);
        // TV-W002 because no run.end
        Assert.Contains(result.Warnings, w => w.Code == "TV-W002");
    }

    // -------------------------------------------------------------------
    // T7 -- Mixed valid and malformed: errors for malformed, valid lines still checked
    // -------------------------------------------------------------------

    [Fact]
    public void T07_MixedValidAndMalformed_AllReported()
    {
        // CONTRACT: Given four lines: valid run.start, malformed JSON,
        //           line with wrong runId, valid decision with correct seq,
        //           When Validate(lines),
        //           Then TV-E001 on malformed line AND TV-E005 on wrong-runId line.
        //           Both reported (validator doesn't bail on first error).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0, runId: "aaaa0000"),
            "not json at all",
            // seq=1 here but expectedSeq is still 1 (malformed didn't advance) -- this is correct
            $"{{\"v\":1,\"seq\":1,\"ts\":200,\"type\":\"observation\",\"runId\":\"WRONG_ID\",\"data\":{{\"method\":\"x\",\"argument\":null,\"value\":null}}}}",
            DecisionLine(seq: 2, ts: 300, runId: "aaaa0000"),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E001" && e.LineNumber == 2);
        Assert.Contains(result.Errors, e => e.Code == "TV-E005" && e.LineNumber == 3);
    }

    // -------------------------------------------------------------------
    // T9 -- TV-E004: seq starts at 1 instead of 0
    // -------------------------------------------------------------------

    [Fact]
    public void T09_SeqStartsAt1_ErrorE004()
    {
        // CONTRACT: Given a single line with seq=1,
        //           When Validate(lines),
        //           Then TV-E004 (expected 0, got 1).
        //           Also TV-E006 (seq 0 is not run.start because there is no seq 0).
        var lines = new[]
        {
            $"{{\"v\":1,\"seq\":1,\"ts\":0,\"type\":\"run.start\",\"runId\":\"aabbccdd\",\"data\":{{\"questId\":66130}}}}"
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E004" && e.LineNumber == 1);
        Assert.Contains(result.Errors, e => e.Code == "TV-E006");
    }

    // -------------------------------------------------------------------
    // T10 -- Multiple errors in same trace: all reported
    // -------------------------------------------------------------------

    [Fact]
    public void T10_MultipleErrors_AllReported()
    {
        // CONTRACT: Given a trace with multiple distinct issues,
        //           When Validate(lines),
        //           Then all errors are reported, not just the first.
        var lines = new[]
        {
            // seq 0 but type is decision (not run.start) -> TV-E006
            DecisionLine(seq: 0, ts: 0, runId: "aabbccdd"),
            // wrong runId -> TV-E005
            DecisionLine(seq: 1, ts: 100, runId: "DIFFERENT"),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2, $"Expected at least 2 errors, got {result.Errors.Count}: {FormatIssues(result)}");
        Assert.Contains(result.Errors, e => e.Code == "TV-E006");
        Assert.Contains(result.Errors, e => e.Code == "TV-E005");
    }

    // -------------------------------------------------------------------
    // T13 -- TV-E004: seq gap (0,1,3)
    // -------------------------------------------------------------------

    [Fact]
    public void T13_SeqGap_ErrorE004()
    {
        // CONTRACT: Given three lines with seq 0, 1, 3 (gap at 3),
        //           When Validate(lines),
        //           Then exactly one TV-E004 error on the line with seq 3.
        //           Message mentions "expected 2".
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 100),
            RunEndLine(seq: 3, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        var e004 = Assert.Single(result.Errors.Where(e => e.Code == "TV-E004"));
        Assert.Equal(3, e004.LineNumber);
        Assert.Contains("expected 2", e004.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------
    // T14 -- TV-E004: seq duplicate (0,1,1,2)
    // -------------------------------------------------------------------

    [Fact]
    public void T14_SeqDuplicate_ErrorE004()
    {
        // CONTRACT: Given four lines with seq 0, 1, 1, 2 (repeat at line 3),
        //           When Validate(lines),
        //           Then exactly one TV-E004 error (validator resyncs per TV10, no cascade).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 100),
            ObsLine(seq: 1, ts: 150),       // duplicate
            RunEndLine(seq: 2, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        var seqErrors = result.Errors.Where(e => e.Code == "TV-E004").ToList();
        Assert.Single(seqErrors);
        Assert.Equal(3, seqErrors[0].LineNumber);
    }

    // -------------------------------------------------------------------
    // T15 -- TV-E004: seq decrease (0,1,2,1)
    // -------------------------------------------------------------------

    [Fact]
    public void T15_SeqDecrease_ErrorE004()
    {
        // CONTRACT: Given four lines with seq 0, 1, 2, 1 (decrease at line 4),
        //           When Validate(lines),
        //           Then TV-E004 on line 4.
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 100),
            ObsLine(seq: 2, ts: 150),
            DecisionLine(seq: 1, ts: 200),  // decrease
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E004" && e.LineNumber == 4);
    }

    // -------------------------------------------------------------------
    // T16 -- TV-E005: runId mismatch
    // -------------------------------------------------------------------

    [Fact]
    public void T16_RunIdMismatch_ErrorE005()
    {
        // CONTRACT: Given two lines where one event has a different runId,
        //           When Validate(lines),
        //           Then exactly one TV-E005 error on line 2.
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0, runId: "aaaa0000"),
            DecisionLine(seq: 1, ts: 100, runId: "bbbb1111"),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        var e005 = Assert.Single(result.Errors.Where(e => e.Code == "TV-E005"));
        Assert.Equal(2, e005.LineNumber);
    }

    // -------------------------------------------------------------------
    // T17 -- TV-E006: run.start not at seq 0
    // -------------------------------------------------------------------

    [Fact]
    public void T17_RunStartNotAtSeq0_ErrorE006()
    {
        // CONTRACT: Given two lines: seq 0 is decision, seq 1 is run.start,
        //           When Validate(lines),
        //           Then TV-E006 on line 1 (seq 0 is not run.start)
        //           AND TV-E006 on line 2 (run.start at seq != 0).
        var lines = new[]
        {
            DecisionLine(seq: 0, ts: 0),
            RunStartLine(seq: 1, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        var e006s = result.Errors.Where(e => e.Code == "TV-E006").ToList();
        Assert.True(e006s.Count >= 2, $"Expected at least 2 TV-E006 errors, got {e006s.Count}: {FormatIssues(result)}");
        Assert.Contains(e006s, e => e.LineNumber == 1);
        Assert.Contains(e006s, e => e.LineNumber == 2);
    }

    // -------------------------------------------------------------------
    // T18 -- Single event (run.start only) -> TV-W002
    // -------------------------------------------------------------------

    [Fact]
    public void T18_SingleRunStart_WarnsW002()
    {
        // CONTRACT: Given a single run.start event,
        //           When Validate(lines),
        //           Then TV-W002 warning (missing run.end), no errors.
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        AssertSingleWarning(result, "TV-W002");
    }

    // -------------------------------------------------------------------
    // T19 -- TV-W001: non-monotonic ts
    // -------------------------------------------------------------------

    [Fact]
    public void T19_NonMonotonicTs_WarnsW001()
    {
        // CONTRACT: Given three lines with ts values 0, 200, 100,
        //           When Validate(lines),
        //           Then exactly one TV-W001 warning on line 3.
        //           Message mentions "decreased from 200 to 100".
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 200),
            RunEndLine(seq: 2, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        var w001 = Assert.Single(result.Warnings.Where(w => w.Code == "TV-W001"));
        Assert.Equal(3, w001.LineNumber);
        Assert.Contains("200", w001.Message);
        Assert.Contains("100", w001.Message);
    }

    // -------------------------------------------------------------------
    // T20 -- TV-W002: missing run.end
    // -------------------------------------------------------------------

    [Fact]
    public void T20_MissingRunEnd_WarnsW002()
    {
        // CONTRACT: Given two lines: run.start at seq 0, decision at seq 1, no run.end,
        //           When Validate(lines),
        //           Then exactly one TV-W002 warning (no line number).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 1, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        var w002 = Assert.Single(result.Warnings.Where(w => w.Code == "TV-W002"));
        Assert.Null(w002.LineNumber);
    }

    // -------------------------------------------------------------------
    // T24 -- TV-E007: run.end not last event
    // -------------------------------------------------------------------

    [Fact]
    public void T24_RunEndNotLast_ErrorE007()
    {
        // CONTRACT: Given three lines: run.start (seq 0), run.end (seq 1),
        //           decision (seq 2) after run.end,
        //           When Validate(lines),
        //           Then TV-E007 error (run.end at event index 1 but last event is index 2).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            RunEndLine(seq: 1, ts: 100),
            DecisionLine(seq: 2, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E007");
    }

    // -------------------------------------------------------------------
    // T25 -- TV-E008: missing type field
    // -------------------------------------------------------------------

    [Fact]
    public void T25_MissingTypeField_ErrorE008()
    {
        // CONTRACT: Given a line missing the "type" field,
        //           When Validate(lines),
        //           Then TV-E008 on line 1.
        //           Also TV-E006 (seq 0 is not run.start since type is unknown).
        var lines = new[]
        {
            """{"v":1,"seq":0,"ts":0,"runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E008" && e.LineNumber == 1);
        Assert.Contains(result.Errors, e => e.Code == "TV-E006");
    }

    // -------------------------------------------------------------------
    // T_OnlyBlankLines -- TV-W003: only blank lines
    // -------------------------------------------------------------------

    [Fact]
    public void T_OnlyBlankLines_WarnsW003()
    {
        // CONTRACT: Given ["", "  ", "\t"],
        //           When Validate(lines),
        //           Then exactly one TV-W003 warning, zero errors.
        var lines = new[] { "", "  ", "\t" };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        AssertSingleWarning(result, "TV-W003");
        Assert.Empty(result.Errors);
    }

    // -------------------------------------------------------------------
    // T_NoRunStartAtAll -- TV-E006: no run.start
    // -------------------------------------------------------------------

    [Fact]
    public void T_NoRunStartAtAll_ErrorE006()
    {
        // CONTRACT: Given two lines: seq 0 with type decision, seq 1 with type run.end,
        //           When Validate(lines),
        //           Then TV-E006 on line 1 (seq 0 is not run.start).
        //           TV-W002 is NOT emitted (there IS a run.end).
        var lines = new[]
        {
            DecisionLine(seq: 0, ts: 0),
            RunEndLine(seq: 1, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E006" && e.LineNumber == 1);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "TV-W002");
    }

    // -------------------------------------------------------------------
    // T_SeqResyncPrevents Cascade -- TV10
    // -------------------------------------------------------------------

    [Fact]
    public void T_SeqResync_PreventsCascade()
    {
        // CONTRACT: Given lines with seq 0, 2, 3, 4 (gap at line 2, then correct
        //           progression after resync),
        //           When Validate(lines),
        //           Then exactly one TV-E004 on the line with seq 2.
        //           Lines with seq 3 and 4 produce no seq errors (resync per TV10).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            DecisionLine(seq: 2, ts: 100),  // gap: expected 1, got 2
            ObsLine(seq: 3, ts: 150),
            RunEndLine(seq: 4, ts: 200),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        var seqErrors = result.Errors.Where(e => e.Code == "TV-E004").ToList();
        Assert.Single(seqErrors);
        Assert.Equal(2, seqErrors[0].LineNumber);
    }

    // -------------------------------------------------------------------
    // T_RunEndAsLastIsValid -- run.start + run.end is clean for TV-E007
    // -------------------------------------------------------------------

    [Fact]
    public void T_RunEndAsLastIsValid()
    {
        // CONTRACT: Given two lines: run.start at seq 0, run.end at seq 1,
        //           When Validate(lines),
        //           Then zero errors. run.end IS the last event (TV-E007 not triggered).
        var lines = new[]
        {
            RunStartLine(seq: 0, ts: 0),
            RunEndLine(seq: 1, ts: 100),
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.True(result.IsValid, FormatIssues(result));
        Assert.Empty(result.Errors);
    }

    // -------------------------------------------------------------------
    // T_SeqIsString -- TV-E004: seq is not a number
    // -------------------------------------------------------------------

    [Fact]
    public void T_SeqIsString_ErrorE004()
    {
        // CONTRACT: Given a line where seq is "zero" (string),
        //           When Validate(lines),
        //           Then TV-E004 on line 1 (seq is not an integer).
        var lines = new[]
        {
            """{"v":1,"seq":"zero","ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E004" && e.LineNumber == 1);
    }

    // -------------------------------------------------------------------
    // T_SeqMissing -- TV-E004: seq field missing
    // -------------------------------------------------------------------

    [Fact]
    public void T_SeqMissing_ErrorE004()
    {
        // CONTRACT: Given a line missing the "seq" field entirely,
        //           When Validate(lines),
        //           Then TV-E004 on line 1.
        var lines = new[]
        {
            """{"v":1,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E004" && e.LineNumber == 1);
    }

    // -------------------------------------------------------------------
    // T_VValue99 -- TV-E003 for v=99 (wrong version)
    // -------------------------------------------------------------------

    [Fact]
    public void T_VValue99_ErrorE003()
    {
        // CONTRACT: Given a line with v=99,
        //           When Validate(lines),
        //           Then TV-E003 on line 1.
        var lines = new[]
        {
            """{"v":99,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}"""
        };

        var validator = new TraceValidator();
        var result = validator.Validate(lines);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E003" && e.LineNumber == 1);
    }
}
