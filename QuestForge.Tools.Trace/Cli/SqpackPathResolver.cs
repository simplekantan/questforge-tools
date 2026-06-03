namespace QuestForge.Tools.Trace.Cli;

public static class SqpackPathResolver
{
    public static string? Resolve()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack",
            @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack",
        ];

        foreach (var path in candidates)
            if (Directory.Exists(path))
                return path;

        return null;
    }
}
