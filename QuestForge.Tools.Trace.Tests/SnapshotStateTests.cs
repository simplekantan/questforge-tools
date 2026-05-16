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
}
