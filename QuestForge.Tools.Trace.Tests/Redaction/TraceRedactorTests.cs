using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Redaction;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Redaction;

/// <summary>
/// Tests for <see cref="TraceRedactor"/>.
/// Scenarios T1--T21 from TRACE_REDACT_PLAN.md.
/// All tests are RED: they will fail until Builder implements TraceRedactor,
/// RedactionReport, and ExcludedFieldHit.
/// </summary>
public sealed class TraceRedactorTests
{
    private static readonly JsonSerializerOptions Opts = TraceEventJsonContext.Default.Options;

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>Serialize a TraceEvent to a JSONL line.</summary>
    private static string Line(TraceEvent e) =>
        JsonSerializer.Serialize(e, e.GetType(), Opts);

    /// <summary>Build a run.start line with wallClockUtc set to a real timestamp.</summary>
    private static string RunStartWithClock(string runId = "aabbccdd", uint questId = 66130)
    {
        // Hand-craft JSON to ensure wallClockUtc is present with a real value.
        return $"{{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"run.start\",\"runId\":\"{runId}\",\"data\":{{\"questId\":{questId},\"wallClockUtc\":\"2026-05-12T14:22:01.234Z\"}}}}";
    }

    /// <summary>Build a run.start line with wallClockUtc already null (pre-redacted).</summary>
    private static string RunStartRedacted(string runId = "aabbccdd", uint questId = 66130)
    {
        return $"{{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"run.start\",\"runId\":\"{runId}\",\"data\":{{\"questId\":{questId},\"wallClockUtc\":null}}}}";
    }

    /// <summary>Build a run.end line.</summary>
    private static string RunEndLine(string runId = "aabbccdd")
    {
        return $"{{\"v\":1,\"seq\":1,\"ts\":100,\"type\":\"run.end\",\"runId\":\"{runId}\",\"data\":{{\"outcome\":\"done\"}}}}";
    }

    /// <summary>Build a decision line.</summary>
    private static string DecisionLine(string runId = "aabbccdd")
    {
        return $"{{\"v\":1,\"seq\":2,\"ts\":200,\"type\":\"decision\",\"runId\":\"{runId}\",\"data\":{{\"stepId\":\"s1\",\"actionType\":\"navigate\"}}}}";
    }

    /// <summary>Build an observation line.</summary>
    private static string ObsLine(string runId = "aabbccdd")
    {
        return $"{{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"{runId}\",\"data\":{{\"method\":\"GetZone\",\"argument\":null,\"value\":{{\"value\":181}}}}}}";
    }

    /// <summary>Create a TraceRedactor and run Redact, returning (output lines, report).</summary>
    private static (string Output, RedactionReport Report) Redact(IReadOnlyList<string> lines)
    {
        var redactor = new TraceRedactor();
        var writer = new StringWriter { NewLine = "\n" };
        var report = redactor.Redact(lines, writer);
        return (writer.ToString(), report);
    }

    /// <summary>Split output back into lines for per-line assertions. Handles trailing newline.</summary>
    private static string[] OutputLines(string output)
    {
        if (string.IsNullOrEmpty(output)) return [];
        // Each line ends with \n, so split and drop the trailing empty entry.
        var parts = output.Split('\n');
        return parts.Length > 0 && parts[^1] == "" ? parts[..^1] : parts;
    }

    /// <summary>Format report for assertion failure messages.</summary>
    private static string FormatReport(RedactionReport r) =>
        $"TotalLines={r.TotalLines}, WallClockStripped={r.WallClockStripped}, " +
        $"AlreadyRedacted={r.AlreadyRedacted}, ExcludedHits={r.ExcludedFieldHits.Count}";

    // ===================================================================
    // Core redaction
    // ===================================================================

    /// <summary>
    /// CONTRACT: T1 -- run.start with wallClockUtc set to a real timestamp is
    /// redacted to wallClockUtc:null. Non-run.start lines pass through unchanged.
    /// Report reflects 1 line stripped.
    /// </summary>
    [Fact]
    public void T01_StripWallClockUtc_FromRunStart()
    {
        var lines = new[] { RunStartWithClock(), RunEndLine() };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        // First line should have wallClockUtc:null
        Assert.Contains("\"wallClockUtc\":null", outLines[0]);
        Assert.DoesNotContain("2026-05-12T14:22:01.234Z", outLines[0]);
        // Second line byte-for-byte identical
        Assert.Equal(lines[1], outLines[1]);
        // Report
        Assert.Equal(2, report.TotalLines);
        Assert.Equal(1, report.WallClockStripped);
        Assert.Equal(0, report.AlreadyRedacted);
        Assert.Empty(report.ExcludedFieldHits);
    }

    /// <summary>
    /// CONTRACT: T2 -- Already-redacted trace (wallClockUtc already null) passes
    /// through byte-for-byte unchanged. Report shows AlreadyRedacted=1.
    /// </summary>
    [Fact]
    public void T02_AlreadyRedacted_PassesThroughUnchanged()
    {
        var lines = new[] { RunStartRedacted(), RunEndLine() };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        // Both lines byte-for-byte identical
        Assert.Equal(lines[0], outLines[0]);
        Assert.Equal(lines[1], outLines[1]);
        // Report
        Assert.Equal(0, report.WallClockStripped);
        Assert.Equal(1, report.AlreadyRedacted);
    }

    /// <summary>
    /// CONTRACT: T3 -- Redacting twice produces byte-for-byte identical output
    /// (idempotency). Redact(Redact(input)) == Redact(input).
    /// </summary>
    [Fact]
    public void T03_Idempotency_RedactTwice_SameOutput()
    {
        var lines = new[] { RunStartWithClock(), RunEndLine() };

        var (output1, _) = Redact(lines);
        var (output2, report2) = Redact(OutputLines(output1));

        Assert.Equal(output1, output2);
        Assert.Equal(0, report2.WallClockStripped);
        Assert.Equal(1, report2.AlreadyRedacted);
    }

    /// <summary>
    /// CONTRACT: T4 -- Non-run.start lines pass through byte-for-byte unchanged.
    /// Only the run.start line is modified (wallClockUtc stripped).
    /// </summary>
    [Fact]
    public void T04_NonRunStartLines_ByteForByteIdentical()
    {
        var obsLine = ObsLine();
        var decLine = DecisionLine();
        var lines = new[] { RunStartWithClock(), obsLine, decLine };

        var (output, _) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(3, outLines.Length);
        // Lines 2 and 3 (0-indexed 1,2) are byte-for-byte identical
        Assert.Equal(obsLine, outLines[1]);
        Assert.Equal(decLine, outLines[2]);
    }

    // ===================================================================
    // Excluded field scanning
    // ===================================================================

    /// <summary>
    /// CONTRACT: T5 -- A line containing an excluded field key ("characterName")
    /// is reported in ExcludedFieldHits with the correct PropertyName and LineNumber.
    /// The line itself is still emitted (warning, not filter).
    /// </summary>
    [Fact]
    public void T05_ExcludedField_CharacterName_Reported()
    {
        var lines = new[]
        {
            RunStartRedacted(),
            "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"characterName\":\"Warrior of Light\"}}"
        };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        // Both lines emitted
        Assert.Equal(2, outLines.Length);
        // Excluded field detected
        Assert.Single(report.ExcludedFieldHits);
        var hit = report.ExcludedFieldHits[0];
        Assert.Equal("characterName", hit.PropertyName);
        Assert.Equal(2, hit.LineNumber);
    }

    /// <summary>
    /// CONTRACT: T6 -- Multiple excluded fields on the same line are each reported
    /// individually with the same LineNumber.
    /// </summary>
    [Fact]
    public void T06_MultipleExcludedFields_SameLine()
    {
        var lines = new[]
        {
            "{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"run.start\",\"runId\":\"aabbccdd\",\"data\":{\"questId\":66130,\"wallClockUtc\":null,\"characterName\":\"Test\",\"worldName\":\"Gilgamesh\"}}"
        };

        var (_, report) = Redact(lines);

        Assert.Equal(2, report.ExcludedFieldHits.Count);
        Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == "characterName" && h.LineNumber == 1);
        Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == "worldName" && h.LineNumber == 1);
    }

    /// <summary>
    /// CONTRACT: T7 -- Nested excluded field (accountId inside data.observed) is
    /// detected by recursive property-name scanning.
    /// </summary>
    [Fact]
    public void T07_NestedExcludedField_Detected()
    {
        var lines = new[]
        {
            "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"observed\":{\"accountId\":\"12345\"}}}"
        };

        var (_, report) = Redact(lines);

        Assert.Single(report.ExcludedFieldHits);
        Assert.Equal("accountId", report.ExcludedFieldHits[0].PropertyName);
        Assert.Equal(1, report.ExcludedFieldHits[0].LineNumber);
    }

    /// <summary>
    /// CONTRACT: T8 -- Excluded field name appearing as a string VALUE (not a
    /// property name) is NOT flagged. Property-name scanning is precise.
    /// </summary>
    [Fact]
    public void T08_ExcludedFieldAsValue_NotFlagged()
    {
        var lines = new[]
        {
            "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"method\":\"characterName\"}}"
        };

        var (_, report) = Redact(lines);

        Assert.Empty(report.ExcludedFieldHits);
    }

    /// <summary>
    /// CONTRACT: T9 -- No excluded fields in trace produces empty ExcludedFieldHits.
    /// </summary>
    [Fact]
    public void T09_NoExcludedFields_EmptyHits()
    {
        var lines = new[] { RunStartRedacted(), RunEndLine() };

        var (_, report) = Redact(lines);

        Assert.Empty(report.ExcludedFieldHits);
    }

    // ===================================================================
    // Edge cases
    // ===================================================================

    /// <summary>
    /// CONTRACT: T10 -- Malformed JSON line passes through unchanged, no crash.
    /// It counts toward TotalLines.
    /// </summary>
    [Fact]
    public void T10_MalformedJson_PassesThroughUnchanged()
    {
        var malformed = "not json {{{";
        var lines = new[] { RunStartWithClock(), malformed, RunEndLine() };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(3, outLines.Length);
        Assert.Equal(malformed, outLines[1]);
        Assert.Equal(3, report.TotalLines);
    }

    /// <summary>
    /// CONTRACT: T11 -- Blank lines pass through, are NOT counted in TotalLines.
    /// </summary>
    [Fact]
    public void T11_BlankLines_PassThroughNotCounted()
    {
        var lines = new[] { RunStartRedacted(), "", RunEndLine() };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(3, outLines.Length);
        Assert.Equal("", outLines[1]);
        Assert.Equal(2, report.TotalLines);
    }

    /// <summary>
    /// CONTRACT: T12 -- run.start without wallClockUtc field passes through unchanged.
    /// No WallClockStripped, no AlreadyRedacted.
    /// </summary>
    [Fact]
    public void T12_RunStartWithoutWallClockUtc_PassesThrough()
    {
        var line = "{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"run.start\",\"runId\":\"aabbccdd\",\"data\":{\"questId\":66130}}";
        var lines = new[] { line };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(line, outLines[0]);
        Assert.Equal(0, report.WallClockStripped);
        Assert.Equal(0, report.AlreadyRedacted);
    }

    /// <summary>
    /// CONTRACT: T13 -- All eleven excluded property names are detected when
    /// present across separate lines.
    /// </summary>
    [Fact]
    public void T13_AllElevenExcludedNames_Detected()
    {
        var excludedNames = new[]
        {
            "characterName", "worldName", "serverId", "contentId", "accountId",
            "friendList", "fcName", "partyMembers", "retainerName",
            "chatContent", "chatMessage"
        };

        var lines = excludedNames.Select((name, i) =>
            $"{{\"v\":1,\"seq\":{i},\"ts\":{i * 10},\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{{\"{name}\":\"value\"}}}}"
        ).ToArray();

        var (_, report) = Redact(lines);

        Assert.Equal(11, report.ExcludedFieldHits.Count);
        foreach (var name in excludedNames)
        {
            Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == name);
        }
    }

    /// <summary>
    /// CONTRACT: T14 -- Empty trace (no lines) produces empty output and zeroed report.
    /// </summary>
    [Fact]
    public void T14_EmptyInput_EmptyOutputZeroedReport()
    {
        var lines = Array.Empty<string>();

        var (output, report) = Redact(lines);

        Assert.Equal("", output);
        Assert.Equal(0, report.TotalLines);
        Assert.Equal(0, report.WallClockStripped);
        Assert.Equal(0, report.AlreadyRedacted);
        Assert.Empty(report.ExcludedFieldHits);
    }

    /// <summary>
    /// CONTRACT: T15 -- Multiple run.start events (unusual but possible in multi-part
    /// traces) all get wallClockUtc stripped.
    /// </summary>
    [Fact]
    public void T15_MultipleRunStart_AllStripped()
    {
        var lines = new[]
        {
            RunStartWithClock(runId: "run1"),
            RunEndLine(runId: "run1"),
            RunStartWithClock(runId: "run2"),
            RunEndLine(runId: "run2")
        };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Contains("\"wallClockUtc\":null", outLines[0]);
        Assert.Contains("\"wallClockUtc\":null", outLines[2]);
        Assert.Equal(2, report.WallClockStripped);
    }

    /// <summary>
    /// CONTRACT: T16 -- run.start without data field passes through unchanged. No crash.
    /// </summary>
    [Fact]
    public void T16_RunStartWithoutDataField_PassesThrough()
    {
        var line = "{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"run.start\",\"runId\":\"aabbccdd\"}";
        var lines = new[] { line };

        var (output, report) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(line, outLines[0]);
        Assert.Equal(0, report.WallClockStripped);
        Assert.Equal(0, report.AlreadyRedacted);
    }

    // ===================================================================
    // Byte stability
    // ===================================================================

    /// <summary>
    /// CONTRACT: T21 -- Non-run.start lines are byte-for-byte identical in output.
    /// Exact string match per line ensures no re-serialization side effects.
    /// </summary>
    [Fact]
    public void T21_ByteStability_NonRunStartLines()
    {
        // Use lines with specific whitespace/ordering that re-serialization might alter
        var obs = "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"method\":\"GetZone\",\"argument\":null,\"value\":{\"value\":181}}}";
        var dec = "{\"v\":1,\"seq\":2,\"ts\":200,\"type\":\"decision\",\"runId\":\"aabbccdd\",\"data\":{\"stepId\":\"s1\",\"actionType\":\"navigate\"}}";
        var end = "{\"v\":1,\"seq\":3,\"ts\":300,\"type\":\"run.end\",\"runId\":\"aabbccdd\",\"data\":{\"outcome\":\"done\"}}";
        var lines = new[] { RunStartRedacted(), obs, dec, end };

        var (output, _) = Redact(lines);
        var outLines = OutputLines(output);

        Assert.Equal(obs, outLines[1]);
        Assert.Equal(dec, outLines[2]);
        Assert.Equal(end, outLines[3]);
    }

    // ===================================================================
    // Excluded field inside array
    // ===================================================================

    /// <summary>
    /// CONTRACT: T22 -- Excluded field inside an array element is detected by
    /// recursive scanning through arrays and objects.
    /// </summary>
    [Fact]
    public void T22_ExcludedFieldInsideArray_Detected()
    {
        var lines = new[]
        {
            "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"items\":[{\"retainerName\":\"MyRetainer\"}]}}"
        };

        var (_, report) = Redact(lines);

        Assert.Single(report.ExcludedFieldHits);
        Assert.Equal("retainerName", report.ExcludedFieldHits[0].PropertyName);
    }

    // ===================================================================
    // LF line endings
    // ===================================================================

    /// <summary>
    /// CONTRACT: T23 -- Output uses LF line endings, not CRLF. The redactor must
    /// emit \n regardless of platform (TRACE_FORMAT.md SS2).
    /// </summary>
    [Fact]
    public void T23_OutputUsesLF_NotCRLF()
    {
        var lines = new[] { RunStartRedacted(), RunEndLine() };

        var redactor = new TraceRedactor();
        var writer = new StringWriter();
        // Deliberately do NOT set writer.NewLine -- redactor must handle it
        redactor.Redact(lines, writer);
        var output = writer.ToString();

        Assert.Contains("\n", output);
        Assert.DoesNotContain("\r\n", output);
    }

    // ===================================================================
    // Report counting
    // ===================================================================

    /// <summary>
    /// CONTRACT: T24 -- TotalLines counts only non-blank lines. Blank lines are excluded.
    /// </summary>
    [Fact]
    public void T24_TotalLines_CountsNonBlankOnly()
    {
        var lines = new[] { RunStartRedacted(), "", "  ", RunEndLine() };

        var (_, report) = Redact(lines);

        Assert.Equal(2, report.TotalLines);
    }

    /// <summary>
    /// CONTRACT: T25 -- WallClockStripped == 1 when exactly one run.start has a
    /// non-null wallClockUtc that gets stripped.
    /// </summary>
    [Fact]
    public void T25_WallClockStripped_ExactlyOne()
    {
        var lines = new[] { RunStartWithClock(), RunEndLine() };

        var (_, report) = Redact(lines);

        Assert.Equal(1, report.WallClockStripped);
    }

    /// <summary>
    /// CONTRACT: T26 -- WallClockStripped == 0 when trace has no run.start events at all.
    /// </summary>
    [Fact]
    public void T26_NoRunStart_ZeroWallClockStripped()
    {
        var lines = new[] { ObsLine(), DecisionLine() };

        var (_, report) = Redact(lines);

        Assert.Equal(0, report.WallClockStripped);
        Assert.Equal(0, report.AlreadyRedacted);
    }

    /// <summary>
    /// CONTRACT: T27 -- Multiple excluded fields across different lines are all
    /// reported with correct line numbers.
    /// </summary>
    [Fact]
    public void T27_MultipleExcludedFields_AcrossLines()
    {
        var lines = new[]
        {
            "{\"v\":1,\"seq\":0,\"ts\":0,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"characterName\":\"Test\"}}",
            "{\"v\":1,\"seq\":1,\"ts\":50,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"worldName\":\"Gilgamesh\"}}",
            "{\"v\":1,\"seq\":2,\"ts\":100,\"type\":\"observation\",\"runId\":\"aabbccdd\",\"data\":{\"contentId\":\"abc123\"}}"
        };

        var (_, report) = Redact(lines);

        Assert.Equal(3, report.ExcludedFieldHits.Count);
        Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == "characterName" && h.LineNumber == 1);
        Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == "worldName" && h.LineNumber == 2);
        Assert.Contains(report.ExcludedFieldHits, h => h.PropertyName == "contentId" && h.LineNumber == 3);
    }
}
