using System.Text.Json;
using QuestForge.Adapters.Tracing;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Shared factory helpers for building trace events and JSONL strings in tests.
/// All helpers avoid any reference to QuestForge.Adapters.Fakes — events are built directly.
/// </summary>
internal static class TraceTestHelpers
{
    internal static readonly DateTimeOffset T0 =
        new(2026, 5, 16, 10, 0, 0, TimeSpan.Zero);

    // -------------------------------------------------------------------------
    // Event factories
    // -------------------------------------------------------------------------

    internal static RunStartEvent Start(
        string runId = "aaa",
        uint questId = 66130,
        uint schemaId = 1)
        => new(RunId: runId, QuestId: questId, QuestSchemaId: schemaId, At: T0);

    internal static RunEndEvent End(
        string outcome = "done",
        string runId = "aaa")
        => new(RunId: runId, Outcome: outcome, At: T0.AddSeconds(10));

    internal static DecisionEvent Decision(
        string? stepId,
        string actionType,
        string runId = "aaa",
        double offsetSeconds = 1)
        => new(RunId: runId, StepId: stepId, ActionType: actionType,
               At: T0.AddSeconds(offsetSeconds));

    internal static ActionSubmittedEvent Submitted(
        string actionType,
        JsonElement? parameters,
        string runId = "aaa",
        double offsetSeconds = 2)
        => new(RunId: runId, ActionType: actionType, Parameters: parameters,
               At: T0.AddSeconds(offsetSeconds));

    internal static ActionCompletedEvent Completed(
        string actionType,
        string outcome = "ok",
        string runId = "aaa",
        double offsetSeconds = 3)
        => new(RunId: runId, ActionType: actionType, Outcome: outcome,
               At: T0.AddSeconds(offsetSeconds));

    internal static ObservationEvent Obs(
        string method,
        JsonElement? argument,
        JsonElement? value,
        string? runId = "aaa",
        double offsetSeconds = 0.5)
        => new(RunId: runId, Method: method, Argument: argument, Value: value,
               At: T0.AddSeconds(offsetSeconds));

    // -------------------------------------------------------------------------
    // Parameters builders
    // -------------------------------------------------------------------------

    internal static JsonElement NavParams(float x, float y, float z, int zone, float stoppingDistance = 3.0f)
        => JsonSerializer.SerializeToElement(new
        {
            destination = new { x, y, z },
            zone,
            options = new { stoppingDistance }
        });

    internal static JsonElement InteractParams(uint npcId)
        => JsonSerializer.SerializeToElement(new { target = npcId });

    // -------------------------------------------------------------------------
    // Observation value / argument builders
    // -------------------------------------------------------------------------

    internal static JsonElement ZoneValue(uint zoneId)
        => JsonSerializer.SerializeToElement(new { value = zoneId });

    internal static JsonElement PositionValue(float x, float y, float z)
        => JsonSerializer.SerializeToElement(new { x, y, z });

    internal static JsonElement QuestIdArg(uint questId)
        => JsonSerializer.SerializeToElement(new { value = questId });

    internal static JsonElement IntValue(int v)
        => JsonSerializer.SerializeToElement(v);

    internal static JsonElement UintValue(uint v)
        => JsonSerializer.SerializeToElement(v);

    internal static JsonElement BoolValue(bool v)
        => JsonSerializer.SerializeToElement(v);

    internal static JsonElement FailureValue(string reason = "NotFound")
        => JsonSerializer.SerializeToElement(new { failure = reason, detail = "" });

    // -------------------------------------------------------------------------
    // JSONL serializer
    // -------------------------------------------------------------------------

    internal static string MakeTrace(params TraceEvent[] events)
    {
        var opts = TraceEventJsonContext.Default.Options;
        return string.Join("\n", events.Select(e => JsonSerializer.Serialize(e, e.GetType(), opts)));
    }
}
