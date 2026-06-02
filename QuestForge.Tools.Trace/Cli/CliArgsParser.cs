namespace QuestForge.Tools.Trace.Cli;

public static class CliArgsParser
{
    // Value-consuming flags (require a following token)
    private static readonly HashSet<string> ValueFlags = new(StringComparer.Ordinal)
    {
        "--quest-data", "--out", "--format",
    };

    // Boolean flags (no following token)
    private static readonly HashSet<string> BoolFlags = new(StringComparer.Ordinal)
    {
        "--stdout", "--fail-on-warning", "--with-trace", "--no-trace",
    };

    public static CliArgs Parse(string[] args)
    {
        // Zero args
        if (args.Length == 0)
            return Default(CliSubcommand.None);

        // Detect subcommand
        var subcommand = args[0] switch
        {
            "--help" or "-h" or "help"   => CliSubcommand.Help,
            "extract-fixture"            => CliSubcommand.ExtractFixture,
            "validate-fixture"           => CliSubcommand.ValidateFixture,
            "list-fixtures"              => CliSubcommand.ListFixtures,
            "extract-quest"              => CliSubcommand.ExtractQuest,
            "validate"                   => CliSubcommand.ValidateTrace,
            _                            => CliSubcommand.Unknown,
        };

        if (subcommand == CliSubcommand.Help)
            return Default(CliSubcommand.Help);

        if (subcommand == CliSubcommand.Unknown)
            return Default(CliSubcommand.Unknown) with { UnknownToken = args[0] };

        // Parse remaining args
        string? tracePath      = null;
        string? fixturePath    = null;
        string? questDataRoot  = null;
        string? outputPath     = null;
        bool    stdout         = false;
        bool    failOnWarning  = false;
        string  format         = "text";
        string? parseError     = null;
        bool    positionalSeen = false;
        bool    withTrace      = true; // default ON for extract-fixture

        int i = 1;
        while (i < args.Length)
        {
            var token = args[i];

            if (token.StartsWith("--"))
            {
                if (ValueFlags.Contains(token))
                {
                    // Need a following value
                    if (i + 1 >= args.Length)
                    {
                        parseError = $"flag {token} requires a value";
                        break;
                    }
                    var value = args[i + 1];
                    switch (token)
                    {
                        case "--quest-data": questDataRoot = value; break;
                        case "--out":        outputPath    = value; break;
                        case "--format":     format        = value; break;
                    }
                    i += 2;
                }
                else if (BoolFlags.Contains(token))
                {
                    switch (token)
                    {
                        case "--stdout":         stdout        = true;  break;
                        case "--fail-on-warning": failOnWarning = true;  break;
                        case "--with-trace":     withTrace     = true;  break;
                        case "--no-trace":       withTrace     = false; break;
                    }
                    i++;
                }
                else
                {
                    parseError = $"unknown flag: {token}";
                    break;
                }
            }
            else
            {
                // Positional argument
                if (positionalSeen)
                {
                    parseError = $"unexpected positional argument: {token}";
                    break;
                }
                positionalSeen = true;

                // Route to the appropriate field depending on subcommand
                if (subcommand == CliSubcommand.ValidateFixture)
                    fixturePath = token;
                else
                    tracePath = token;

                i++;
            }
        }

        return new CliArgs(
            Subcommand:    subcommand,
            TracePath:     tracePath,
            FixturePath:   fixturePath,
            QuestDataRoot: questDataRoot,
            OutputPath:    outputPath,
            Stdout:        stdout,
            FailOnWarning: failOnWarning,
            Format:        format,
            UnknownToken:  null,
            ParseError:    parseError,
            WithTrace:     withTrace);
    }

    private static CliArgs Default(CliSubcommand subcommand) =>
        new(subcommand,
            TracePath:     null,
            FixturePath:   null,
            QuestDataRoot: null,
            OutputPath:    null,
            Stdout:        false,
            FailOnWarning: false,
            Format:        "text",
            UnknownToken:  null,
            ParseError:    null);
}
