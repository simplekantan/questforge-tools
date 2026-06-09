using QuestForge.Tools.Trace.Cli;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Cli;

public class CliArgsTests
{
    // ---------------------------------------------------------------------------
    // T-1: Happy path — extract-fixture with every flag
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_ExtractFixture_AllArgs()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["extract-fixture", "run.jsonl", "--quest-data", "./qd", "--out", "out.json"],
         *           When Parse is called,
         *           Then Subcommand == ExtractFixture, TracePath == "run.jsonl",
         *                QuestDataRoot == "./qd", OutputPath == "out.json",
         *                Stdout == false, ParseError == null.
         *
         * BUILDER GUIDANCE: Walk args left-to-right; first token selects subcommand,
         *                   subsequent tokens are flags or positionals.
         */

        // Arrange
        string[] args = ["extract-fixture", "run.jsonl", "--quest-data", "./qd", "--out", "out.json"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.ExtractFixture, result.Subcommand);
        Assert.Equal("run.jsonl", result.TracePath);
        Assert.Equal("./qd", result.QuestDataRoot);
        Assert.Equal("out.json", result.OutputPath);
        Assert.False(result.Stdout, "Stdout should default to false when --stdout is absent");
        Assert.Null(result.ParseError);
    }

    // ---------------------------------------------------------------------------
    // T-2: Happy path — extract-fixture --stdout
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_ExtractFixture_Stdout()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["extract-fixture", "run.jsonl", "--stdout"],
         *           When Parse is called,
         *           Then Stdout == true, OutputPath == null, TracePath == "run.jsonl".
         *
         * BUILDER GUIDANCE: --stdout is a boolean flag; set Stdout = true, no value consumed.
         */

        // Arrange
        string[] args = ["extract-fixture", "run.jsonl", "--stdout"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.True(result.Stdout, "Stdout should be true when --stdout flag is present");
        Assert.Null(result.OutputPath);
        Assert.Equal("run.jsonl", result.TracePath);
    }

    // ---------------------------------------------------------------------------
    // T-3: Happy path — validate-fixture --fail-on-warning
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_ValidateFixture_FailOnWarning()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["validate-fixture", "f.json", "--fail-on-warning"],
         *           When Parse is called,
         *           Then Subcommand == ValidateFixture, FixturePath == "f.json",
         *                FailOnWarning == true.
         *
         * BUILDER GUIDANCE: First positional after validate-fixture goes to FixturePath,
         *                   not TracePath.
         */

        // Arrange
        string[] args = ["validate-fixture", "f.json", "--fail-on-warning"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.ValidateFixture, result.Subcommand);
        Assert.Equal("f.json", result.FixturePath);
        Assert.True(result.FailOnWarning, "FailOnWarning should be true when --fail-on-warning is present");
    }

    // ---------------------------------------------------------------------------
    // T-4: Happy path — list-fixtures --format json
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_ListFixtures_JsonFormat()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["list-fixtures", "--format", "json"],
         *           When Parse is called,
         *           Then Subcommand == ListFixtures, Format == "json".
         *
         * BUILDER GUIDANCE: --format consumes the next token as its value.
         */

        // Arrange
        string[] args = ["list-fixtures", "--format", "json"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.ListFixtures, result.Subcommand);
        Assert.Equal("json", result.Format);
    }

    // ---------------------------------------------------------------------------
    // T-5: Happy path — extract-quest with all args
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_ExtractQuest_AllArgs()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["extract-quest", "run.jsonl", "--quest-data", "./qd", "--out", "draft.json"],
         *           When Parse is called,
         *           Then Subcommand == ExtractQuest, TracePath == "run.jsonl",
         *                QuestDataRoot == "./qd", OutputPath == "draft.json".
         *
         * BUILDER GUIDANCE: extract-quest uses TracePath (same as extract-fixture) for the
         *                   trace positional argument.
         */

        // Arrange
        string[] args = ["extract-quest", "run.jsonl", "--quest-data", "./qd", "--out", "draft.json"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.ExtractQuest, result.Subcommand);
        Assert.Equal("run.jsonl", result.TracePath);
        Assert.Equal("./qd", result.QuestDataRoot);
        Assert.Equal("draft.json", result.OutputPath);
    }

    // ---------------------------------------------------------------------------
    // T-6: Edge case — empty args produces None, no parse error
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_MissingSubcommand()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args [],
         *           When Parse is called,
         *           Then Subcommand == None, TracePath == null, ParseError == null.
         *
         * BUILDER GUIDANCE: Zero args is the help trigger — not a parse error. Return
         *                   a CliArgs with all defaults and Subcommand = None.
         */

        // Arrange
        string[] args = [];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.None, result.Subcommand);
        Assert.Null(result.TracePath);
        Assert.Null(result.ParseError);
    }

    // ---------------------------------------------------------------------------
    // T-7: Edge case — unknown subcommand token
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_UnknownSubcommand()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["frob"],
         *           When Parse is called,
         *           Then Subcommand == Unknown, UnknownToken == "frob".
         *
         * BUILDER GUIDANCE: When the first token is not in the recognised set, set
         *                   Subcommand = Unknown and capture the token in UnknownToken.
         */

        // Arrange
        string[] args = ["frob"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.Unknown, result.Subcommand);
        Assert.Equal("frob", result.UnknownToken);
    }

    // ---------------------------------------------------------------------------
    // T-11 (edge): flag with missing value
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_FlagWithMissingValue_SetsParseError()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["extract-fixture", "run.jsonl", "--out"] (trailing --out with no value),
         *           When Parse is called,
         *           Then ParseError == "flag --out requires a value".
         *
         * BUILDER GUIDANCE: When a value-consuming flag is the last token with nothing
         *                   following it, set ParseError to the canonical message.
         */

        // Arrange
        string[] args = ["extract-fixture", "run.jsonl", "--out"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal("flag --out requires a value", result.ParseError);
    }

    // ---------------------------------------------------------------------------
    // T-13: --badge-dir flag
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_QuestCoverage_BadgeDir()
    {
        string[] args = ["quest-coverage", "--quest-data", "./qd", "--badge-dir", "badges"];

        CliArgs result = CliArgsParser.Parse(args);

        Assert.Equal(CliSubcommand.QuestCoverage, result.Subcommand);
        Assert.Equal("./qd", result.QuestDataRoot);
        Assert.Equal("badges", result.BadgeDirPath);
        Assert.Null(result.ParseError);
    }

    // ---------------------------------------------------------------------------
    // T-14: --badge and --badge-dir together
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_QuestCoverage_BadgeAndBadgeDir()
    {
        string[] args = ["quest-coverage", "--quest-data", "./qd", "--badge", "overall.json", "--badge-dir", "badges"];

        CliArgs result = CliArgsParser.Parse(args);

        Assert.Equal("overall.json", result.BadgePath);
        Assert.Equal("badges", result.BadgeDirPath);
        Assert.Null(result.ParseError);
    }

    // ---------------------------------------------------------------------------
    // T-12 (edge): unknown flag
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseArgs_UnknownFlag_SetsParseError()
    {
        /*
         * RED: Will fail until Builder implements CliArgsParser.Parse.
         *
         * CONTRACT: Given args ["extract-fixture", "run.jsonl", "--frobnicate"],
         *           When Parse is called,
         *           Then ParseError == "unknown flag: --frobnicate".
         *
         * BUILDER GUIDANCE: Any unrecognised --flag token sets ParseError with the
         *                   canonical "unknown flag: <token>" message.
         */

        // Arrange
        string[] args = ["extract-fixture", "run.jsonl", "--frobnicate"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal("unknown flag: --frobnicate", result.ParseError);
    }
}
