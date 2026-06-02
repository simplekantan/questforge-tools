using System.Text;
using System.Text.Json;
using QuestForge.Tools.Trace.Fixture;
using QuestForge.Tools.Trace.Validation;

namespace QuestForge.Tools.Trace.Cli;

public static class OutputFormatters
{
    /// <summary>
    /// Format a FixtureValidationResult as one block per issue plus a summary line.
    /// Mirrors the qf-validate output style.
    /// </summary>
    public static string FormatIssues(FixtureValidationResult result)
    {
        var sb = new StringBuilder();

        foreach (var error in result.Errors)
            sb.Append($"ERROR    {error.Code}  {error.Message}\n");

        foreach (var warning in result.Warnings)
            sb.Append($"WARNING  {warning.Code}  {warning.Message}\n");

        // Blank line before summary only when there were issues
        if (result.Errors.Count > 0 || result.Warnings.Count > 0)
            sb.Append('\n');

        var passed = result.Errors.Count == 0 ? "Validation passed." : "Validation failed.";
        sb.Append($"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s). {passed}");

        return sb.ToString();
    }

    /// <summary>
    /// Format a TraceValidationResult as one line per issue plus a summary line.
    /// </summary>
    public static string FormatTraceIssues(TraceValidationResult result)
    {
        var sb = new StringBuilder();

        foreach (var error in result.Errors)
        {
            var loc = error.LineNumber.HasValue ? $"line {error.LineNumber}: " : "";
            sb.Append($"ERROR    [{error.Code}]  {loc}{error.Message}\n");
        }
        foreach (var warning in result.Warnings)
        {
            var loc = warning.LineNumber.HasValue ? $"line {warning.LineNumber}: " : "";
            sb.Append($"WARNING  [{warning.Code}]  {loc}{warning.Message}\n");
        }

        if (result.Errors.Count > 0 || result.Warnings.Count > 0)
            sb.Append('\n');

        var passed = result.IsValid ? "Validation passed." : "Validation failed.";
        sb.Append($"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s). {passed}");

        return sb.ToString();
    }

    /// <summary>
    /// Format a list of TODO strings as a labelled block. Returns "" when todos is empty.
    /// </summary>
    public static string FormatTodos(IReadOnlyList<string> todos)
    {
        if (todos.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("TODOs (human author must fill these in):");
        foreach (var todo in todos)
            sb.Append($"\n  - {todo}");

        return sb.ToString();
    }

    /// <summary>
    /// Format a fixture list as a text table.
    /// </summary>
    public static string FormatFixtureList(IReadOnlyList<FixtureListEntry> entries, IReadOnlyList<string> gaps)
    {
        // Compute column widths dynamically
        const string header1 = "fixture";
        const string header2 = "quest";
        const string header3 = "capabilities";

        int col1Width = header1.Length;
        int col2Width = header2.Length;

        foreach (var e in entries)
        {
            col1Width = Math.Max(col1Width, e.FixtureFile.Length);
            var questLabel = e.QuestFileExists ? $"[OK] {e.QuestFile}" : $"[MISSING] {e.QuestFile}";
            col2Width = Math.Max(col2Width, questLabel.Length);
        }

        const string sep = "  ";

        var sb = new StringBuilder();
        // Header row
        sb.Append(header1.PadRight(col1Width));
        sb.Append(sep);
        sb.Append(header2.PadRight(col2Width));
        sb.Append(sep);
        sb.Append(header3);
        sb.Append('\n');

        // Data rows
        foreach (var e in entries)
        {
            var questLabel = e.QuestFileExists ? $"[OK] {e.QuestFile}" : $"[MISSING] {e.QuestFile}";
            var caps = string.Join(", ", e.Capabilities);

            sb.Append(e.FixtureFile.PadRight(col1Width));
            sb.Append(sep);
            sb.Append(questLabel.PadRight(col2Width));
            sb.Append(sep);
            sb.Append(caps);
            sb.Append('\n');
        }

        // Gaps section
        if (gaps.Count > 0)
        {
            sb.Append('\n');
            sb.Append("Gaps (uncovered capabilities):");
            foreach (var gap in gaps)
            {
                sb.Append('\n');
                sb.Append($"  {gap}");
            }
        }

        // Remove trailing newline (caller uses WriteLine or Write)
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
            sb.Length--;

        return sb.ToString();
    }

    /// <summary>
    /// Format a fixture list as a JSON array.
    /// </summary>
    public static string FormatFixtureListJson(IReadOnlyList<FixtureListEntry> entries, IReadOnlyList<string> gaps)
    {
        var model = new FixtureListJsonModel(
            Fixtures: entries.Select(e => new FixtureListJsonEntry(
                FixtureFile: e.FixtureFile,
                Capabilities: e.Capabilities.ToList(),
                QuestFile: e.QuestFile,
                QuestFileExists: e.QuestFileExists)).ToList(),
            Gaps: gaps.ToList());

        return JsonSerializer.Serialize(model, FixtureModelSerializer.Options);
    }

    // Private models for JSON serialisation
    private sealed record FixtureListJsonModel(
        List<FixtureListJsonEntry> Fixtures,
        List<string> Gaps);

    private sealed record FixtureListJsonEntry(
        string FixtureFile,
        List<string> Capabilities,
        string? QuestFile,
        bool QuestFileExists);
}
