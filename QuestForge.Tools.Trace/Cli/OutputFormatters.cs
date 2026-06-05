using System.Text;
using System.Text.Json;
using QuestForge.Tools.Trace.Analysis;
using QuestForge.Tools.Trace.Coverage;
using QuestForge.Tools.Trace.Fixture;
using QuestForge.Tools.Trace.Redaction;
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

    /// <summary>
    /// Format a RedactionReport for stderr output.
    /// </summary>
    public static string FormatRedactionReport(RedactionReport report)
    {
        var sb = new StringBuilder();
        sb.Append($"Redaction complete: {report.TotalLines} lines processed");
        if (report.WallClockStripped > 0)
            sb.Append($", {report.WallClockStripped} wallClockUtc stripped");
        if (report.AlreadyRedacted > 0)
            sb.Append($", {report.AlreadyRedacted} already redacted");
        sb.Append(".\n");

        if (report.ExcludedFieldHits.Count > 0)
        {
            sb.Append('\n');
            sb.Append("WARNING: excluded fields found (recording proxy should have prevented these):\n");
            foreach (var hit in report.ExcludedFieldHits)
                sb.Append($"  line {hit.LineNumber}: \"{hit.PropertyName}\"\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format a QuestStateChangeReport as a human-readable timeline.
    /// </summary>
    public static string FormatStateChanges(QuestStateChangeReport report)
    {
        var sb = new StringBuilder();

        if (report.QuestId is null)
        {
            sb.Append("No run.start found; cannot determine quest ID.");
            return sb.ToString();
        }

        sb.Append($"Quest {report.QuestId} state changes:");

        if (report.Changes.Count == 0)
        {
            sb.Append("\n  (none)");
            return sb.ToString();
        }

        foreach (var c in report.Changes)
        {
            sb.Append($"\n  seq {c.Seq,-4}");
            sb.Append($" {c.Kind,-9}");
            sb.Append($" {c.PreviousValue}->{c.NewValue}");
            if (c.Detail is not null)
                sb.Append($"  ({c.Detail})");
            if (c.AfterStepId is not null)
                sb.Append($"  after: {c.AfterStepId} / {c.AfterActionType}");
        }

        return sb.ToString();
    }

    public static string FormatCoverageText(CoverageReport report)
    {
        var sb = new StringBuilder();

        AppendTextSection(sb, "Steps", report.Steps);
        AppendTextSection(sb, "Predicates", report.Predicates);
        AppendTextSection(sb, "Action Types", report.ActionTypes);

        int totalCovered = report.Steps.Covered + report.Predicates.Covered + report.ActionTypes.Covered;
        int totalTotal = report.Steps.Total + report.Predicates.Total + report.ActionTypes.Total;
        double overall = report.OverallPercentage;
        sb.Append($"Overall: {totalCovered}/{totalTotal} ({overall:F1}%)\n");

        return sb.ToString();
    }

    private static void AppendTextSection(StringBuilder sb, string label, CoverageSection section)
    {
        sb.Append($"{label}: {section.Covered}/{section.Total} ({section.Percentage:F1}%)\n");
        if (section.UncoveredItems.Count > 0)
        {
            sb.Append("  Uncovered:\n");
            foreach (var item in section.UncoveredItems)
                sb.Append($"    - {item}\n");
        }
    }

    public static string FormatCoverageJson(CoverageReport report)
    {
        int totalCovered = report.Steps.Covered + report.Predicates.Covered + report.ActionTypes.Covered;
        int totalTotal = report.Steps.Total + report.Predicates.Total + report.ActionTypes.Total;
        double overallPct = report.OverallPercentage;

        var model = new CoverageJsonModel(
            Steps: new CoverageSectionJson(
                report.Steps.Covered, report.Steps.Total, report.Steps.Percentage,
                report.Steps.CoveredItems.ToList(), report.Steps.UncoveredItems.ToList()),
            Predicates: new CoverageSectionJson(
                report.Predicates.Covered, report.Predicates.Total, report.Predicates.Percentage,
                report.Predicates.CoveredItems.ToList(), report.Predicates.UncoveredItems.ToList()),
            ActionTypes: new CoverageSectionJson(
                report.ActionTypes.Covered, report.ActionTypes.Total, report.ActionTypes.Percentage,
                report.ActionTypes.CoveredItems.ToList(), report.ActionTypes.UncoveredItems.ToList()),
            Overall: new CoverageOverallJson(totalCovered, totalTotal, overallPct));

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        return JsonSerializer.Serialize(model, options);
    }

    public static string FormatCoverageMarkdown(CoverageReport report)
    {
        var sb = new StringBuilder();

        sb.Append("## Fixture Coverage Report\n\n");
        sb.Append("| Category | Covered | Total | Percentage |\n");
        sb.Append("|---|---|---|---|\n");

        int totalCovered = report.Steps.Covered + report.Predicates.Covered + report.ActionTypes.Covered;
        int totalTotal = report.Steps.Total + report.Predicates.Total + report.ActionTypes.Total;

        sb.Append($"| Steps | {report.Steps.Covered} | {report.Steps.Total} | {report.Steps.Percentage:F1}% |\n");
        sb.Append($"| Predicates | {report.Predicates.Covered} | {report.Predicates.Total} | {report.Predicates.Percentage:F1}% |\n");
        sb.Append($"| Action Types | {report.ActionTypes.Covered} | {report.ActionTypes.Total} | {report.ActionTypes.Percentage:F1}% |\n");
        sb.Append($"| **Overall** | **{totalCovered}** | **{totalTotal}** | **{report.OverallPercentage:F1}%** |\n");

        if (report.Steps.UncoveredItems.Count > 0)
        {
            sb.Append("\n### Uncovered Steps\n");
            foreach (var item in report.Steps.UncoveredItems)
                sb.Append($"- `{item}`\n");
        }

        if (report.Predicates.UncoveredItems.Count > 0)
        {
            sb.Append("\n### Uncovered Predicates\n");
            foreach (var item in report.Predicates.UncoveredItems)
                sb.Append($"- `{item}`\n");
        }

        if (report.ActionTypes.UncoveredItems.Count > 0)
        {
            sb.Append("\n### Uncovered Action Types\n");
            foreach (var item in report.ActionTypes.UncoveredItems)
                sb.Append($"- `{item}`\n");
        }

        return sb.ToString();
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

    private sealed record CoverageJsonModel(
        CoverageSectionJson Steps,
        CoverageSectionJson Predicates,
        CoverageSectionJson ActionTypes,
        CoverageOverallJson Overall);

    private sealed record CoverageSectionJson(
        int Covered,
        int Total,
        double Percentage,
        List<string> CoveredItems,
        List<string> Uncovered);

    private sealed record CoverageOverallJson(
        int Covered,
        int Total,
        double Percentage);
}
