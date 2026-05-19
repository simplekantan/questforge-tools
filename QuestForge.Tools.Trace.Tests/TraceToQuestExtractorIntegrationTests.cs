using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
using System.Text.Json;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Integration test for <see cref="TraceToQuestExtractor"/>.
/// Test 35 from the parity improvement spec.
/// All tests are RED: they will fail until Builder implements the missing action types.
/// </summary>
public sealed class TraceToQuestExtractorIntegrationTests
{
    // -------------------------------------------------------------------------
    // Test 35 — Full quest flow: accept, attune, useaethernet, handover, turn-in
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_FullQuestFlow_AllStepTypesPresent()
    {
        /*
         * RED: Will fail until Builder implements HandOver, UseAethernet, AttunementStep,
         * and attunement detection in TraceToQuestExtractor.
         *
         * CONTRACT: Given a minimal synthetic trace representing a complete quest run with:
         *   1. AcceptStep  — Interact on NPC 1000 → IsQuestAccepted transitions false→true
         *   2. AttunementStep — AethernetShardTargeted(8) + Interact(8) + IsAetheryteAttuned(8)=1
         *   3. TravelStep (UseAethernet) — UseAethernet(destinationShardId=77) → zone 201
         *   4. HandOverItemStep — HandOver(NPC=2000, items=[501])
         *   5. TurnInStep  — Interact on NPC 3000 → IsQuestComplete transitions false→true
         *
         *           When  Extract,
         *           Then  the flattened list of all steps contains, in order:
         *                 AcceptStep, AttunementStep, TravelStep, HandOverItemStep, TurnInStep.
         *
         * NOTE: The Navigate before each action may produce a TravelStep that groups with
         *       or before the action step. The assertion checks only that all five required
         *       step types appear in the correct relative order — it does not over-constrain
         *       the total step count.
         *
         * BUILDER GUIDANCE:
         *   - Wire up all five action type handlers in TraceToQuestExtractor.
         *   - Order of steps in flattened output must respect the trace timestamp order.
         *   - Navigate steps that bracket each action are acceptable in the output and must
         *     not displace the required step types from their relative order.
         */

        // Arrange — build the synthetic trace event by event
        var opts = TraceEventJsonContext.Default.Options;

        // Helper: serialize InventoryChangedEvent with the discriminator.
        string SerializeInventory(InventoryChangedEvent ev)
            => JsonSerializer.Serialize<TraceEvent>(ev, opts);

        // Core trace parts using MakeTrace (no discriminator) + manual inventory lines.
        var tracePart1 = MakeTrace(
            Start(questId: 66130u),

            // --- Pre-roll observations ---
            Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u),    offsetSeconds: 0.1),
            Obs("GetQuestSequence", argument: QuestIdArg(66130u), value: IntValue(0), offsetSeconds: 0.2),
            Obs("IsQuestAccepted",  argument: QuestIdArg(66130u), value: BoolValue(false), offsetSeconds: 0.3),

            // --- Step 1: Accept quest (navigate to NPC then interact) ---
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(10f, 0f, 10f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Decision(null, "interact", offsetSeconds: 2.1),
            Submitted("Interact", InteractParams(1000u), offsetSeconds: 2.6),
            Completed("Interact", "ok", offsetSeconds: 3),
            Obs("IsQuestAccepted", argument: QuestIdArg(66130u), value: BoolValue(true), offsetSeconds: 3.5),

            // --- Step 2: Attune to aetheryte 8 (unified trace — shard targeted first) ---
            ObsAethernetShardTargeted(shardId: 8u, offsetSeconds: 4.0),
            Decision(null, "interact", offsetSeconds: 4.1),
            Submitted("Interact", InteractParams(8u), offsetSeconds: 4.6),
            Completed("Interact", "ok", offsetSeconds: 5),
            ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1, offsetSeconds: 5.5),

            // --- Step 3: UseAethernet to zone 201 ---
            Decision(null, "useaethernet", offsetSeconds: 6),
            Submitted("UseAethernet", UseAethernetParams(77u), offsetSeconds: 6.5),
            Completed("UseAethernet", "ok", offsetSeconds: 7),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(201u), offsetSeconds: 7.5),

            // --- Step 4: HandOver item 501 to NPC 2000 ---
            Decision(null, "navigate", offsetSeconds: 8),
            Submitted("Navigate", NavParams(50f, 0f, 50f, zone: 201), offsetSeconds: 8.5),
            Completed("Navigate", "Arrived", offsetSeconds: 9),
            Decision(null, "handover", offsetSeconds: 9.1),
            Submitted("HandOver", HandOverParams(2000u, 501u), offsetSeconds: 9.6),
            Completed("HandOver", "ok", offsetSeconds: 10)
        );

        // InventoryChanged fires after HandOver Completed, before next Decision
        var inventoryLine = SerializeInventory(InventoryChanged(
            gained:        [],
            lost:          [(501u, 1)],
            newHash:       11u,
            runId:         "aaa",
            offsetSeconds: 10.5));

        var tracePart2 = MakeTrace(
            // --- Step 5: Turn in quest at NPC 3000 ---
            Obs("IsQuestComplete", argument: QuestIdArg(66130u), value: BoolValue(false), offsetSeconds: 11.0),
            Decision(null, "navigate", offsetSeconds: 11.1),
            Submitted("Navigate", NavParams(90f, 0f, 90f, zone: 201), offsetSeconds: 11.6),
            Completed("Navigate", "Arrived", offsetSeconds: 12),
            Decision(null, "interact", offsetSeconds: 12.1),
            Submitted("Interact", InteractParams(3000u), offsetSeconds: 12.6),
            Completed("Interact", "ok", offsetSeconds: 13),
            Obs("IsQuestComplete", argument: QuestIdArg(66130u), value: BoolValue(true), offsetSeconds: 13.5),
            Decision(null, "done", offsetSeconds: 14),
            End("done", offsetSeconds: 20)
        );

        var fullTrace = tracePart1 + "\n" + inventoryLine + "\n" + tracePart2;

        var events = TraceEventParser.ReadText(fullTrace);
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert — extract must succeed
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;

        // Flatten all steps from all sequences in order
        var allSteps = draft.Definition.Sequences
            .OrderBy(s => s.Sequence)
            .SelectMany(s => s.Steps)
            .ToList();

        // Verify all five required step types are present
        Assert.Contains(allSteps, s => s is AcceptStep);
        Assert.Contains(allSteps, s => s is AttunementStep);
        Assert.Contains(allSteps, s => s is TravelStep ts
            && ts.RouteHint?.Aethernet != null
            && ts.RouteHint.Aethernet.To == 77u);
        Assert.Contains(allSteps, s => s is HandOverItemStep ho
            && ho.Items.Contains(501u));
        Assert.Contains(allSteps, s => s is TurnInStep);

        // Verify relative order: Accept < Attune < Travel(aethernet) < HandOver < TurnIn
        int acceptIdx  = allSteps.FindIndex(s => s is AcceptStep);
        int attuneIdx  = allSteps.FindIndex(s => s is AttunementStep);
        int travelIdx  = allSteps.FindIndex(s => s is TravelStep ts
            && ts.RouteHint?.Aethernet != null
            && ts.RouteHint.Aethernet.To == 77u);
        int handOverIdx = allSteps.FindIndex(s => s is HandOverItemStep);
        int turnInIdx  = allSteps.FindIndex(s => s is TurnInStep);

        Assert.True(acceptIdx  >= 0, "AcceptStep must be present.");
        Assert.True(attuneIdx  >= 0, "AttunementStep must be present.");
        Assert.True(travelIdx  >= 0, "TravelStep with aethernet route hint must be present.");
        Assert.True(handOverIdx >= 0, "HandOverItemStep must be present.");
        Assert.True(turnInIdx  >= 0, "TurnInStep must be present.");

        Assert.True(acceptIdx  < attuneIdx,   "AcceptStep must precede AttunementStep.");
        Assert.True(attuneIdx  < travelIdx,   "AttunementStep must precede aethernet TravelStep.");
        Assert.True(travelIdx  < handOverIdx, "Aethernet TravelStep must precede HandOverItemStep.");
        Assert.True(handOverIdx < turnInIdx,  "HandOverItemStep must precede TurnInStep.");
    }
}
