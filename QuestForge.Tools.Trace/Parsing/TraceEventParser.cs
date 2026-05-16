using System.Text.Json;
using System.Text.Json.Nodes;
using QuestForge.Adapters.Tracing;

namespace QuestForge.Tools.Trace.Parsing;

/// <summary>
/// Reads JSONL trace streams into typed <see cref="TraceEvent"/> lists.
/// Blank lines and unrecognised/malformed JSON lines are skipped with a warning.
/// Handles both traces with a "type" discriminator (Phase 7+ recorder) and traces
/// without one (test helpers that serialize via the concrete C# type).
/// </summary>
public static class TraceEventParser
{
    /// <summary>
    /// Reads a JSONL trace file from disk.
    /// Lines that are blank or fail to deserialize are skipped with a warning written to
    /// the supplied writer (defaults to <see cref="Console.Error"/>).
    /// </summary>
    public static IReadOnlyList<TraceEvent> ReadFile(string path, TextWriter? warnings = null)
    {
        using var stream = File.OpenRead(path);
        return ReadStream(stream, warnings);
    }

    /// <summary>Reads from a <see cref="Stream"/>.</summary>
    public static IReadOnlyList<TraceEvent> ReadStream(Stream stream, TextWriter? warnings = null)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var content = reader.ReadToEnd();
        return ReadText(content, warnings);
    }

    /// <summary>Reads from a raw JSONL string.</summary>
    public static IReadOnlyList<TraceEvent> ReadText(string jsonl, TextWriter? warnings = null)
    {
        var result = new List<TraceEvent>();
        var lines = jsonl.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var ev = ParseLine(line, warnings);
                if (ev != null) result.Add(ev);
            }
            catch (JsonException ex)
            {
                var w = warnings ?? Console.Error;
                w.WriteLine($"[warn] skipping malformed line: {ex.Message}");
            }
            catch (Exception ex)
            {
                var w = warnings ?? Console.Error;
                w.WriteLine($"[warn] skipping malformed line: {ex.Message}");
            }
        }
        return result;
    }

    private static TraceEvent? ParseLine(string line, TextWriter? warnings)
    {
        // First try with the polymorphic context (requires "type" discriminator)
        // If the JSON has a "type" field, STJ will use it for polymorphic dispatch.
        // If not (e.g. test helpers that serialize by concrete type), fall back to heuristic.
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        // Check if "type" discriminator is present
        if (root.TryGetProperty("type", out var typeEl))
        {
            var typeStr = typeEl.GetString();
            return typeStr switch
            {
                "run.start" when root.TryGetProperty("questId", out _) =>
                    JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                "run.end" => JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                "observation" => JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                "decision" => JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                "action.submitted" => JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                "action.completed" => JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.TraceEvent),
                _ => WarnAndSkip(warnings, $"unknown type discriminator: {typeStr}")
            };
        }

        // No "type" discriminator: detect by properties present
        return DetectAndDeserialize(root, line, warnings);
    }

    private static TraceEvent? DetectAndDeserialize(JsonElement root, string line, TextWriter? warnings)
    {
        var opts = TraceEventJsonContext.Default.Options;

        // Unique property heuristics (most-specific first):
        if (root.TryGetProperty("questId", out _) && root.TryGetProperty("questSchemaId", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.RunStartEvent);

        if (root.TryGetProperty("outcome", out _) && root.TryGetProperty("runId", out _)
            && !root.TryGetProperty("actionType", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.RunEndEvent);

        if (root.TryGetProperty("method", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.ObservationEvent);

        if (root.TryGetProperty("stepId", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.DecisionEvent);

        if (root.TryGetProperty("actionType", out _) && root.TryGetProperty("parameters", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.ActionSubmittedEvent);

        if (root.TryGetProperty("actionType", out _) && root.TryGetProperty("outcome", out _))
            return JsonSerializer.Deserialize(line, TraceEventJsonContext.Default.ActionCompletedEvent);

        warnings?.WriteLine($"[warn] skipping unrecognised event shape");
        return null;
    }

    private static TraceEvent? WarnAndSkip(TextWriter? warnings, string message)
    {
        warnings?.WriteLine($"[warn] skipping malformed line: {message}");
        return null;
    }
}
