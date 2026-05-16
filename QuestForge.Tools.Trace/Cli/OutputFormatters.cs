using QuestForge.Tools.Trace.Fixture;

namespace QuestForge.Tools.Trace.Cli;

public static class OutputFormatters
{
    /// <summary>
    /// Format a FixtureValidationResult as one block per issue plus a summary line.
    /// Mirrors the qf-validate output style.
    /// </summary>
    public static string FormatIssues(FixtureValidationResult result) => throw new NotImplementedException();

    /// <summary>
    /// Format a list of TODO strings as a labelled block. Returns "" when todos is empty.
    /// </summary>
    public static string FormatTodos(IReadOnlyList<string> todos) => throw new NotImplementedException();

    /// <summary>
    /// Format a fixture list as a text table.
    /// </summary>
    public static string FormatFixtureList(IReadOnlyList<FixtureListEntry> entries, IReadOnlyList<string> gaps) => throw new NotImplementedException();

    /// <summary>
    /// Format a fixture list as a JSON array.
    /// </summary>
    public static string FormatFixtureListJson(IReadOnlyList<FixtureListEntry> entries, IReadOnlyList<string> gaps) => throw new NotImplementedException();
}
