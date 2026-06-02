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
        => new() { RunId = runId, Data = new RunStartEvent.RunStartData { QuestId = questId } };

    internal static RunEndEvent End(
        string outcome = "done",
        string runId = "aaa",
        double offsetSeconds = 10)
        => new() { RunId = runId, Data = new RunEndEvent.RunEndData { Outcome = outcome } };

    internal static DecisionEvent Decision(
        string? stepId,
        string actionType,
        string runId = "aaa",
        double offsetSeconds = 1)
        => new() { RunId = runId, Data = new DecisionEvent.DecisionData { StepId = stepId, ActionType = actionType } };

    internal static ActionSubmittedEvent Submitted(
        string actionType,
        JsonElement? parameters,
        string runId = "aaa",
        double offsetSeconds = 2)
        => new() { RunId = runId, Data = new ActionSubmittedEvent.ActionSubmittedData { ActionType = actionType, Parameters = parameters } };

    internal static ActionCompletedEvent Completed(
        string actionType,
        string outcome = "ok",
        string runId = "aaa",
        double offsetSeconds = 3)
        => new() { RunId = runId, Data = new ActionCompletedEvent.ActionCompletedData { ActionType = actionType, Outcome = outcome } };

    internal static ObservationEvent Obs(
        string method,
        JsonElement? argument,
        JsonElement? value,
        string? runId = "aaa",
        double offsetSeconds = 0.5)
        => new() { RunId = runId ?? "", Data = new ObservationEvent.ObservationData { Method = method, Argument = argument, Value = value } };

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
    // New helpers for parity improvement tests (RED phase)
    // These reference types/methods that do not yet exist — they are intentionally
    // unresolvable until Builder implements the missing members.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="ObservationEvent"/> with Method="InventoryChanged" for use in tests.
    /// The value shape is {"gained":[{"itemId":N,"qty":N}],"lost":[...],"newHash":N}.
    /// <paramref name="gained"/> and <paramref name="lost"/> default to empty arrays.
    /// </summary>
    internal static ObservationEvent InventoryChanged(
        (uint id, int qty)[]? gained = null,
        (uint id, int qty)[]? lost   = null,
        uint newHash = 1u,
        string runId = "aaa",
        double offsetSeconds = 2.0)
    {
        var gainedArr = (gained ?? []).Select(t => new { itemId = t.id, qty = t.qty }).ToArray();
        var lostArr   = (lost   ?? []).Select(t => new { itemId = t.id, qty = t.qty }).ToArray();
        var value = JsonSerializer.SerializeToElement(
            new { gained = gainedArr, lost = lostArr, newHash });
        return new ObservationEvent
        {
            RunId = runId,
            Data = new ObservationEvent.ObservationData
            {
                Method   = "InventoryChanged",
                Argument = null,
                Value    = value
            }
        };
    }

    /// <summary>
    /// Builds the Parameters JsonElement for a HandOver action.
    /// Shape: {"target": npcId, "items": [itemId, ...]}
    /// </summary>
    internal static JsonElement HandOverParams(uint npcId, params uint[] itemIds)
        => JsonSerializer.SerializeToElement(new
        {
            target = npcId,
            items  = itemIds
        });

    /// <summary>
    /// Builds the Parameters JsonElement for a UseAethernet action.
    /// Shape: {"destinationShardId": shardId}
    /// </summary>
    internal static JsonElement UseAethernetParams(uint destinationShardId)
        => JsonSerializer.SerializeToElement(new { destinationShardId });

    /// <summary>
    /// Builds the Parameters JsonElement for a UseAethernet action with both source and
    /// destination shard IDs (Issue #25 — new sourceShardId field).
    /// Shape: {"sourceShardId": sourceShardId, "destinationShardId": destinationShardId}
    ///
    /// RED PHASE: This helper is referenced by tests 35, 37, 39 which will fail until
    /// Builder adds it AND updates TraceToQuestExtractor to read sourceShardId.
    /// </summary>
    internal static JsonElement UseAethernetParamsWithSource(uint sourceShardId, uint destinationShardId)
        => JsonSerializer.SerializeToElement(new { sourceShardId, destinationShardId });

    /// <summary>
    /// Builds a JsonElement representing an item-count adapter argument.
    /// Shape: {"value": itemId}
    /// </summary>
    internal static JsonElement ItemCountArg(uint itemId)
        => JsonSerializer.SerializeToElement(new { value = itemId });

    /// <summary>
    /// Builds a JsonElement representing an item-count adapter return value.
    /// Shape: {"value": count}
    /// </summary>
    internal static JsonElement ItemCountValue(int count)
        => JsonSerializer.SerializeToElement(new { value = count });

    /// <summary>
    /// Builds a JsonElement that is just a plain unsigned number (no wrapper object).
    /// Used to test plain-number argument shapes.
    /// </summary>
    internal static JsonElement PlainNumber(uint n)
        => JsonSerializer.SerializeToElement(n);

    /// <summary>
    /// Builds an <see cref="ObservationEvent"/> for the "IsAetheryteAttuned" method.
    /// <paramref name="value"/> is 0 or 1 (integer form, as emitted by authoring mode).
    /// </summary>
    internal static ObservationEvent ObsIsAetheryteAttuned(
        uint aetheryteId,
        int value = 1,
        string runId = "aaa",
        double offsetSeconds = 1.0)
        => Obs(
            method:        "IsAetheryteAttuned",
            argument:      ItemCountArg(aetheryteId),
            value:         IntValue(value),
            runId:         runId,
            offsetSeconds: offsetSeconds);

    /// <summary>
    /// Builds an <see cref="ObservationEvent"/> for the "AethernetShardTargeted" method.
    /// Only present in authoring/unified traces — not pure engine-run traces.
    /// </summary>
    internal static ObservationEvent ObsAethernetShardTargeted(
        uint shardId,
        string runId = "aaa",
        double offsetSeconds = 0.5)
        => Obs(
            method:        "AethernetShardTargeted",
            argument:      null,
            value:         PlainNumber(shardId),
            runId:         runId,
            offsetSeconds: offsetSeconds);

    /// <summary>
    /// Builds an <see cref="ObservationEvent"/> for the "GetItemCount" method.
    /// Argument shape: {"value": itemId}; value shape: {"value": count}.
    /// </summary>
    internal static ObservationEvent ObsGetItemCount(
        uint itemId,
        int count,
        string runId = "aaa",
        double offsetSeconds = 1.0)
        => Obs(
            method:        "GetItemCount",
            argument:      ItemCountArg(itemId),
            value:         ItemCountValue(count),
            runId:         runId,
            offsetSeconds: offsetSeconds);

    // -------------------------------------------------------------------------
    // JSONL serializer
    // -------------------------------------------------------------------------

    internal static string MakeTrace(params TraceEvent[] events)
    {
        var opts = TraceEventJsonContext.Default.Options;
        return string.Join("\n", events.Select(e => JsonSerializer.Serialize(e, e.GetType(), opts)));
    }
}
