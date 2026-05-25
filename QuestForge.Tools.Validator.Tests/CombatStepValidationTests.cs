using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// RED PHASE: Tests for three new structural validator rules for combat steps
/// (COMBAT_STEP_PART_A_PLAN.md Task 7.2, Validation rule table).
///
/// Rules under test:
///   - Error   "structural/combat-overworld-needs-kill-ids"  when Spawn==OverworldEnemies AND KillEnemyDataIds is empty
///   - Warning "structural/combat-missing-expect"            when a combat step has no Expect
///   - Error   "structural/combat-kill-id-zero"             when any entry in KillEnemyDataIds is 0
///
/// ALL new-rule tests will FAIL RED until Builder adds the combat validation arm
/// to StructuralValidator. The project compiles as-is because CombatStep and
/// CombatSpawn already exist in QuestForge.Schema; the failures are assertion
/// failures — the codes never appear in errors because no production code emits them.
/// </summary>
public sealed class CombatStepValidationTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Builds a minimal valid CombatStep baseline (auto-on-enter, one kill id, expect set).</summary>
    private static CombatStep ValidCombatStep(string id = "combat-a") => new()
    {
        Id               = id,
        Spawn            = CombatSpawn.AutoOnEnterArea,
        KillEnemyDataIds = [100],
        Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
    };

    private static string FormatErrors(IEnumerable<ValidationError> errors) =>
        string.Join("; ", errors.Select(e => $"[{e.Severity}/{e.Code}] {e.Message}"));

    // =========================================================================
    // structural/combat-overworld-needs-kill-ids
    // =========================================================================

    [Fact]
    public void CV_OverworldEmptyKillIds_ReportsError()
    {
        // CV-overworld-empty-fires:
        // Given a combat step with Spawn=OverworldEnemies and KillEnemyDataIds=[],
        // When validated,
        // Then exactly one structural/combat-overworld-needs-kill-ids Error is produced.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.OverworldEnemies,
            KillEnemyDataIds = [],   // ← the mutation that triggers the rule
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/combat-overworld-needs-kill-ids");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void CV_OverworldWithKillIds_NoError()
    {
        // CV-overworld-with-ids-ok:
        // Given Spawn=OverworldEnemies and KillEnemyDataIds=[100],
        // Then NO combat-overworld-needs-kill-ids error.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.OverworldEnemies,
            KillEnemyDataIds = [100],
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/combat-overworld-needs-kill-ids");
    }

    [Fact]
    public void CV_AutoOnEnterAreaEmptyKillIds_NoOverworldError()
    {
        // CV-auto-empty-ok:
        // Given Spawn=AutoOnEnterArea and KillEnemyDataIds=[] (the "clear-the-room" case),
        // Then NO combat-overworld-needs-kill-ids error.
        // (AutoOnEnterArea with empty ids is legitimate — kill all hostiles in the arena.)
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [],   // empty is OK for AutoOnEnterArea
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/combat-overworld-needs-kill-ids");
    }

    // =========================================================================
    // structural/combat-missing-expect
    // =========================================================================

    [Fact]
    public void CV_MissingExpect_ReportsWarning()
    {
        // CV-missing-expect-warns:
        // Given a combat step with Expect=null,
        // When validated,
        // Then exactly one structural/combat-missing-expect with Severity.Warning is produced.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [100],
            Expect           = null  // ← the mutation: no completion predicate
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/combat-missing-expect");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void CV_HasExpect_NoMissingExpectWarning()
    {
        // CV-has-expect-ok:
        // Given a combat step with a valid Expect predicate,
        // Then NO combat-missing-expect warning.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [100],
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/combat-missing-expect");
    }

    [Fact]
    public void CV_HasQuestFlagExpect_NoMissingExpectWarning()
    {
        // CV-has-expect-ok (questFlag variant):
        // A questFlag predicate also satisfies the expect requirement.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [100],
            Expect           = new PredicateExpect { Predicate = "questFlag(66104, 3)" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/combat-missing-expect");
    }

    // =========================================================================
    // structural/combat-kill-id-zero
    // =========================================================================

    [Fact]
    public void CV_KillIdZeroInList_ReportsError()
    {
        // CV-kill-id-zero-fires:
        // Given KillEnemyDataIds=[100, 0],
        // When validated,
        // Then exactly one structural/combat-kill-id-zero Error is produced.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [100, 0],  // ← the mutation: 0 is invalid
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/combat-kill-id-zero");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void CV_KillIdOnlyZero_ReportsError()
    {
        // CV-kill-id-zero-fires (single zero):
        // Given KillEnemyDataIds=[0] alone,
        // Then exactly one structural/combat-kill-id-zero Error is produced.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [0],       // ← zero is never a valid BNpc data-id
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/combat-kill-id-zero");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void CV_AllNonZeroKillIds_NoKillIdZeroError()
    {
        // CV-kill-id-nonzero-ok:
        // Given KillEnemyDataIds=[100, 200],
        // Then NO combat-kill-id-zero error.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.AutoOnEnterArea,
            KillEnemyDataIds = [100, 200],
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/combat-kill-id-zero");
    }

    // =========================================================================
    // CV-clean-combat-step: a well-formed combat step produces none of the three codes
    // =========================================================================

    [Fact]
    public void CV_WellFormedCombatStep_ProducesNoCombatErrors()
    {
        // CV-clean-combat-step:
        // Given a combat step with Spawn=AutoOnEnterArea, KillEnemyDataIds=[100], valid Expect,
        // Then none of the three combat validation codes appear.
        var step = ValidCombatStep();
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        var combatCodes = new[]
        {
            "structural/combat-overworld-needs-kill-ids",
            "structural/combat-missing-expect",
            "structural/combat-kill-id-zero"
        };
        var combatErrors = errors.Where(e => combatCodes.Contains(e.Code)).ToList();

        Assert.True(combatErrors.Count == 0,
            $"Expected no combat validation errors but got: {FormatErrors(combatErrors)}");
    }

    [Fact]
    public void CV_WellFormedOverworldCombatStep_ProducesNoCombatErrors()
    {
        // CV-clean-combat-step (OverworldEnemies variant):
        // Given Spawn=OverworldEnemies with non-empty, non-zero KillEnemyDataIds and Expect set,
        // Then none of the three combat codes appear.
        var step = new CombatStep
        {
            Id               = "combat-a",
            Spawn            = CombatSpawn.OverworldEnemies,
            KillEnemyDataIds = [100, 200],
            Expect           = new PredicateExpect { Predicate = "questFlag(66104, 3)" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        var combatCodes = new[]
        {
            "structural/combat-overworld-needs-kill-ids",
            "structural/combat-missing-expect",
            "structural/combat-kill-id-zero"
        };
        var combatErrors = errors.Where(e => combatCodes.Contains(e.Code)).ToList();

        Assert.True(combatErrors.Count == 0,
            $"Expected no combat validation errors for well-formed OverworldEnemies step but got: {FormatErrors(combatErrors)}");
    }

    // =========================================================================
    // CV-combat-in-branch: a combat step nested in a BranchStep is also validated
    // =========================================================================

    [Fact]
    public void CV_CombatStepInBranch_OverworldEmptyKillIds_ReportsError()
    {
        // Verifies that combat rule validation recurses into BranchStep cases,
        // matching the pattern of ValidateBranchRules/CheckFragmentRules recursion
        // already present in StructuralValidator.
        //
        // Given a BranchStep whose "default" case contains a combat step with
        // Spawn=OverworldEnemies and KillEnemyDataIds=[],
        // When validated,
        // Then structural/combat-overworld-needs-kill-ids is reported (not silently skipped).
        var combatStep = new CombatStep
        {
            Id               = "combat-nested",
            Spawn            = CombatSpawn.OverworldEnemies,
            KillEnemyDataIds = [],
            Expect           = new PredicateExpect { Predicate = "questVariable(66104, 0) >= 3" }
        };

        var branch = new BranchStep
        {
            Id       = "branch-a",
            Branches = [new BranchCase("default", [combatStep])],
            Expect   = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, branch)])).ToList();

        Assert.True(
            errors.Any(e => e.Code == "structural/combat-overworld-needs-kill-ids"),
            $"Expected structural/combat-overworld-needs-kill-ids for nested combat step but got: {FormatErrors(errors)}");
    }
}
