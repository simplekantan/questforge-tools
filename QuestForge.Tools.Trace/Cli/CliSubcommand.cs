namespace QuestForge.Tools.Trace.Cli;

public enum CliSubcommand
{
    None,            // no args at all → help
    Help,            // --help, -h, or "help"
    Unknown,         // first token is not a recognised subcommand
    ExtractFixture,
    ValidateFixture,
    ListFixtures,
    ExtractQuest,
    ValidateTrace,
    Redact,
}
