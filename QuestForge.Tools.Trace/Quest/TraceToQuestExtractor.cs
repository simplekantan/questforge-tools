using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using QuestForge.Schema;

namespace QuestForge.Tools.Trace.Quest;

/// <summary>
/// Replays a trace event sequence to produce a <see cref="QuestDraftResult"/> containing
/// a partially-inferred <see cref="QuestDefinition"/> and a checklist of TODOs for
/// the human author.
/// </summary>
public sealed class TraceToQuestExtractor
{
    private readonly StepInferenceEngine _inference;

    public TraceToQuestExtractor(StepInferenceEngine? inference = null)
        => _inference = inference ?? new StepInferenceEngine();

    /// <summary>
    /// Extract a quest draft from the supplied event list.
    /// Returns <see cref="Result{T}.Failure"/> with code <c>"no-run-start"</c> when no
    /// <see cref="RunStartEvent"/> is present.
    ///
    /// Fast path: if <see cref="StepRecordedEvent"/> entries are present (authoring trace),
    /// the quest definition is reconstructed directly from those events without observation
    /// correlation. Falls back to the observation-based algorithm when none are present.
    /// </summary>
    public Result<QuestDraftResult> Extract(IReadOnlyList<TraceEvent> events)
    {
        // Step 1: locate RunStartEvent
        var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
        if (runStart == null)
            return Result.Fail<QuestDraftResult>("no-run-start", "trace contains no run.start event");

        var runId = runStart.RunId;
        var activeQuest = new QuestId(runStart.QuestId);

        // Fast path: authoring traces carry StepRecordedEvent entries which contain the
        // full serialised Step. Use those directly — no observation correlation needed.
        var stepRecordedEvents = events
            .OfType<StepRecordedEvent>()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.At)
            .ToList();

        if (stepRecordedEvents.Count > 0)
            return ExtractFromStepRecordedEvents(runStart, stepRecordedEvents);

        // Step 2: initialise SnapshotState
        var snapshot = new SnapshotState(activeQuest);

        // Find index of RunStart and first Decision
        int runStartIdx = -1;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is RunStartEvent rs && rs.RunId == runId)
            {
                runStartIdx = i;
                break;
            }
        }

        // Step 3: Pre-roll — apply ObservationEvents between RunStart and first Decision
        int firstDecisionIdx = -1;
        for (int i = runStartIdx + 1; i < events.Count; i++)
        {
            if (events[i] is DecisionEvent dec && dec.RunId == runId)
            {
                firstDecisionIdx = i;
                break;
            }
        }

        int preRollEnd = firstDecisionIdx >= 0 ? firstDecisionIdx : events.Count;
        for (int i = runStartIdx + 1; i < preRollEnd; i++)
        {
            if (events[i] is ObservationEvent obs)
                snapshot.Apply(obs);
        }

        // Step 4: index all decisions in order (matching runId)
        var decisions = new List<(int EventIndex, DecisionEvent Decision)>();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is DecisionEvent dec && dec.RunId == runId)
                decisions.Add((i, dec));
        }

        // Find RunEndEvent
        var runEnd = events.OfType<RunEndEvent>().LastOrDefault(e => e.RunId == runId);

        // Working list of (groupKey, step)
        var workingList = new List<(int GroupKey, Step Step)>();
        var todos = new List<string>();
        var hasAcceptStepWithUnknownPosition = false;

        // Step 5: for each decision
        for (int i = 0; i < decisions.Count; i++)
        {
            var (decisionIdx, decision) = decisions[i];

            // Skip decisions after RunEnd
            if (runEnd != null && decision.At > runEnd.At) continue;

            // Skip terminal actions and wait — UNLESS combat correlation is present in the pre-roll.
            // We must evaluate the snapshot before deciding to skip, because all combat observations
            // may have landed in the pre-roll (before this first decision).
            var actionTypeLower = decision.ActionType.ToLowerInvariant();
            bool isSkippableAction = TraceConstants.IsTerminalAction(actionTypeLower) || actionTypeLower == TraceConstants.ActionWait;

            // a. before snapshot
            var before = snapshot.ToSnapshot(decision.At, activeQuest);

            // b. find first ActionSubmittedEvent after decision with same runId
            ActionSubmittedEvent? submitted = null;
            int submittedIdx = -1;
            for (int j = decisionIdx + 1; j < events.Count; j++)
            {
                if (events[j] is ActionSubmittedEvent sub && sub.RunId == runId)
                {
                    submitted = sub;
                    submittedIdx = j;
                    break;
                }
            }

            // c. find first ActionCompletedEvent after submitted
            ActionCompletedEvent? completed = null;
            int completedIdx = -1;
            for (int j = submittedIdx + 1; j < events.Count; j++)
            {
                if (events[j] is ActionCompletedEvent comp && comp.RunId == runId)
                {
                    completed = comp;
                    completedIdx = j;
                    break;
                }
            }

            // d. If Interact, record the NPC interaction BEFORE advance
            var submittedTypeLower = submitted?.ActionType.ToLowerInvariant() ?? string.Empty;
            if (submitted != null && submittedTypeLower == "interact" && submitted.Parameters.HasValue)
            {
                uint npcId = 0;
                var p = submitted.Parameters.Value;
                if (p.TryGetProperty("target", out var tgt))
                    { try { npcId = tgt.GetUInt32(); } catch { } }
                else if (p.TryGetProperty("value", out var val))
                    { try { npcId = val.GetUInt32(); } catch { } }

                if (npcId != 0)
                    snapshot.RecordInteract(new NpcId(npcId));
            }

            // e. Advance snapshot: apply ObservationEvents between completed (exclusive)
            //    and next decision (exclusive)
            int advanceStart = completedIdx >= 0 ? completedIdx + 1 : (submittedIdx >= 0 ? submittedIdx + 1 : decisionIdx + 1);
            int advanceEnd = (i + 1 < decisions.Count) ? decisions[i + 1].EventIndex : events.Count;

            // Collect ObservationEvents with Method="InventoryChanged" in this window for HandOver fallback
            var inventoryChangedInWindow = new List<ObservationEvent>();

            for (int j = advanceStart; j < advanceEnd; j++)
            {
                if (events[j] is ObservationEvent obs)
                {
                    snapshot.Apply(obs);
                    if (obs.Method == "InventoryChanged")
                        inventoryChangedInWindow.Add(obs);
                }
            }

            // f. afterAt
            DateTimeOffset afterAt;
            if (i + 1 < decisions.Count)
                afterAt = decisions[i + 1].Decision.At;
            else if (runEnd != null)
                afterAt = runEnd.At;
            else if (completed != null)
                afterAt = completed.At;
            else
                afterAt = decision.At;

            // g. after snapshot
            var after = snapshot.ToSnapshot(afterAt, activeQuest);

            // h. inference
            var inference = _inference.Infer(before, after);

            // i. Build step
            var stepId = !string.IsNullOrEmpty(inference.SuggestedStepId)
                ? inference.SuggestedStepId
                : $"step-{i}";

            Step? step = null;

            // Combat takes precedence: emit CombatStep when inference returns "combat",
            // regardless of action type (handles wait decisions and missing submitted events).
            // WHY StepFactory: the offline extractor must produce the SAME CombatStep the live
            // authoring path (SnapshotAggregator → StepFactory) produces for the same event
            // stream — including Zone/RequiredZone. Building through the shared factory keeps
            // live and offline in lock-step; hand-rolling the step here previously left
            // Zone/RequiredZone unset, a live-vs-offline divergence.
            if (inference.StepType == "combat")
            {
                step = StepFactory.Build("combat", stepId, inference.SuggestedExpect, after, before);
                todos.Add($"combat step {stepId}: review Spawn (defaulted overworldEnemies) and KillEnemyDataIds");

                var groupKey2 = before.QuestSequence;
                workingList.Add((groupKey2, step));
                snapshot.ResetPendingKeyItemDeltas();
                continue;
            }

            // After inference, apply the skippable-action check (wait, terminal).
            // We deferred this so inference could run on the pre-roll correlation.
            if (isSkippableAction)
            {
                snapshot.ResetPendingKeyItemDeltas();
                continue;
            }

            // If no submitted action and inference is not combat, skip.
            if (submitted == null)
            {
                todos.Add($"decision at seq {before.QuestSequence} had no action.submitted; skipped");
                snapshot.ResetPendingKeyItemDeltas();
                continue;
            }

            if (submittedTypeLower == "navigate")
            {
                // Parse destination
                float x = 0f, y = 0f, z = 0f;
                int zone = 0;

                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("destination", out var dest))
                    {
                        try { x = dest.TryGetProperty("x", out var xp) ? xp.GetSingle() : 0f; } catch { }
                        try { y = dest.TryGetProperty("y", out var yp) ? yp.GetSingle() : 0f; } catch { }
                        try { z = dest.TryGetProperty("z", out var zp) ? zp.GetSingle() : 0f; } catch { }
                    }
                    else
                    {
                        // Fallback: top-level x/y/z
                        try { x = p.TryGetProperty("x", out var xp) ? xp.GetSingle() : 0f; } catch { }
                        try { y = p.TryGetProperty("y", out var yp) ? yp.GetSingle() : 0f; } catch { }
                        try { z = p.TryGetProperty("z", out var zp) ? zp.GetSingle() : 0f; } catch { }
                    }

                    if (p.TryGetProperty("zone", out var zoneEl))
                    {
                        try { zone = zoneEl.GetInt32(); } catch { }
                    }
                }

                // Record navigate destination for use by subsequent interact/handover/attunement steps
                snapshot.RecordNavigateDestination(x, y, z);

                float? stopDist = null;
                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("options", out var opts) && opts.TryGetProperty("stoppingDistance", out var sd))
                        try { stopDist = sd.GetSingle(); } catch { }
                }

                ExpectValue? expect = null;
                if (!string.IsNullOrEmpty(inference.SuggestedExpect))
                    expect = new PredicateExpect { Predicate = inference.SuggestedExpect };

                step = new TravelStep
                {
                    Id = stepId,
                    Destination = new TravelDestination(zone, new Position3(x, y, z)),
                    Expect = expect,
                    StopDistance = stopDist
                };
            }
            else if (submittedTypeLower == "interact")
            {
                // Parse NPC ID
                uint npcId = 0;
                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("target", out var tgt))
                        try { npcId = tgt.GetUInt32(); } catch { }
                    else if (p.TryGetProperty("value", out var val))
                        try { npcId = val.GetUInt32(); } catch { }
                }

                var npcZone = (int)(before.Zone.Value);
                // Use LastNpcPosition from preceding Navigate if available
                var npcPos = snapshot.LastNpcPosition is { } lnp
                    ? new Position3(lnp.X, lnp.Y, lnp.Z)
                    : new Position3(0, 0, 0);
                var npcLocation = new NpcLocation(npcId, npcZone, npcPos);

                ExpectValue? expect = null;
                if (!string.IsNullOrEmpty(inference.SuggestedExpect))
                    expect = new PredicateExpect { Predicate = inference.SuggestedExpect };

                if (inference.InferredFrom == InferredFrom.QuestAccepted)
                {
                    step = new AcceptStep
                    {
                        Id = stepId,
                        Target = npcLocation,
                        Expect = expect
                    };
                    if (npcLocation.Position is { X: 0, Y: 0, Z: 0 })
                        hasAcceptStepWithUnknownPosition = true;
                }
                else if (inference.InferredFrom == InferredFrom.QuestCompleted)
                {
                    step = new TurnInStep
                    {
                        Id = stepId,
                        Target = npcLocation,
                        Expect = expect
                    };
                }
                else if (inference.StepType == TraceConstants.ActionAttune)
                {
                    // B22: inference engine detected attunement (AethernetShardTargeted was present)
                    var aetheryteId = after.LastAttuned?.Value ?? npcId;
                    step = new AttunementStep
                    {
                        Id = $"attune-aetheryte-{aetheryteId}",
                        Target = new Schema.AetheryteId(aetheryteId),
                        Location = npcLocation,
                        Expect = expect
                    };
                }
                else if (before.LastAttuned != after.LastAttuned
                    && after.LastAttuned.HasValue
                    && after.LastAttuned.Value.Value == npcId)
                {
                    // B23 fallback: no AethernetShardTargeted, but IsAetheryteAttuned transitioned 0→1
                    // and the newly attuned ID matches the submitted NPC target
                    var aetheryteId = after.LastAttuned.Value.Value;
                    step = new AttunementStep
                    {
                        Id = $"attune-aetheryte-{aetheryteId}",
                        Target = new Schema.AetheryteId(aetheryteId),
                        Location = npcLocation,
                        Expect = expect
                    };
                }
                else
                {
                    step = new TalkStep
                    {
                        Id = stepId,
                        Target = npcLocation,
                        Expect = expect
                    };
                }
            }
            else if (submittedTypeLower == TraceConstants.ActionHandover)
            {
                // Parse NPC ID from parameters.target
                uint npcId = 0;
                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("target", out var tgt))
                        try { npcId = tgt.GetUInt32(); } catch { }
                }

                var npcZone = (int)(before.Zone.Value);
                var npcPos = snapshot.LastNpcPosition is { } lnp
                    ? new Position3(lnp.X, lnp.Y, lnp.Z)
                    : new Position3(0, 0, 0);
                var npcLocation = new NpcLocation(npcId, npcZone, npcPos);

                // Parse item IDs from parameters.items first
                uint[] itemIds = [];
                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("items", out var itemsEl)
                        && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        var parsed = new List<uint>();
                        foreach (var el in itemsEl.EnumerateArray())
                        {
                            try { parsed.Add(el.GetUInt32()); } catch { }
                        }
                        itemIds = parsed.ToArray();
                    }
                }

                // Fallback: collect Lost item IDs from InventoryChanged ObservationEvents in window
                if (itemIds.Length == 0 && inventoryChangedInWindow.Count > 0)
                {
                    var fromInventory = new List<uint>();
                    foreach (var inv in inventoryChangedInWindow)
                    {
                        if (!inv.Value.HasValue) continue;
                        var root = inv.Value.Value;
                        if (root.ValueKind != JsonValueKind.Object) continue;
                        if (!root.TryGetProperty("lost", out var lostArr)) continue;
                        if (lostArr.ValueKind != JsonValueKind.Array) continue;
                        foreach (var item in lostArr.EnumerateArray())
                        {
                            if (item.TryGetProperty("itemId", out var idEl))
                                try { fromInventory.Add(idEl.GetUInt32()); } catch { }
                        }
                    }
                    itemIds = fromInventory.ToArray();
                }

                // If still no items, add a TODO
                if (itemIds.Length == 0)
                    todos.Add($"TODO: fill in items handed over at step {stepId} — could not infer from parameters or inventory events");

                ExpectValue? expect = null;
                if (!string.IsNullOrEmpty(inference.SuggestedExpect))
                    expect = new PredicateExpect { Predicate = inference.SuggestedExpect };

                step = new HandOverItemStep
                {
                    Id = stepId,
                    Target = npcLocation,
                    Items = itemIds,
                    Expect = expect
                };
            }
            else if (submittedTypeLower == TraceConstants.ActionUseAethernet)
            {
                // Parse destination shard ID from parameters.destinationShardId or parameters.shardId
                uint destShardId = 0;
                uint sourceShardId = 0;
                if (submitted.Parameters.HasValue)
                {
                    var p = submitted.Parameters.Value;
                    if (p.TryGetProperty("destinationShardId", out var sid))
                        try { destShardId = sid.GetUInt32(); } catch { }
                    else if (p.TryGetProperty("shardId", out var sid2))
                        try { destShardId = sid2.GetUInt32(); } catch { }

                    if (p.TryGetProperty("sourceShardId", out var srcEl))
                        try { sourceShardId = srcEl.GetUInt32(); } catch { }
                }

                if (destShardId == 0)
                    todos.Add($"TODO: unknown aethernet destination shard at step {stepId} — fill in destinationShardId manually");

                // Destination zone from after snapshot
                int destZone = after.Zone.Value > 0 ? (int)after.Zone.Value : (int)before.Zone.Value;

                ExpectValue? expect = null;
                if (!string.IsNullOrEmpty(inference.SuggestedExpect))
                    expect = new PredicateExpect { Predicate = inference.SuggestedExpect };

                var aethernet = destShardId > 0
                    ? new AethernetRouteHint(
                        From: sourceShardId == 0 ? null : sourceShardId,
                        To: destShardId)
                    : null;

                step = new TravelStep
                {
                    Id = stepId,
                    Destination = new TravelDestination(destZone, null),
                    RouteHint = new RouteHint(Aethernet: aethernet),
                    Expect = expect
                };
            }
            else
            {
                // Generic step — best effort
                todos.Add($"unrecognised action type '{submitted.ActionType}' at step {stepId}; manual edit required");
                // Skip emitting a step for unknown types
                snapshot.ResetPendingKeyItemDeltas();
                continue;
            }

            // j. groupKey = before.QuestSequence
            var groupKey = before.QuestSequence;
            workingList.Add((groupKey, step));

            // k. Reset pending key-item deltas so they don't leak into the next decision
            snapshot.ResetPendingKeyItemDeltas();
        }

        // Step 6: Group by groupKey preserving order
        var sequences = new List<QuestSequence>();
        if (workingList.Count > 0)
        {
            var currentKey = workingList[0].GroupKey;
            var currentSteps = new List<Step>();

            foreach (var (key, s) in workingList)
            {
                if (key != currentKey)
                {
                    sequences.Add(new QuestSequence { Sequence = currentKey, Steps = currentSteps.ToArray() });
                    currentKey = key;
                    currentSteps = [];
                }
                currentSteps.Add(s);
            }
            sequences.Add(new QuestSequence { Sequence = currentKey, Steps = currentSteps.ToArray() });
        }

        // Step 7: assemble QuestDefinition
        // Find AcceptFrom from first AcceptStep
        NpcLocation? acceptFrom = null;
        foreach (var (_, s) in workingList)
        {
            if (s is AcceptStep acceptStep)
            {
                acceptFrom = acceptStep.Target;
                break;
            }
        }
        acceptFrom ??= new NpcLocation(0u, 0, new Position3(0, 0, 0));

        // Step 8: collect TODOs
        todos.Insert(0, "name (Lumina lookup required)");
        todos.Insert(1, "expansion");
        todos.Insert(2, "category");
        todos.Insert(3, "lastVerifiedPatch");
        todos.Insert(4, "requirements (level, job, prereqs)");

        if (hasAcceptStepWithUnknownPosition)
            todos.Add("acceptFrom NPC position");

        var definition = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = runStart.QuestId,
            Name              = "TODO",
            Expansion         = "TODO",
            Category          = "TODO",
            Enabled           = true,
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "TODO",
            Requirements      = new Requirements(),
            AcceptFrom        = acceptFrom,
            Sequences         = sequences.ToArray()
        };

        return Result.Ok(new QuestDraftResult(definition, todos));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Fast path: reconstruct from StepRecordedEvent entries (authoring traces)
    // ────────────────────────────────────────────────────────────────────────

    private static Result<QuestDraftResult> ExtractFromStepRecordedEvents(
        RunStartEvent runStart,
        List<StepRecordedEvent> stepRecordedEvents)
    {
        // Deserialise each Step from the embedded JSON element using quest file options.
        var workingList = new List<(int GroupKey, Step Step)>();
        var todos = new List<string>();

        foreach (var evt in stepRecordedEvents)
        {
            Step? step = null;
            try
            {
                step = evt.Step.Deserialize<Step>(QuestForgeJsonContext.QuestFileOptions);
            }
            catch
            {
                todos.Add($"TODO: could not deserialise step '{evt.StepId}' at sequence {evt.SequenceNumber}; manual edit required");
                continue;
            }

            if (step is null)
            {
                todos.Add($"TODO: step '{evt.StepId}' at sequence {evt.SequenceNumber} deserialised as null; manual edit required");
                continue;
            }

            workingList.Add((evt.SequenceNumber, step));
        }

        // Group by SequenceNumber preserving insertion order.
        var sequences = new List<QuestSequence>();
        if (workingList.Count > 0)
        {
            var currentKey   = workingList[0].GroupKey;
            var currentSteps = new List<Step>();

            foreach (var (key, s) in workingList)
            {
                if (key != currentKey)
                {
                    sequences.Add(new QuestSequence { Sequence = currentKey, Steps = currentSteps.ToArray() });
                    currentKey   = key;
                    currentSteps = [];
                }
                currentSteps.Add(s);
            }
            sequences.Add(new QuestSequence { Sequence = currentKey, Steps = currentSteps.ToArray() });
        }

        // Find acceptFrom from first AcceptStep.
        NpcLocation? acceptFrom = null;
        foreach (var (_, s) in workingList)
        {
            if (s is AcceptStep acceptStep)
            {
                acceptFrom = acceptStep.Target;
                break;
            }
        }
        acceptFrom ??= new NpcLocation(0u, 0, new Position3(0, 0, 0));

        // Standard TODOs for fields that cannot be inferred from the trace.
        todos.Insert(0, "name (Lumina lookup required)");
        todos.Insert(1, "expansion");
        todos.Insert(2, "category");
        todos.Insert(3, "lastVerifiedPatch");
        todos.Insert(4, "requirements (level, job, prereqs)");

        var definition = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = runStart.QuestId,
            Name              = "TODO",
            Expansion         = "TODO",
            Category          = "TODO",
            Enabled           = true,
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "TODO",
            Requirements      = new Requirements(),
            AcceptFrom        = acceptFrom,
            Sequences         = sequences.ToArray()
        };

        return Result.Ok(new QuestDraftResult(definition, todos));
    }
}
