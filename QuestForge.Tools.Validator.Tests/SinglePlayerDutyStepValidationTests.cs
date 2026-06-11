using QuestForge.Schema;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Tests for SinglePlayerDutyStep structural validation rules:
///   - structural/spd-cfc-zero             when ContentFinderConditionId == 0
///   - structural/spd-entry-target-null    when Talk/Interact entry kind has null EntryTargetId
///   - structural/spd-entry-target-zero    when Talk/Interact entry kind has EntryTargetId == 0
///   - structural/spd-proximity-has-target when Proximity entry kind has non-null EntryTargetId
///
/// Plus legacy coexistence test (S3_SV_LEGACY_T1).
///
/// Spec: S3_SV_SPD_T1 through S3_SV_SPD_T10 + S3_SV_LEGACY_T1 from DUTY_STEP_SPLIT_SLICE3_PLAN.md §6.
/// </summary>
public sealed class SinglePlayerDutyStepValidationTests
{
    // =========================================================================
    // S3_SV_SPD_T1: Valid SinglePlayerDutyStep (talk entry) -- no errors
    // =========================================================================

    [Fact]
    public void SPD_ValidTalkEntry_NoErrors()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 830,
            EntryKind                = SpdEntryKind.Talk,
            EntryTargetId            = 1045123u,
            EntryPosition            = new Position3(10f, 0f, 10f),
            Expect                   = new PredicateExpect { Predicate = "questSequence(91001) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoErrors(errors);
    }

    // =========================================================================
    // S3_SV_SPD_T2: Valid SinglePlayerDutyStep (proximity, null target) -- no errors
    // =========================================================================

    [Fact]
    public void SPD_ValidProximityNullTarget_NoErrors()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd-proximity",
            ContentFinderConditionId = 832,
            EntryKind                = SpdEntryKind.Proximity,
            EntryTargetId            = null,
            EntryPosition            = new Position3(10f, 0f, 10f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        var spdErrors = errors.Where(e => e.Code.StartsWith("structural/spd-")).ToList();
        Assert.Empty(spdErrors);
    }

    // =========================================================================
    // S3_SV_SPD_T3: CFC == 0 -- error structural/spd-cfc-zero
    // =========================================================================

    [Fact]
    public void SPD_CfcZero_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 0,
            EntryKind                = SpdEntryKind.Talk,
            EntryTargetId            = 5000u,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-cfc-zero");
    }

    // =========================================================================
    // S3_SV_SPD_T4: Talk entry, EntryTargetId null -- error structural/spd-entry-target-null
    // =========================================================================

    [Fact]
    public void SPD_TalkEntryTargetNull_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 830,
            EntryKind                = SpdEntryKind.Talk,
            EntryTargetId            = null,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        Assert.Contains(errors, e => e.Code == "structural/spd-entry-target-null");
        var err = errors.First(e => e.Code == "structural/spd-entry-target-null");
        Assert.Contains("Talk", err.Message);
        Assert.Contains("null", err.Message);
    }

    // =========================================================================
    // S3_SV_SPD_T5: Interact entry, EntryTargetId null -- error structural/spd-entry-target-null
    // =========================================================================

    [Fact]
    public void SPD_InteractEntryTargetNull_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 831,
            EntryKind                = SpdEntryKind.Interact,
            EntryTargetId            = null,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-entry-target-null");
        var err = errors.First(e => e.Code == "structural/spd-entry-target-null");
        Assert.Contains("Interact", err.Message);
    }

    // =========================================================================
    // S3_SV_SPD_T6: Talk entry, EntryTargetId == 0 -- error structural/spd-entry-target-zero
    // =========================================================================

    [Fact]
    public void SPD_TalkEntryTargetZero_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 830,
            EntryKind                = SpdEntryKind.Talk,
            EntryTargetId            = 0u,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-entry-target-zero");
        var err = errors.First(e => e.Code == "structural/spd-entry-target-zero");
        Assert.Contains("0", err.Message);
    }

    // =========================================================================
    // S3_SV_SPD_T7: Interact entry, EntryTargetId == 0 -- error structural/spd-entry-target-zero
    // =========================================================================

    [Fact]
    public void SPD_InteractEntryTargetZero_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 831,
            EntryKind                = SpdEntryKind.Interact,
            EntryTargetId            = 0u,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-entry-target-zero");
    }

    // =========================================================================
    // S3_SV_SPD_T8: Proximity entry with non-null EntryTargetId -- error structural/spd-proximity-has-target
    // =========================================================================

    [Fact]
    public void SPD_ProximityWithTarget_ReportsError()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 832,
            EntryKind                = SpdEntryKind.Proximity,
            EntryTargetId            = 5000u,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-proximity-has-target");
        var err = errors.First(e => e.Code == "structural/spd-proximity-has-target");
        Assert.Contains("proximity", err.Message);
        Assert.Contains("entryTargetId", err.Message);
    }

    // =========================================================================
    // S3_SV_SPD_T9: Multiple errors fire simultaneously -- CFC zero + entry target null
    // =========================================================================

    [Fact]
    public void SPD_CfcZeroAndEntryTargetNull_BothErrorsFire()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 0,
            EntryKind                = SpdEntryKind.Talk,
            EntryTargetId            = null,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        Assert.Contains(errors, e => e.Code == "structural/spd-cfc-zero");
        Assert.Contains(errors, e => e.Code == "structural/spd-entry-target-null");
    }

    // =========================================================================
    // S3_SV_SPD_T10: Proximity entry with null target and valid CFC -- no errors
    // =========================================================================

    [Fact]
    public void SPD_ProximityNullTargetValidCfc_NoSpdErrors()
    {
        var step = new SinglePlayerDutyStep
        {
            Id                       = "enter-spd",
            ContentFinderConditionId = 833,
            EntryKind                = SpdEntryKind.Proximity,
            EntryTargetId            = null,
            EntryPosition            = new Position3(0f, 0f, 0f)
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        var spdErrors = errors.Where(e => e.Code.StartsWith("structural/spd-")).ToList();
        Assert.Empty(spdErrors);
    }

    // =========================================================================
    // S3_SV_LEGACY_T1: Old DutyStep with kind "duty" and null CFC still fires structural/duty-missing-required-field
    // =========================================================================

    [Fact]
    public void Legacy_DutyStepKindDutyNullCfc_StillReportsDutyMissingRequiredField()
    {
        // S3_SV_LEGACY_T1: confirms ValidateDutyStep is not broken by new step-type arms
        var step = new DutyStep
        {
            Id                       = "enter-duty-legacy",
            Kind                     = "duty",
            ContentFinderConditionId = null
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/duty-missing-required-field", stepId: "enter-duty-legacy");
    }
}
