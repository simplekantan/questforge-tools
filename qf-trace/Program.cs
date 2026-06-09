using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Analysis;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Fixture;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
using QuestForge.Tools.Trace.Redaction;
using QuestForge.Tools.Trace.Validation;

namespace qf_trace;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cliArgs = CliArgsParser.Parse(args);

        // Help / no-op cases
        if (cliArgs.Subcommand == CliSubcommand.None || cliArgs.Subcommand == CliSubcommand.Help)
        {
            PrintHelp();
            return 0;
        }

        // Unknown subcommand
        if (cliArgs.Subcommand == CliSubcommand.Unknown)
        {
            Console.Error.WriteLine($"qf-trace: unknown subcommand '{cliArgs.UnknownToken}'. Run 'qf-trace --help' for usage.");
            return 1;
        }

        // Parse error
        if (cliArgs.ParseError != null)
        {
            Console.Error.WriteLine($"qf-trace: {cliArgs.ParseError}");
            return 1;
        }

        // validate, redact, and state-changes subcommands do not need quest-data root; dispatch early
        if (cliArgs.Subcommand == CliSubcommand.ValidateTrace)
            return RunValidateTrace(cliArgs);

        if (cliArgs.Subcommand == CliSubcommand.Redact)
            return RunRedact(cliArgs);

        if (cliArgs.Subcommand == CliSubcommand.StateChanges)
            return RunStateChanges(cliArgs);

        // Resolve quest-data root
        string? resolvedRoot;
        if (cliArgs.QuestDataRoot != null)
        {
            resolvedRoot = Path.GetFullPath(cliArgs.QuestDataRoot);
            if (!Directory.Exists(resolvedRoot))
            {
                Console.Error.WriteLine($"qf-trace: quest-data directory not found: {resolvedRoot}");
                return 1;
            }
        }
        else
        {
            resolvedRoot = QuestDataRootResolver.Resolve();
            if (resolvedRoot != null)
                Console.Error.WriteLine($"qf-trace: using quest-data root: {resolvedRoot}");
            else
                Console.Error.WriteLine("qf-trace: no quest-data root found; using placeholder paths");
        }

        return cliArgs.Subcommand switch
        {
            CliSubcommand.ExtractFixture  => RunExtractFixture(cliArgs, resolvedRoot),
            CliSubcommand.ValidateFixture => RunValidateFixture(cliArgs, resolvedRoot),
            CliSubcommand.ListFixtures    => RunListFixtures(cliArgs, resolvedRoot),
            CliSubcommand.ExtractQuest    => RunExtractQuest(cliArgs, resolvedRoot),
            CliSubcommand.Coverage        => RunCoverage(cliArgs, resolvedRoot),
            CliSubcommand.GenerateQuestList => RunGenerateQuestList(cliArgs),
            CliSubcommand.QuestCoverage   => RunQuestCoverage(cliArgs, resolvedRoot),
            _                             => 1,
        };
    }

    private static int RunCoverage(CliArgs cliArgs, string? resolvedRoot)
    {
        if (resolvedRoot is null)
        {
            Console.Error.WriteLine("qf-trace: coverage requires --quest-data or a resolvable sibling");
            return 1;
        }

        var analyzer = new QuestForge.Tools.Trace.Coverage.CoverageAnalyzer();
        var report = analyzer.Analyze(resolvedRoot);

        var output = cliArgs.Format switch
        {
            "json" => QuestForge.Tools.Trace.Cli.OutputFormatters.FormatCoverageJson(report),
            "markdown" => QuestForge.Tools.Trace.Cli.OutputFormatters.FormatCoverageMarkdown(report),
            _ => QuestForge.Tools.Trace.Cli.OutputFormatters.FormatCoverageText(report),
        };
        Console.Out.Write(output);

        if (cliArgs.BadgePath is { } bjPath)
        {
            int totalCovered = report.Steps.Covered + report.Predicates.Covered + report.ActionTypes.Covered;
            int totalTotal = report.Steps.Total + report.Predicates.Total + report.ActionTypes.Total;
            var badgeJson = SerializeBadge("fixture coverage", totalCovered, totalTotal, report.OverallPercentage);
            File.WriteAllText(bjPath, badgeJson);
            Console.Error.WriteLine($"Written to {bjPath}");
        }

        if (cliArgs.MinCoverage is { } min && report.OverallPercentage < min)
        {
            Console.Error.WriteLine($"qf-trace: overall coverage {report.OverallPercentage:F1}% is below threshold {min}%");
            return 1;
        }

        return 0;
    }

    private static int RunGenerateQuestList(CliArgs cliArgs)
    {
        var sqpackPath = cliArgs.SqpackPath ?? QuestForge.Tools.Trace.Cli.SqpackPathResolver.Resolve();
        if (sqpackPath is null || !Directory.Exists(sqpackPath))
        {
            Console.Error.WriteLine("qf-trace: generate-quest-list requires --sqpack or auto-detected game install");
            return 1;
        }

        Console.Error.WriteLine($"qf-trace: reading quest data from {sqpackPath}");
        var entries = QuestForge.Tools.Trace.Coverage.QuestListGenerator.Generate(sqpackPath);
        Console.Error.WriteLine($"qf-trace: found {entries.Count} quests");

        var json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        if (cliArgs.OutputPath is { } outPath)
        {
            File.WriteAllText(outPath, json);
            Console.Out.WriteLine($"Written to {outPath}");
        }
        else
        {
            Console.Out.Write(json);
        }

        return 0;
    }

    private static int RunQuestCoverage(CliArgs cliArgs, string? resolvedRoot)
    {
        if (resolvedRoot is null)
        {
            Console.Error.WriteLine("qf-trace: quest-coverage requires --quest-data");
            return 1;
        }

        var questListPath = Path.Combine(resolvedRoot, "quest-list.json");
        if (!File.Exists(questListPath))
        {
            Console.Error.WriteLine($"qf-trace: quest-list.json not found at {questListPath}");
            Console.Error.WriteLine("Run: qf-trace generate-quest-list --out <data-repo>/quest-list.json");
            return 1;
        }

        var questListJson = File.ReadAllText(questListPath);
        var allQuests = System.Text.Json.JsonSerializer.Deserialize<List<QuestForge.Tools.Trace.Coverage.QuestListEntry>>(
            questListJson, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
            ?? [];

        var questsDir = Path.Combine(resolvedRoot, "quests");
        var coveredIds = new HashSet<uint>();
        if (Directory.Exists(questsDir))
        {
            foreach (var file in Directory.EnumerateFiles(questsDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var quest = System.Text.Json.JsonSerializer.Deserialize<QuestForge.Schema.QuestDefinition>(
                        json, QuestForge.Schema.QuestForgeJsonContext.QuestFileOptions);
                    if (quest is not null && quest.Id != 0)
                        coveredIds.Add(quest.Id);
                }
                catch { }
            }
        }

        var groups = allQuests
            .Where(q => q.Category is "msq" or "class" or "role")
            .GroupBy(q => (q.Expansion, q.Category))
            .OrderBy(g => ExpansionOrder(g.Key.Expansion))
            .ThenBy(g => g.Key.Category);

        Console.Out.WriteLine("Quest Coverage Report");
        Console.Out.WriteLine("=====================");
        Console.Out.WriteLine();

        var totalAll = 0;
        var coveredAll = 0;

        string? lastExpansion = null;
        foreach (var group in groups)
        {
            if (lastExpansion != group.Key.Expansion)
            {
                if (lastExpansion is not null) Console.Out.WriteLine();
                Console.Out.WriteLine($"  {group.Key.Expansion.ToUpperInvariant()}");
                lastExpansion = group.Key.Expansion;
            }

            var total = group.Count();
            var covered = group.Count(q => coveredIds.Contains(q.Id));
            var pct = total > 0 ? (double)covered / total * 100 : 0;
            Console.Out.WriteLine($"    {group.Key.Category,-10} {covered,4} / {total,-4}  ({pct,5:F1}%)");

            totalAll += total;
            coveredAll += covered;
        }

        Console.Out.WriteLine();
        var overallPct = totalAll > 0 ? (double)coveredAll / totalAll * 100 : 0;
        Console.Out.WriteLine($"  Overall:    {coveredAll,4} / {totalAll,-4}  ({overallPct,5:F1}%)");

        if (cliArgs.MarkdownPath is { } mdPath)
        {
            var md = new System.Text.StringBuilder();
            md.AppendLine("# Quest Coverage Report");
            md.AppendLine();
            md.AppendLine("| Expansion | Category | Covered | Total | % |");
            md.AppendLine("|-----------|----------|---------|-------|---|");
            foreach (var group in groups)
            {
                var total = group.Count();
                var covered = group.Count(q => coveredIds.Contains(q.Id));
                var pct = total > 0 ? (double)covered / total * 100 : 0;
                md.AppendLine($"| {group.Key.Expansion} | {group.Key.Category} | {covered} | {total} | {pct:F1}% |");
            }
            md.AppendLine();
            md.AppendLine($"**Overall: {coveredAll} / {totalAll} ({overallPct:F1}%)**");
            File.WriteAllText(mdPath, md.ToString());
            Console.Error.WriteLine($"Written to {mdPath}");
        }

        if (cliArgs.BadgePath is { } bjPath)
        {
            var badgeJson = SerializeBadge("quest coverage", coveredAll, totalAll, overallPct);
            File.WriteAllText(bjPath, badgeJson);
            Console.Error.WriteLine($"Written to {bjPath}");
        }

        if (cliArgs.BadgeDirPath is { } badgeDir)
        {
            Directory.CreateDirectory(badgeDir);

            File.WriteAllText(
                Path.Combine(badgeDir, "overall.json"),
                SerializeBadge("quest coverage", coveredAll, totalAll, overallPct));

            foreach (var group in groups)
            {
                var total = group.Count();
                var covered = group.Count(q => coveredIds.Contains(q.Id));
                var pct = total > 0 ? (double)covered / total * 100 : 0;
                var label = $"{FormatExpansionLabel(group.Key.Expansion)} {group.Key.Category}";
                var fileName = $"{group.Key.Expansion}-{group.Key.Category}.json";
                File.WriteAllText(
                    Path.Combine(badgeDir, fileName),
                    SerializeBadge(label, covered, total, pct));
            }

            Console.Error.WriteLine($"Badge files written to {badgeDir}/");
        }

        return 0;
    }

    private static string SerializeBadge(string label, int covered, int total, double pct)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            label,
            message = $"{covered}/{total} ({pct:F1}%)",
            color = BadgeColor(pct)
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string BadgeColor(double pct) => pct switch
    {
        0 => "lightgrey",
        < 25 => "red",
        < 50 => "orange",
        < 75 => "yellow",
        < 100 => "green",
        _ => "brightgreen"
    };

    private static string FormatExpansionLabel(string expansion) => expansion switch
    {
        "arr" => "ARR",
        "heavensward" => "HW",
        "stormblood" => "SB",
        "shadowbringers" => "ShB",
        "endwalker" => "EW",
        "dawntrail" => "DT",
        _ => expansion.ToUpperInvariant()
    };

    private static int ExpansionOrder(string expansion) => expansion switch
    {
        "arr" => 0, "heavensward" => 1, "stormblood" => 2,
        "shadowbringers" => 3, "endwalker" => 4, "dawntrail" => 5,
        _ => 99
    };

    private static int RunValidateTrace(CliArgs cliArgs)
    {
        if (cliArgs.TracePath is null)
        {
            Console.Error.WriteLine("qf-trace: validate requires <trace.jsonl>");
            return 1;
        }
        if (!File.Exists(cliArgs.TracePath))
        {
            Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
            return 1;
        }

        var result = new TraceValidator().Validate(cliArgs.TracePath);
        Console.Out.Write(OutputFormatters.FormatTraceIssues(result));

        if (result.Errors.Count > 0) return 1;
        if (result.Warnings.Count > 0 && cliArgs.FailOnWarning) return 2;
        return 0;
    }

    private static int RunRedact(CliArgs cliArgs)
    {
        if (cliArgs.TracePath is null)
        {
            Console.Error.WriteLine("qf-trace: redact requires <input>");
            return 1;
        }
        if (!File.Exists(cliArgs.TracePath))
        {
            Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
            return 1;
        }

        var redactor = new TraceRedactor();
        RedactionReport report;

        if (cliArgs.OutputPath is not null)
        {
            using var writer = new StreamWriter(cliArgs.OutputPath, append: false,
                new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
            report = redactor.RedactFile(cliArgs.TracePath, writer);
        }
        else
        {
            Console.Out.NewLine = "\n";
            report = redactor.RedactFile(cliArgs.TracePath, Console.Out);
        }

        Console.Error.Write(OutputFormatters.FormatRedactionReport(report));
        return report.ExcludedFieldHits.Count > 0 ? 2 : 0;
    }

    private static int RunExtractFixture(CliArgs cliArgs, string? resolvedRoot)
    {
        if (cliArgs.TracePath is null)
        {
            Console.Error.WriteLine("qf-trace: extract-fixture requires <trace.jsonl>");
            return 1;
        }
        if (!File.Exists(cliArgs.TracePath))
        {
            Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
            return 1;
        }

        var events    = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
        var extractor = new TraceToFixtureExtractor(resolvedRoot);
        var result    = extractor.Extract(events);

        if (result is Result<FixtureModel>.Failure f)
        {
            Console.Error.WriteLine($"qf-trace: {f.Reason}: {f.Detail}");
            return 1;
        }

        var fixture = ((Result<FixtureModel>.Success)result).Value;
        var json    = FixtureModelSerializer.Serialize(fixture);

        if (cliArgs.Stdout)
        {
            Console.Out.Write(json);
            return 0;
        }

        var outputPath = cliArgs.OutputPath ?? extractor.SuggestFilename(fixture);
        File.WriteAllText(outputPath, json);
        Console.Out.WriteLine($"Written to {outputPath}");

        if (cliArgs.WithTrace)
        {
            var runEvents = TraceToFixtureExtractor.FilterToFixtureRun(events);
            if (runEvents.Count == 0)
            {
                Console.Error.WriteLine("qf-trace: no run.start found; skipping trace co-emit");
            }
            else
            {
                var basename = outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? outputPath[..^5]
                    : outputPath;
                var tracePath = basename + ".trace.jsonl";
                using var writer = new StreamWriter(tracePath, append: false, encoding: System.Text.Encoding.UTF8);
                foreach (var ev in runEvents)
                    writer.WriteLine(JsonSerializer.Serialize(ev, TraceEventJsonContext.Default.TraceEvent));
                Console.Out.WriteLine($"Written to {tracePath}");
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine("TODO: edit the 'description' field before committing.");
        return 0;
    }

    private static int RunValidateFixture(CliArgs cliArgs, string? resolvedRoot)
    {
        if (cliArgs.FixturePath is null)
        {
            Console.Error.WriteLine("qf-trace: validate-fixture requires <fixture.json>");
            return 1;
        }
        if (!File.Exists(cliArgs.FixturePath))
        {
            Console.Error.WriteLine($"qf-trace: fixture file not found: {cliArgs.FixturePath}");
            return 1;
        }
        if (resolvedRoot is null)
        {
            Console.Error.WriteLine("qf-trace: validate-fixture requires --quest-data or a resolvable sibling");
            return 1;
        }

        var result = new FixtureValidator(resolvedRoot).ValidateFile(cliArgs.FixturePath);
        Console.Out.Write(OutputFormatters.FormatIssues(result));

        if (result.HasErrors) return 1;
        if (result.Warnings.Count > 0 && cliArgs.FailOnWarning) return 2;
        return 0;
    }

    private static int RunListFixtures(CliArgs cliArgs, string? resolvedRoot)
    {
        if (resolvedRoot is null)
        {
            Console.Error.WriteLine("qf-trace: list-fixtures requires --quest-data or a resolvable sibling");
            return 1;
        }

        var cmd     = new ListFixturesCommand(resolvedRoot);
        var entries = cmd.Enumerate();
        var gaps    = cmd.ComputeGaps(entries);

        if (cliArgs.Format == "json")
            Console.Out.Write(OutputFormatters.FormatFixtureListJson(entries, gaps));
        else
            Console.Out.Write(OutputFormatters.FormatFixtureList(entries, gaps));

        return 0;
    }

    private static int RunExtractQuest(CliArgs cliArgs, string? resolvedRoot)
    {
        if (cliArgs.TracePath is null)
        {
            Console.Error.WriteLine("qf-trace: extract-quest requires <trace.jsonl>");
            return 1;
        }
        if (!File.Exists(cliArgs.TracePath))
        {
            Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
            return 1;
        }

        IQuestMetadataResolver resolver = NullMetadataResolver.Instance;
        var sqpackPath = cliArgs.SqpackPath ?? QuestForge.Tools.Trace.Cli.SqpackPathResolver.Resolve();
        if (sqpackPath is not null && Directory.Exists(sqpackPath))
        {
            try
            {
                resolver = new LuminaMetadataResolver(sqpackPath);
                Console.Error.WriteLine($"qf-trace: using Lumina data from {sqpackPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"qf-trace: lumina init failed ({ex.Message}); continuing without name resolution");
            }
        }

        var events = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
        var result = new TraceToQuestExtractor(resolver: resolver).Extract(events);

        if (result is Result<QuestDraftResult>.Failure f)
        {
            Console.Error.WriteLine($"qf-trace: {f.Reason}: {f.Detail}");
            return 1;
        }

        var draft  = ((Result<QuestDraftResult>.Success)result).Value;
        var json   = JsonSerializer.Serialize(draft.Definition, QuestForgeJsonContext.QuestFileOptions);
        var output = cliArgs.OutputPath ?? $"{draft.Definition.Id}-draft.json";

        File.WriteAllText(output, json);
        Console.Out.WriteLine($"Written to {output}");
        Console.Out.WriteLine();
        Console.Out.Write(OutputFormatters.FormatTodos(draft.Todos));
        return 0;
    }

    private static int RunStateChanges(CliArgs cliArgs)
    {
        if (cliArgs.TracePath is null)
        {
            Console.Error.WriteLine("qf-trace: state-changes requires <trace.jsonl>");
            return 1;
        }
        if (!File.Exists(cliArgs.TracePath))
        {
            Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
            return 1;
        }

        var events = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
        var report = new QuestStateChangeAnalyzer().Analyze(events);
        Console.Out.WriteLine(OutputFormatters.FormatStateChanges(report));
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("qf-trace <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  extract-fixture <trace.jsonl> [--quest-data <dir>] [--out <path>] [--stdout]");
        Console.WriteLine("    Convert a .jsonl trace into a fixture JSON draft.");
        Console.WriteLine();
        Console.WriteLine("  validate-fixture <fixture.json> [--quest-data <dir>] [--fail-on-warning]");
        Console.WriteLine("    Cross-check a fixture against its referenced quest file.");
        Console.WriteLine();
        Console.WriteLine("  list-fixtures [--quest-data <dir>] [--format text|json]");
        Console.WriteLine("    Enumerate fixtures and show capability coverage / gaps.");
        Console.WriteLine();
        Console.WriteLine("  extract-quest <trace.jsonl> [--quest-data <dir>] [--out <path>] [--sqpack <path>]");
        Console.WriteLine("    Convert a .jsonl trace into a QuestDefinition draft.");
        Console.WriteLine("    With --sqpack, resolves quest name, expansion, category, and requirements");
        Console.WriteLine("    from FFXIV game data via Lumina. Auto-detects standard install if omitted.");
        Console.WriteLine();
        Console.WriteLine("  validate <trace.jsonl> [--fail-on-warning]");
        Console.WriteLine("    Validate structural integrity of a JSONL trace file.");
        Console.WriteLine();
        Console.WriteLine("  redact <input> [<output>]");
        Console.WriteLine("    Strip wallClockUtc and verify no excluded PII fields.");
        Console.WriteLine("    If <output> is omitted, write to stdout. Report goes to stderr.");
        Console.WriteLine();
        Console.WriteLine("  state-changes <trace.jsonl>");
        Console.WriteLine("    Show a timeline of quest state transitions correlated with decisions.");
        Console.WriteLine();
        Console.WriteLine("Quest-data root:");
        Console.WriteLine("  --quest-data <dir>          Path to the questforge-data checkout root.");
        Console.WriteLine("                              If omitted, qf-trace probes ./quests/, then");
        Console.WriteLine("                              ../questforge-data/quests/, in that order.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0    success / clean validation");
        Console.WriteLine("  1    usage error, fatal error, or validation errors");
        Console.WriteLine("  2    validation warnings only with --fail-on-warning");
    }
}
