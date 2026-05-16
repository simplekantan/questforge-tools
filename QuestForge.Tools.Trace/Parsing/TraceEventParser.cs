using QuestForge.Adapters.Tracing;

namespace QuestForge.Tools.Trace.Parsing;

/// <summary>
/// Reads JSONL trace streams into typed <see cref="TraceEvent"/> lists.
/// Blank lines and unrecognised/malformed JSON lines are skipped with a warning.
/// </summary>
public static class TraceEventParser
{
    /// <summary>
    /// Reads a JSONL trace file from disk.
    /// Lines that are blank or fail to deserialize are skipped with a warning written to
    /// the supplied writer (defaults to <see cref="Console.Error"/>).
    /// </summary>
    public static IReadOnlyList<TraceEvent> ReadFile(string path, TextWriter? warnings = null)
        => throw new NotImplementedException();

    /// <summary>Reads from a <see cref="Stream"/>.</summary>
    public static IReadOnlyList<TraceEvent> ReadStream(Stream stream, TextWriter? warnings = null)
        => throw new NotImplementedException();

    /// <summary>Reads from a raw JSONL string.</summary>
    public static IReadOnlyList<TraceEvent> ReadText(string jsonl, TextWriter? warnings = null)
        => throw new NotImplementedException();
}
