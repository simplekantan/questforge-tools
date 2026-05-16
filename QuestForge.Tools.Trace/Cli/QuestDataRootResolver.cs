namespace QuestForge.Tools.Trace.Cli;

public static class QuestDataRootResolver
{
    /// <summary>
    /// Resolve a quest-data root using the probe algorithm.
    /// Returns the absolute, normalised path to the root, or null if no candidate
    /// directory contains a "quests/" subdirectory.
    /// </summary>
    /// <param name="workingDirectory">The directory used as the probe anchor. Defaults to Environment.CurrentDirectory.</param>
    public static string? Resolve(string? workingDirectory = null)
    {
        var cwd = workingDirectory ?? Environment.CurrentDirectory;

        // Probe 1: cwd itself contains a "quests/" subdirectory
        if (Directory.Exists(Path.Combine(cwd, "quests")))
            return Path.GetFullPath(cwd);

        // Probe 2: sibling "questforge-data" repo contains a "quests/" subdirectory
        var sibling = Path.GetFullPath(Path.Combine(cwd, "..", "questforge-data"));
        if (Directory.Exists(Path.Combine(sibling, "quests")))
            return sibling;

        return null;
    }
}
