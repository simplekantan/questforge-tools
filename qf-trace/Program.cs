using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Fixture;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
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

        // validate subcommand does not need quest-data root; dispatch early
        if (cliArgs.Subcommand == CliSubcommand.ValidateTrace)
            return RunValidateTrace(cliArgs);

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
            _                             => 1,
        };
    }

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

        var events = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
        var result = new TraceToQuestExtractor().Extract(events);

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
        Console.WriteLine("  extract-quest <trace.jsonl> [--quest-data <dir>] [--out <path>]");
        Console.WriteLine("    Convert a .jsonl trace into a QuestDefinition draft.");
        Console.WriteLine();
        Console.WriteLine("  validate <trace.jsonl> [--fail-on-warning]");
        Console.WriteLine("    Validate structural integrity of a JSONL trace file.");
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
