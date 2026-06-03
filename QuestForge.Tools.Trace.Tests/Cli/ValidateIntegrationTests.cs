using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Validation;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Cli;

public sealed class ValidateIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string WriteTempTrace(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qf-validate-test-{Guid.NewGuid()}.jsonl");
        File.WriteAllText(path, string.Join("\n", lines));
        _tempFiles.Add(path);
        return path;
    }

    private static string Serialize(TraceEvent evt) =>
        JsonSerializer.Serialize(evt, typeof(TraceEvent), TraceEventJsonContext.Default.Options);

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    [Fact]
    public void T24_CleanTrace_FileApi_IsValid()
    {
        /*
         * CONTRACT: Given a temp file with a valid 3-event trace (run.start seq=0,
         *           observation seq=1, run.end seq=2), all with matching runId and v=1,
         *           When TraceValidator.Validate(filePath) is called,
         *           Then IsValid is true and there are no errors or warnings.
         */
        var rs = new RunStartEvent
        {
            RunId = "int-test", Seq = 0, Ts = 0, V = 1,
            Data = new RunStartEvent.RunStartData { QuestId = 65 }
        };
        var obs = new ObservationEvent
        {
            RunId = "int-test", Seq = 1, Ts = 10, V = 1,
            Data = new ObservationEvent.ObservationData { Method = "GetPlayerZone" }
        };
        var re = new RunEndEvent
        {
            RunId = "int-test", Seq = 2, Ts = 100, V = 1,
            Data = new RunEndEvent.RunEndData { Outcome = "done" }
        };

        var path = WriteTempTrace(Serialize(rs), Serialize(obs), Serialize(re));
        var result = new TraceValidator().Validate(path);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void T25_BadSeq_FileApi_HasErrors()
    {
        /*
         * CONTRACT: Given a temp file with seq violation (seq 0, seq 5),
         *           When TraceValidator.Validate(filePath) is called,
         *           Then IsValid is false and errors contain TV-E004.
         */
        var rs = new RunStartEvent
        {
            RunId = "int-test", Seq = 0, Ts = 0, V = 1,
            Data = new RunStartEvent.RunStartData { QuestId = 65 }
        };
        var obs = new ObservationEvent
        {
            RunId = "int-test", Seq = 5, Ts = 10, V = 1,
            Data = new ObservationEvent.ObservationData { Method = "GetPlayerZone" }
        };

        var path = WriteTempTrace(Serialize(rs), Serialize(obs));
        var result = new TraceValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TV-E004");
    }

    [Fact]
    public void T26_WarningOnly_FailOnWarning_ExitCode()
    {
        /*
         * CONTRACT: Given a valid trace with only a warning (missing run.end),
         *           When validated, IsValid is true but warnings are non-empty.
         *           The CLI would return exit code 2 with --fail-on-warning.
         *           (We test the result shape here; CLI exit code is Program.Main's concern.)
         */
        var rs = new RunStartEvent
        {
            RunId = "int-test", Seq = 0, Ts = 0, V = 1,
            Data = new RunStartEvent.RunStartData { QuestId = 65 }
        };

        var path = WriteTempTrace(Serialize(rs));
        var result = new TraceValidator().Validate(path);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Code == "TV-W002");
    }

    [Fact]
    public void T27_CliParser_Validate_NoPositional_ParsesCorrectly()
    {
        /*
         * CONTRACT: Given args ["validate"] with no positional argument,
         *           When CliArgsParser.Parse is called,
         *           Then Subcommand is ValidateTrace and TracePath is null.
         *           (The CLI would then print an error and return 1.)
         */
        var result = CliArgsParser.Parse(["validate"]);

        Assert.Equal(CliSubcommand.ValidateTrace, result.Subcommand);
        Assert.Null(result.TracePath);
    }

    [Fact]
    public void T27b_CliParser_Validate_WithPath_ParsesCorrectly()
    {
        /*
         * CONTRACT: Given args ["validate", "trace.jsonl"],
         *           When CliArgsParser.Parse is called,
         *           Then Subcommand is ValidateTrace and TracePath is "trace.jsonl".
         */
        var result = CliArgsParser.Parse(["validate", "trace.jsonl"]);

        Assert.Equal(CliSubcommand.ValidateTrace, result.Subcommand);
        Assert.Equal("trace.jsonl", result.TracePath);
    }

    [Fact]
    public void T27c_CliParser_Validate_FailOnWarning_ParsesCorrectly()
    {
        /*
         * CONTRACT: Given args ["validate", "trace.jsonl", "--fail-on-warning"],
         *           When CliArgsParser.Parse is called,
         *           Then FailOnWarning is true.
         */
        var result = CliArgsParser.Parse(["validate", "trace.jsonl", "--fail-on-warning"]);

        Assert.Equal(CliSubcommand.ValidateTrace, result.Subcommand);
        Assert.True(result.FailOnWarning);
    }

    [Fact]
    public void T28_OutputFormatter_CleanTrace_ContainsValid()
    {
        /*
         * CONTRACT: Given a clean TraceValidationResult (no errors, no warnings),
         *           When FormatTraceIssues is called,
         *           Then the output contains "valid" (case-insensitive).
         */
        var result = new TraceValidationResult(
            Array.Empty<TraceValidationIssue>(),
            Array.Empty<TraceValidationIssue>());

        var output = OutputFormatters.FormatTraceIssues(result);

        Assert.Contains("valid", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void T29_OutputFormatter_WithErrors_ContainsErrorCode()
    {
        /*
         * CONTRACT: Given a TraceValidationResult with one TV-E004 error,
         *           When FormatTraceIssues is called,
         *           Then the output contains "TV-E004".
         */
        var result = new TraceValidationResult(
            new[] { new TraceValidationIssue("TV-E004", "seq violation", 2) },
            Array.Empty<TraceValidationIssue>());

        var output = OutputFormatters.FormatTraceIssues(result);

        Assert.Contains("TV-E004", output);
    }
}
