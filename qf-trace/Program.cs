namespace qf_trace;

/// <summary>
/// Stub entry point for qf-trace CLI.
/// Full dispatch (extract-fixture, validate-fixture, list-fixtures, extract-quest) is deferred to Builder phase.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            Console.WriteLine("qf-trace <subcommand> [options]");
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  extract-fixture   Turn a .jsonl trace into a fixture JSON draft.");
            Console.WriteLine("  validate-fixture  Cross-check a fixture against its referenced quest file.");
            Console.WriteLine("  list-fixtures     Enumerate fixtures and show capability coverage / gaps.");
            Console.WriteLine("  extract-quest     Turn a .jsonl trace into a QuestDefinition draft.");
            return 0;
        }

        throw new NotImplementedException($"Subcommand '{args[0]}' is not yet implemented.");
    }
}
