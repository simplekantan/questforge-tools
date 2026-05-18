using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="SnapshotState"/> — the mutable game-state accumulator.
/// Scenarios 21–25 from PHASE_10_PLAN.md §12.4.
/// All tests are RED: they will fail until Builder implements SnapshotState.Apply / ToSnapshot.
/// </summary>
public sealed class SnapshotStateTests
{
    private static readonly QuestId ActiveQuest = new(66130u);

    // -------------------------------------------------------------------------
    // Scenario 21 — GetPlayerZone updates Zone and returns true
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetPlayerZone_UpdatesZoneField_AndReturnsTrue()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  Apply is called with a GetPlayerZone observation whose value is {"value":182},
         *           Then  state.Zone == ZoneId(182) and the return value is true.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Value as a JSON object; read the "value" uint property.
         *   - Set this.Zone = new ZoneId(parsed value).
         *   - Return true (the method is recognised).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = Obs("GetPlayerZone", argument: null, value: ZoneValue(182u));

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised, "Apply should return true for a recognised method.");
        Assert.Equal(new ZoneId(182u), state.Zone);
    }

    // -------------------------------------------------------------------------
    // Scenario 22 — GetQuestSequence for wrong questId is ignored but returns true
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetQuestSequence_WrongQuestId_DoesNotMutate_ButReturnsTrue()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given SnapshotState(quest=66130), initial QuestSequence == 0,
         *           When  Apply is called with GetQuestSequence whose argument quest ID is 12345,
         *           Then  QuestSequence remains 0 and Apply returns true.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Argument as {"value": <uint>}.
         *   - If the parsed quest ID != _activeQuest.Value, skip mutation but still return true.
         *   - The method is "recognised" regardless of the quest-ID filter.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = Obs("GetQuestSequence", argument: QuestIdArg(12345u), value: IntValue(5));

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised, "Apply should return true even when quest-ID filter prevents mutation.");
        Assert.Equal(0, state.QuestSequence);
    }

    // -------------------------------------------------------------------------
    // Scenario 23 — GetQuestFlags for correct questId updates flags
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetQuestFlags_CorrectQuestId_UpdatesFlags()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given SnapshotState(quest=66130),
         *           When  Apply is called with GetQuestFlags for quest 66130, value=0x0F,
         *           Then  state.QuestFlags == 0x0Fu.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Argument as {"value": <uint>}; compare to _activeQuest.Value.
         *   - Parse ev.Value as a raw uint JSON number.
         *   - Set this.QuestFlags = parsed value.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = Obs("GetQuestFlags", argument: QuestIdArg(66130u), value: UintValue(0x0Fu));

        // Act
        state.Apply(ev);

        // Assert
        Assert.Equal(0x0Fu, state.QuestFlags);
    }

    // -------------------------------------------------------------------------
    // Scenario 24 — Last-value-wins for repeated observations
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_RepeatedGetQuestSequence_LastValueWins()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given SnapshotState(quest=66130),
         *           When  GetQuestSequence observations return 1, 2, 3 (in order),
         *           Then  state.QuestSequence == 3.
         *
         * BUILDER GUIDANCE:
         *   - Each successive Apply call simply overwrites the field.
         *   - No deduplication or comparison — last write wins.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var arg = QuestIdArg(66130u);

        // Act
        state.Apply(Obs("GetQuestSequence", argument: arg, value: IntValue(1)));
        state.Apply(Obs("GetQuestSequence", argument: arg, value: IntValue(2)));
        state.Apply(Obs("GetQuestSequence", argument: arg, value: IntValue(3)));

        // Assert
        Assert.Equal(3, state.QuestSequence);
    }

    // -------------------------------------------------------------------------
    // Scenario 25 — ToSnapshot captures accumulated state at timestamp
    // -------------------------------------------------------------------------

    [Fact]
    public void ToSnapshot_CapturesAllAccumulatedFields_AtGivenTimestamp()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply and ToSnapshot.
         *
         * CONTRACT: Given state after applying GetPlayerZone=182, GetPlayerPosition=(1,2,3),
         *           GetQuestSequence(66130)=4, IsQuestAccepted(66130)=true,
         *           When  ToSnapshot(DateTimeOffset.Parse("2026-05-16T12:00:00Z")),
         *           Then  snapshot has CapturedAt == that timestamp, Zone == ZoneId(182),
         *                 Position == WorldPosition(1,2,3), QuestSequence == 4,
         *                 QuestAccepted == true, ActiveQuest == QuestId(66130).
         *
         * BUILDER GUIDANCE:
         *   - Construct GameStateSnapshot from current field values.
         *   - Use the _activeQuest for ActiveQuest.
         *   - LastDialoguePrompt, LastDialogueAnswer = null; InventoryHash = 0u.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var arg = QuestIdArg(66130u);

        state.Apply(Obs("GetPlayerZone",    argument: null, value: ZoneValue(182u)));
        state.Apply(Obs("GetPlayerPosition", argument: null, value: PositionValue(1f, 2f, 3f)));
        state.Apply(Obs("GetQuestSequence", argument: arg,  value: IntValue(4)));
        state.Apply(Obs("IsQuestAccepted",  argument: arg,  value: BoolValue(true)));

        var captureAt = DateTimeOffset.Parse("2026-05-16T12:00:00Z");

        // Act
        var snapshot = state.ToSnapshot(captureAt);

        // Assert
        Assert.Equal(captureAt,              snapshot.CapturedAt);
        Assert.Equal(new ZoneId(182u),       snapshot.Zone);
        Assert.Equal(new WorldPosition(1f, 2f, 3f), snapshot.Position);
        Assert.Equal(4,                      snapshot.QuestSequence);
        Assert.True(snapshot.QuestAccepted,  "QuestAccepted should be true after IsQuestAccepted=true observation.");
        Assert.Equal(ActiveQuest,            snapshot.ActiveQuest);
    }

    // -------------------------------------------------------------------------
    // Additional: unrecognised method returns false
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_UnrecognisedMethod_ReturnsFalse()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  Apply is called with method="GetNearbyNpcs",
         *           Then  the return value is false.
         *
         * BUILDER GUIDANCE:
         *   - After the switch/if-chain for recognised methods, return false as the fallthrough.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = Obs("GetNearbyNpcs", argument: null, value: null);

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.False(recognised, "Unrecognised method names should return false.");
    }

    // -------------------------------------------------------------------------
    // Additional: failure-shaped value is silently skipped (no mutation)
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_FailureShapedValue_NoMutation_ReturnsTrue()
    {
        /*
         * RED: Will fail until Builder implements SnapshotState.Apply.
         *
         * CONTRACT: Given SnapshotState(quest=66130) with Zone = ZoneId(0),
         *           When  Apply is called with GetPlayerZone and a failure-shaped value,
         *           Then  Zone remains ZoneId(0) and Apply returns true.
         *
         * BUILDER GUIDANCE:
         *   - After recognising the method, check whether ev.Value contains a "failure" property.
         *   - If so, skip mutation and return true.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = Obs("GetPlayerZone", argument: null, value: FailureValue("Timeout"));

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised, "Recognised method should return true even for failure-shaped values.");
        Assert.Equal(new ZoneId(0u), state.Zone);
    }

    // -------------------------------------------------------------------------
    // Regression: QuestArgMatches must not crash when argument is a plain number
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetQuestSequence_PlainNumberArg_MatchesActiveQuest()
    {
        /*
         * REGRESSION: Old authoring traces wrote the quest ID as a plain JSON number
         * (e.g. 66130) instead of an object ({"value": 66130}).
         * QuestArgMatches must not throw InvalidOperationException in that case.
         *
         * CONTRACT: Given SnapshotState(quest=66130),
         *           When  Apply is called with GetQuestSequence whose argument is the
         *                 plain number 66130 (not an object),
         *           Then  QuestSequence is updated and Apply returns true.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        // Build a plain-number argument (not wrapped in {"value": ...})
        var plainNumberArg = System.Text.Json.JsonSerializer.SerializeToElement(66130u);
        var ev = Obs("GetQuestSequence", argument: plainNumberArg, value: IntValue(7));

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised);
        Assert.Equal(7, state.QuestSequence);
    }

    // =========================================================================
    // Tests 5–21: parity improvement tests (RED phase)
    // These reference Apply overloads and state fields not yet implemented.
    // =========================================================================

    // -------------------------------------------------------------------------
    // Test 5 — IsAetheryteAttuned with integer value 1 sets LastAttuned
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_IsAetheryteAttuned_IntegerValue1_SetsLastAttuned()
    {
        /*
         * RED: Will fail until Builder adds "IsAetheryteAttuned" to SnapshotState.Apply
         * and adds LastAttuned tracking to SnapshotState.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  Apply is called with IsAetheryteAttuned(aetheryteId=8, value=1),
         *           Then  state.LastAttuned == AetheryteId(8) and Apply returns true.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Argument as {"value": uint} → aetheryteId.
         *   - Parse ev.Value as int; if value == 1, set LastAttuned = new AetheryteId(aetheryteId).
         *   - Value of 0 must NOT mutate LastAttuned (see Test 7).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1);

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised, "IsAetheryteAttuned must be a recognised method.");
        Assert.NotNull(state.LastAttuned);
        Assert.Equal(new AetheryteId(8u), state.LastAttuned!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 6 — IsAetheryteAttuned with boolean true also sets LastAttuned
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_IsAetheryteAttuned_BooleanTrue_SetsLastAttuned()
    {
        /*
         * RED: Will fail until Builder handles the boolean-true value form.
         *
         * CONTRACT: Engine traces may emit IsAetheryteAttuned with value=true (boolean)
         *           rather than value=1 (integer). Both forms must set LastAttuned.
         *
         *           Given a fresh SnapshotState,
         *           When  Apply is called with IsAetheryteAttuned whose value is JSON boolean true,
         *           Then  state.LastAttuned == AetheryteId(15) and Apply returns true.
         *
         * BUILDER GUIDANCE:
         *   - After reading ev.Value, check both JsonValueKind.Number (int==1) and
         *     JsonValueKind.True to set LastAttuned.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        // Build a boolean-true observation manually (ObsIsAetheryteAttuned uses IntValue).
        var ev = Obs(
            method:        "IsAetheryteAttuned",
            argument:      ItemCountArg(15u),
            value:         BoolValue(true),
            runId:         "aaa",
            offsetSeconds: 1.0);

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised);
        Assert.NotNull(state.LastAttuned);
        Assert.Equal(new AetheryteId(15u), state.LastAttuned!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 7 — IsAetheryteAttuned with value 0 does NOT mutate LastAttuned
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_IsAetheryteAttuned_ValueZero_DoesNotMutateLastAttuned()
    {
        /*
         * RED: Will fail until Builder implements the guard for value==0.
         *
         * CONTRACT: Given a SnapshotState where LastAttuned == AetheryteId(8) (set by a
         *           prior Apply with value=1),
         *           When  Apply is called with IsAetheryteAttuned(aetheryteId=99, value=0),
         *           Then  state.LastAttuned remains AetheryteId(8) (not overwritten).
         *
         * BUILDER GUIDANCE:
         *   - Only update LastAttuned when the parsed value is truthy (int==1 or bool==true).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.Apply(ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1));

        // Act
        var recognised = state.Apply(ObsIsAetheryteAttuned(aetheryteId: 99u, value: 0));

        // Assert
        Assert.True(recognised, "IsAetheryteAttuned is still a recognised method even for value=0.");
        Assert.Equal(new AetheryteId(8u), state.LastAttuned!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 8 — IsAetheryteAttuned with object-wrapped arg {"value": 8}
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_IsAetheryteAttuned_ObjectArgShape_SetsLastAttuned()
    {
        /*
         * RED: Will fail until Builder handles the {"value": uint} argument form.
         *
         * CONTRACT: Same as Test 5 — this test explicitly validates that the argument
         *           is read from the {"value": <uint>} wrapper object (not plain number),
         *           which is the canonical shape emitted by the authoring adapter.
         *
         *           Given a fresh SnapshotState,
         *           When  Apply receives IsAetheryteAttuned with argument={"value":8} and value=1,
         *           Then  LastAttuned == AetheryteId(8).
         *
         * BUILDER GUIDANCE:
         *   - ev.Argument is a JsonElement with ValueKind == Object containing "value" property.
         *   - Use TryGetProperty("value", ...) then TryGetUInt32(...).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        // ObsIsAetheryteAttuned uses ItemCountArg which wraps in {"value": ...} object.
        var ev = ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1);

        // Act
        state.Apply(ev);

        // Assert — argument parsed from object shape
        Assert.Equal(new AetheryteId(8u), state.LastAttuned!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 9 — AethernetShardTargeted sets LastAethernetShardInteracted
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_AethernetShardTargeted_SetsLastAethernetShardInteracted()
    {
        /*
         * RED: Will fail until Builder adds "AethernetShardTargeted" to SnapshotState.Apply
         * and adds LastAethernetShardInteracted tracking.
         *
         * CONTRACT: This observation only appears in authoring/unified traces (not pure
         *           engine-run traces). Given a fresh SnapshotState,
         *           When  Apply is called with AethernetShardTargeted(shardId=42),
         *           Then  state.LastAethernetShardInteracted == AetheryteId(42),
         *                 Apply returns true.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Value as a plain uint (PlainNumber helper).
         *   - Set LastAethernetShardInteracted = new AetheryteId(shardId).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = ObsAethernetShardTargeted(shardId: 42u);

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised);
        Assert.NotNull(state.LastAethernetShardInteracted);
        Assert.Equal(new AetheryteId(42u), state.LastAethernetShardInteracted!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 10 — GetItemCount updates _keyItemCounts
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetItemCount_UpdatesKeyItemCounts()
    {
        /*
         * RED: Will fail until Builder adds "GetItemCount" to SnapshotState.Apply.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  Apply is called with GetItemCount(itemId=500, count=3),
         *           Then  the internal key-item counts map has 500 → 3,
         *                 Apply returns true.
         *
         *           Verification via ToSnapshot: snapshot.KeyItems[500] == 3.
         *
         * BUILDER GUIDANCE:
         *   - Parse ev.Argument as {"value": uint} → itemId.
         *   - Parse ev.Value as {"value": int} → count.
         *   - Store in a Dictionary<uint, int> _keyItemCounts.
         *   - ToSnapshot must include _keyItemCounts as KeyItems.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = ObsGetItemCount(itemId: 500u, count: 3);

        // Act
        var recognised = state.Apply(ev);
        var snapshot = state.ToSnapshot(T0.AddSeconds(5));

        // Assert
        Assert.True(recognised, "GetItemCount must be a recognised method.");
        Assert.NotNull(snapshot.KeyItems);
        Assert.True(snapshot.KeyItems!.TryGetValue(500u, out var qty));
        Assert.Equal(3, qty);
    }

    // -------------------------------------------------------------------------
    // Test 11 — GetItemCount failure value does NOT mutate key-item counts
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_GetItemCount_FailureValue_DoesNotMutate()
    {
        /*
         * RED: Will fail until Builder guards against failure-shaped values.
         *
         * CONTRACT: Given a GetItemCount observation whose ev.Value has {"failure": "NotFound"},
         *           When  Apply,
         *           Then  _keyItemCounts is NOT modified and Apply returns true
         *                 (failure guard fires before method-specific parsing).
         *
         * BUILDER GUIDANCE:
         *   - The base failure-shape guard (ev.Value has "failure" key → return true)
         *     fires before any method-specific parsing — this already handles it.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        // First add a real entry.
        state.Apply(ObsGetItemCount(itemId: 700u, count: 2));

        // Now apply a failure observation for the same item.
        var failureEv = Obs(
            method:        "GetItemCount",
            argument:      ItemCountArg(700u),
            value:         FailureValue("NotFound"),
            runId:         "aaa",
            offsetSeconds: 2.0);

        // Act
        var recognised = state.Apply(failureEv);
        var snapshot = state.ToSnapshot(T0.AddSeconds(5));

        // Assert — count must remain 2, not 0 or removed.
        Assert.True(recognised);
        Assert.NotNull(snapshot.KeyItems);
        Assert.Equal(2, snapshot.KeyItems![700u]);
    }

    // -------------------------------------------------------------------------
    // Test 12 — InventoryChanged (Gained) updates counts and pending deltas
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InventoryChanged_Gained_UpdatesCountsAndPending()
    {
        /*
         * RED: Will fail until Builder adds InventoryChangedEvent overload to
         * SnapshotState.Apply (or a separate ApplyInventoryChanged method).
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  ApplyInventoryChanged is called with Gained=[{ItemId:101,Qty:1}],
         *                 Lost=[], NewHash=10u,
         *           Then  snapshot.KeyItems[101] == 1,
         *                 snapshot.KeyItemsAdded contains 101u,
         *                 snapshot.KeyItemsRemoved is empty or null.
         *
         * BUILDER GUIDANCE:
         *   - Add a method: public void ApplyInventoryChanged(InventoryChangedEvent ev)
         *     (or overload Apply to accept InventoryChangedEvent).
         *   - Accumulate counts: _keyItemCounts[delta.ItemId] += delta.Qty.
         *   - Track pending added/removed for the next ToSnapshot call.
         *   - After ToSnapshot, ResetPendingKeyItemDeltas clears pending but retains counts.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var ev = InventoryChanged(
            gained:        [(101u, 1)],
            lost:          [],
            newHash:       10u);

        // Act
        state.ApplyInventoryChanged(ev);
        var snapshot = state.ToSnapshot(T0.AddSeconds(3));

        // Assert
        Assert.NotNull(snapshot.KeyItems);
        Assert.Equal(1, snapshot.KeyItems![101u]);

        Assert.NotNull(snapshot.KeyItemsAdded);
        Assert.Contains(101u, snapshot.KeyItemsAdded!);

        // Lost list should be null or empty
        var removed = snapshot.KeyItemsRemoved;
        Assert.True(removed == null || removed.Count == 0,
            "No items were removed; KeyItemsRemoved should be null or empty.");
    }

    // -------------------------------------------------------------------------
    // Test 13 — InventoryChanged (Lost) decrements count
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InventoryChanged_Lost_DecrementsCount()
    {
        /*
         * RED: Will fail until Builder implements ApplyInventoryChanged.
         *
         * CONTRACT: Given state with _keyItemCounts[202] == 3 (from a prior gained event),
         *           When  ApplyInventoryChanged is called with Lost=[{ItemId:202,Qty:1}],
         *           Then  snapshot.KeyItems[202] == 2.
         *
         * BUILDER GUIDANCE:
         *   - _keyItemCounts[delta.ItemId] -= delta.Qty for each Lost entry.
         *   - Do NOT remove the key when count > 0.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.ApplyInventoryChanged(InventoryChanged(gained: [(202u, 3)], newHash: 1u));

        // Act
        state.ApplyInventoryChanged(InventoryChanged(
            lost:    [(202u, 1)],
            newHash: 2u,
            offsetSeconds: 3.0));
        var snapshot = state.ToSnapshot(T0.AddSeconds(4));

        // Assert
        Assert.NotNull(snapshot.KeyItems);
        Assert.Equal(2, snapshot.KeyItems![202u]);
    }

    // -------------------------------------------------------------------------
    // Test 14 — InventoryChanged Lost-to-zero removes entry and flags removed
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InventoryChanged_LostToZero_RemovesEntryButFlagsRemoved()
    {
        /*
         * RED: Will fail until Builder implements ApplyInventoryChanged.
         *
         * CONTRACT: Given state with _keyItemCounts[303] == 1,
         *           When  ApplyInventoryChanged with Lost=[{ItemId:303,Qty:1}],
         *           Then  snapshot.KeyItems does not contain 303
         *                 AND snapshot.KeyItemsRemoved contains 303u.
         *
         * BUILDER GUIDANCE:
         *   - When count reaches 0 or below, remove the key from _keyItemCounts.
         *   - Add the item ID to the pending-removed list regardless of removal.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.ApplyInventoryChanged(InventoryChanged(gained: [(303u, 1)], newHash: 1u));

        // Reset pending so the subsequent snapshot only reflects the loss.
        state.ResetPendingKeyItemDeltas();

        // Act
        state.ApplyInventoryChanged(InventoryChanged(
            lost:    [(303u, 1)],
            newHash: 2u,
            offsetSeconds: 3.0));
        var snapshot = state.ToSnapshot(T0.AddSeconds(4));

        // Assert
        // Key 303 removed from KeyItems dict
        var hasEntry = snapshot.KeyItems?.ContainsKey(303u) ?? false;
        Assert.False(hasEntry, "Item 303 count reached 0; must be removed from KeyItems.");

        // But it should appear in KeyItemsRemoved
        Assert.NotNull(snapshot.KeyItemsRemoved);
        Assert.Contains(303u, snapshot.KeyItemsRemoved!);
    }

    // -------------------------------------------------------------------------
    // Test 15 — InventoryChanged with both gained and lost updates both lists
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_InventoryChanged_GainedAndLost_UpdatesBothLists()
    {
        /*
         * RED: Will fail until Builder implements ApplyInventoryChanged.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  ApplyInventoryChanged is called with
         *                 Gained=[{ItemId:401,Qty:1}], Lost=[{ItemId:402,Qty:1}]
         *                 (assume 402 was already present with qty=1),
         *           Then  snapshot.KeyItemsAdded contains 401u
         *                 AND snapshot.KeyItemsRemoved contains 402u.
         *
         * BUILDER GUIDANCE:
         *   - Both gained and lost lists update their respective pending delta lists.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        // Pre-populate 402.
        state.ApplyInventoryChanged(InventoryChanged(gained: [(402u, 1)], newHash: 1u));
        state.ResetPendingKeyItemDeltas();

        // Act — exchange: lose 402, gain 401.
        state.ApplyInventoryChanged(InventoryChanged(
            gained:        [(401u, 1)],
            lost:          [(402u, 1)],
            newHash:       2u,
            offsetSeconds: 3.0));
        var snapshot = state.ToSnapshot(T0.AddSeconds(4));

        // Assert
        Assert.NotNull(snapshot.KeyItemsAdded);
        Assert.Contains(401u, snapshot.KeyItemsAdded!);

        Assert.NotNull(snapshot.KeyItemsRemoved);
        Assert.Contains(402u, snapshot.KeyItemsRemoved!);
    }

    // -------------------------------------------------------------------------
    // Test 16 — ToSnapshot includes LastAttuned and LastAethernetShardInteracted
    // -------------------------------------------------------------------------

    [Fact]
    public void ToSnapshot_IncludesLastAttuned_AndLastAethernetShard()
    {
        /*
         * RED: Will fail until Builder adds LastAttuned and LastAethernetShardInteracted
         * tracking and includes them in ToSnapshot output.
         *
         * CONTRACT: Given state after:
         *             Apply(IsAetheryteAttuned(8, 1))
         *             Apply(AethernetShardTargeted(42))
         *           When  ToSnapshot,
         *           Then  snapshot.LastAttuned == AetheryteId(8)
         *                 AND snapshot.LastAethernetShardInteracted == AetheryteId(42).
         *
         * BUILDER GUIDANCE:
         *   - ToSnapshot must pass both tracked values into the GameStateSnapshot constructor.
         *   - Use the non-positional init properties LastAttuned and LastAethernetShardInteracted.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.Apply(ObsIsAetheryteAttuned(aetheryteId: 8u, value: 1));
        state.Apply(ObsAethernetShardTargeted(shardId: 42u));

        // Act
        var snapshot = state.ToSnapshot(T0.AddSeconds(5));

        // Assert
        Assert.NotNull(snapshot.LastAttuned);
        Assert.Equal(new QuestForge.Adapters.Types.AetheryteId(8u), snapshot.LastAttuned!.Value);

        Assert.NotNull(snapshot.LastAethernetShardInteracted);
        Assert.Equal(new QuestForge.Adapters.Types.AetheryteId(42u), snapshot.LastAethernetShardInteracted!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 17 — ToSnapshot returns non-null empty KeyItems when no items tracked
    // -------------------------------------------------------------------------

    [Fact]
    public void ToSnapshot_KeyItemsIsNonNullEmptyDict_WhenNoItemsTracked()
    {
        /*
         * RED: Will fail until Builder initializes _keyItemCounts and always
         * passes it (non-null) into GameStateSnapshot.KeyItems.
         *
         * CONTRACT: Given a fresh SnapshotState with no GetItemCount or
         *           ApplyInventoryChanged calls,
         *           When  ToSnapshot,
         *           Then  snapshot.KeyItems is NOT null AND snapshot.KeyItems.Count == 0.
         *
         * WHY: StepInferenceEngine.Rule2.6 guards on `before.KeyItems is not null`.
         *      A null KeyItems means Rule 2.6 never fires for replay-extracted traces.
         *      An empty dict (not null) is the correct sentinel for "authoring mode active,
         *      no items seen yet".
         *
         * BUILDER GUIDANCE:
         *   - Initialize _keyItemCounts = new Dictionary<uint, int>() in constructor.
         *   - In ToSnapshot, always include the dict even when empty:
         *       KeyItems = _keyItemCounts  (or .AsReadOnly()).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);

        // Act
        var snapshot = state.ToSnapshot(T0);

        // Assert
        Assert.NotNull(snapshot.KeyItems);
        Assert.Empty(snapshot.KeyItems!);
    }

    // -------------------------------------------------------------------------
    // Test 18 — ToSnapshot KeyItems populated from pending after InventoryChanged
    // -------------------------------------------------------------------------

    [Fact]
    public void ToSnapshot_KeyItemsAdded_PopulatedFromPending()
    {
        /*
         * RED: Will fail until Builder implements ApplyInventoryChanged.
         *
         * CONTRACT: Given state after ApplyInventoryChanged(gained=[(501, 2)]),
         *           When  ToSnapshot,
         *           Then  snapshot.KeyItemsAdded contains 501u
         *                 AND snapshot.KeyItems[501] == 2.
         *
         * BUILDER GUIDANCE:
         *   - _pendingAdded accumulates item IDs between ResetPendingKeyItemDeltas calls.
         *   - ToSnapshot copies _pendingAdded → KeyItemsAdded.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.ApplyInventoryChanged(InventoryChanged(gained: [(501u, 2)], newHash: 5u));

        // Act
        var snapshot = state.ToSnapshot(T0.AddSeconds(2));

        // Assert
        Assert.NotNull(snapshot.KeyItems);
        Assert.Equal(2, snapshot.KeyItems![501u]);

        Assert.NotNull(snapshot.KeyItemsAdded);
        Assert.Contains(501u, snapshot.KeyItemsAdded!);
    }

    // -------------------------------------------------------------------------
    // Test 19 — ResetPendingKeyItemDeltas clears deltas but retains counts
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetPendingKeyItemDeltas_ClearsDeltasButRetainsKeyItemCounts()
    {
        /*
         * RED: Will fail until Builder implements ResetPendingKeyItemDeltas.
         *
         * CONTRACT: Given state after ApplyInventoryChanged(gained=[(600, 1)]),
         *           When  ResetPendingKeyItemDeltas is called,
         *           Then  snapshot after reset has:
         *                 KeyItems[600] == 1 (count retained),
         *                 KeyItemsAdded is null or empty (pending cleared),
         *                 KeyItemsRemoved is null or empty (pending cleared).
         *
         * BUILDER GUIDANCE:
         *   - ResetPendingKeyItemDeltas clears _pendingAdded and _pendingRemoved.
         *   - _keyItemCounts is NOT cleared.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.ApplyInventoryChanged(InventoryChanged(gained: [(600u, 1)], newHash: 6u));

        // Act
        state.ResetPendingKeyItemDeltas();
        var snapshot = state.ToSnapshot(T0.AddSeconds(3));

        // Assert — counts remain
        Assert.NotNull(snapshot.KeyItems);
        Assert.Equal(1, snapshot.KeyItems![600u]);

        // Pending deltas cleared
        var added   = snapshot.KeyItemsAdded;
        var removed = snapshot.KeyItemsRemoved;
        Assert.True(added   == null || added.Count   == 0, "KeyItemsAdded must be cleared after reset.");
        Assert.True(removed == null || removed.Count == 0, "KeyItemsRemoved must be cleared after reset.");
    }

    // -------------------------------------------------------------------------
    // Test 20 — RecordNavigateDestination sets LastNpcPosition
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordNavigateDestination_SetsLastNpcPosition()
    {
        /*
         * RED: Will fail until Builder adds RecordNavigateDestination to SnapshotState.
         *
         * CONTRACT: Given a fresh SnapshotState,
         *           When  RecordNavigateDestination(new WorldPosition(1f, 2f, 3f)) is called,
         *           Then  state.LastNpcPosition == WorldPosition(1, 2, 3).
         *
         * BUILDER GUIDANCE:
         *   - Add: public void RecordNavigateDestination(WorldPosition destination)
         *         { LastNpcPosition = destination; }
         *   - This allows TraceToQuestExtractor to set NPC position from navigate parameters
         *     before the subsequent interact step.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);

        // Act
        state.RecordNavigateDestination(new WorldPosition(1f, 2f, 3f));

        // Assert
        Assert.NotNull(state.LastNpcPosition);
        Assert.Equal(new WorldPosition(1f, 2f, 3f), state.LastNpcPosition!.Value);
    }

    // -------------------------------------------------------------------------
    // Test 21 — RecordInteract does NOT clear LastNpcPosition
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordInteract_DoesNotClearLastNpcPosition()
    {
        /*
         * RED: Will fail until Builder verifies RecordInteract does not wipe LastNpcPosition.
         *
         * CONTRACT: Given state with LastNpcPosition set via RecordNavigateDestination,
         *           When  RecordInteract is called for an NPC,
         *           Then  LastNpcPosition remains unchanged.
         *
         * WHY: The NPC position set by the preceding Navigate is the source of truth
         *      for the TalkStep/AcceptStep/TurnInStep location. RecordInteract must not
         *      clear it — the extractor reads it during step construction.
         *
         * BUILDER GUIDANCE:
         *   - The existing RecordInteract implementation already leaves LastNpcPosition
         *     alone. This test guards against accidental regression.
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        state.RecordNavigateDestination(new WorldPosition(10f, 0f, 20f));

        // Act
        state.RecordInteract(new NpcId(1014875u));

        // Assert
        Assert.NotNull(state.LastNpcPosition);
        Assert.Equal(new WorldPosition(10f, 0f, 20f), state.LastNpcPosition!.Value);
        Assert.Equal(new NpcId(1014875u), state.LastNpcInteracted!.Value);
    }

    // =========================================================================
    // Regression tests (existing, unchanged below)
    // =========================================================================

    [Fact]
    public void Apply_GetQuestSequence_PlainNumberArg_WrongId_DoesNotMutate()
    {
        /*
         * REGRESSION: Plain-number argument whose value does not match the active quest
         * must be silently ignored (not crash, not mutate).
         */

        // Arrange
        var state = new SnapshotState(ActiveQuest);
        var plainNumberArg = System.Text.Json.JsonSerializer.SerializeToElement(99999u);
        var ev = Obs("GetQuestSequence", argument: plainNumberArg, value: IntValue(42));

        // Act
        var recognised = state.Apply(ev);

        // Assert
        Assert.True(recognised, "Method is still recognised even when quest-ID filter prevents mutation.");
        Assert.Equal(0, state.QuestSequence);
    }
}
