using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Fixture;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Cli;

public class OutputFormattersTests
{
    // ---------------------------------------------------------------------------
    // T-8: Error + warning result → contains ERROR, WARNING, and summary
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatIssues_ErrorsAndWarnings()
    {
        /*
         * RED: Will fail until Builder implements OutputFormatters.FormatIssues.
         *
         * CONTRACT: Given a FixtureValidationResult with one error and one warning,
         *           When FormatIssues is called,
         *           Then the output contains an ERROR line with code and message,
         *                a WARNING line with code and message,
         *                and the summary "1 error(s), 1 warning(s). Validation failed.".
         *
         * BUILDER GUIDANCE: Severity column is left-padded to 7 chars so ERROR (5) and
         *                   WARNING (7) align. Two spaces between each column.
         */

        // Arrange
        var result = new FixtureValidationResult(
            Errors:
            [
                new FixtureValidationIssue(
                    Code: "fixture/quest-file-missing",
                    Message: "quest file not found: x.json"),
            ],
            Warnings:
            [
                new FixtureValidationIssue(
                    Code: "fixture/capability-extra",
                    Message: "capabilities list includes 'step:combat' but quest does not use it"),
            ]);

        // Act
        string output = OutputFormatters.FormatIssues(result);

        // Assert
        Assert.Contains("ERROR", output);
        Assert.Contains("fixture/quest-file-missing", output);
        Assert.Contains("quest file not found: x.json", output);
        Assert.Contains("WARNING", output);
        Assert.Contains("fixture/capability-extra", output);
        Assert.Contains("1 error(s), 1 warning(s). Validation failed.", output);
    }

    // ---------------------------------------------------------------------------
    // T-9: Non-empty todos → header line plus indented items
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatTodos_NonEmpty()
    {
        /*
         * RED: Will fail until Builder implements OutputFormatters.FormatTodos.
         *
         * CONTRACT: Given todos = ["name (Lumina lookup)", "expansion"],
         *           When FormatTodos is called,
         *           Then result starts with "TODOs (human author must fill these in):",
         *                and contains "  - name (Lumina lookup)",
         *                and contains "  - expansion".
         *
         * BUILDER GUIDANCE: Header line first, then each item indented with "  - ".
         *                   Returns the assembled string; caller writes it.
         */

        // Arrange
        IReadOnlyList<string> todos = ["name (Lumina lookup)", "expansion"];

        // Act
        string output = OutputFormatters.FormatTodos(todos);

        // Assert
        Assert.StartsWith("TODOs (human author must fill these in):", output);
        Assert.Contains("  - name (Lumina lookup)", output);
        Assert.Contains("  - expansion", output);
    }

    // ---------------------------------------------------------------------------
    // T-11: Clean result → only summary line, no blank line prefix
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatIssues_CleanResult()
    {
        /*
         * RED: Will fail until Builder implements OutputFormatters.FormatIssues.
         *
         * CONTRACT: Given a FixtureValidationResult with empty Errors and Warnings,
         *           When FormatIssues is called,
         *           Then result equals "0 error(s), 0 warning(s). Validation passed."
         *                (no issue lines, no blank line prefix, no trailing newline).
         *
         * BUILDER GUIDANCE: When there are no issues, skip the issue lines and blank
         *                   separator entirely and return only the summary string.
         */

        // Arrange
        var result = new FixtureValidationResult(
            Errors: [],
            Warnings: []);

        // Act
        string output = OutputFormatters.FormatIssues(result);

        // Assert
        Assert.Equal("0 error(s), 0 warning(s). Validation passed.", output);
    }

    // ---------------------------------------------------------------------------
    // T-12: Empty todos → empty string
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatTodos_Empty()
    {
        /*
         * RED: Will fail until Builder implements OutputFormatters.FormatTodos.
         *
         * CONTRACT: Given todos = [] (empty list),
         *           When FormatTodos is called,
         *           Then returns "" (empty string, no output at all).
         *
         * BUILDER GUIDANCE: Guard at the top of the method — if the list is empty,
         *                   return string.Empty immediately before building anything.
         */

        // Arrange
        IReadOnlyList<string> todos = [];

        // Act
        string output = OutputFormatters.FormatTodos(todos);

        // Assert
        Assert.Equal(string.Empty, output);
    }
}
