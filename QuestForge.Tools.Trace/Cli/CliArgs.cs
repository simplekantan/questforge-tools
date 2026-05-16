namespace QuestForge.Tools.Trace.Cli;

public sealed record CliArgs(
    CliSubcommand Subcommand,
    string? TracePath,
    string? FixturePath,
    string? QuestDataRoot,
    string? OutputPath,
    bool Stdout,
    bool FailOnWarning,
    string Format,
    string? UnknownToken,
    string? ParseError);
