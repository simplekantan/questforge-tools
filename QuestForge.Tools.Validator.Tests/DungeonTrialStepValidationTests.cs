using QuestForge.Schema;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Tests for DungeonTrialStep structural validation rules:
///   - structural/dungeon-trial-cfc-zero when ContentFinderConditionId == 0
///
/// Spec: S3_SV_DT_T1 through S3_SV_DT_T3 from DUTY_STEP_SPLIT_SLICE3_PLAN.md §6.
/// </summary>
public sealed class DungeonTrialStepValidationTests
{
    // =========================================================================
    // S3_SV_DT_T1: Valid DungeonTrialStep -- no errors
    // =========================================================================

    [Fact]
    public void DT_Valid_NoErrors()
    {
        // S3_SV_DT_T1: DungeonTrialStep with valid CFC and Expect predicate produces no errors
        var step = new DungeonTrialStep
        {
            Id                       = "enter-dungeon",
            ContentFinderConditionId = 2,
            Expect                   = new PredicateExpect { Predicate = "questSequence(90001) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoErrors(errors);
    }

    // =========================================================================
    // S3_SV_DT_T2: CFC == 0 -- error structural/dungeon-trial-cfc-zero
    // =========================================================================

    [Fact]
    public void DT_CfcZero_ReportsError()
    {
        // S3_SV_DT_T2: DungeonTrialStep with ContentFinderConditionId == 0 emits structural/dungeon-trial-cfc-zero
        var step = new DungeonTrialStep
        {
            Id                       = "enter-dungeon",
            ContentFinderConditionId = 0
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/dungeon-trial-cfc-zero", stepId: "enter-dungeon");
        Assert.Contains("contentFinderConditionId", errors[0].Message);
        Assert.Contains("0", errors[0].Message);
    }

    // =========================================================================
    // S3_SV_DT_T3: CFC non-zero -- no error
    // =========================================================================

    [Fact]
    public void DT_CfcNonZero_NoError()
    {
        // S3_SV_DT_T3: DungeonTrialStep with ContentFinderConditionId = 167 produces no cfc-zero error
        var step = new DungeonTrialStep
        {
            Id                       = "enter-dungeon",
            ContentFinderConditionId = 167
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/dungeon-trial-cfc-zero");
    }
}
