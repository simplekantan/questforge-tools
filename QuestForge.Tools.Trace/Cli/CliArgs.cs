namespace QuestForge.Tools.Trace.Cli;

public sealed record CliArgs(
    CliSubcommand Subcommand,
    string? TracePath,
    string? FixturePath,
    string? QuestDataRoot,
    string? OutputPath,
    string? SqpackPath,
    bool Stdout,
    bool FailOnWarning,
    string Format,
    string? UnknownToken,
    string? ParseError,
    bool WithTrace = true,
    int? MinCoverage = null,
    string? MarkdownPath = null,
    string? BadgePath = null,
    string? BadgeDirPath = null);
