namespace QuestForge.Tools.Trace.Cli;

public static class QuestDataRootResolver
{
    /// <summary>
    /// Resolve a quest-data root using the probe algorithm.
    /// Returns the absolute, normalised path to the root, or null if no candidate
    /// directory contains a "quests/" subdirectory.
    /// </summary>
    /// <param name="workingDirectory">The directory used as the probe anchor. Defaults to Environment.CurrentDirectory.</param>
    public static string? Resolve(string? workingDirectory = null) => throw new NotImplementedException();
}
