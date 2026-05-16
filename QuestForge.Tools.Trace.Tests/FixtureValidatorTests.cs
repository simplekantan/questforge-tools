using QuestForge.Tools.Trace.Fixture;
using Xunit;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="FixtureValidator"/>.
/// Scenarios 8–13 from PHASE_10_PLAN.md §12.2.
/// All tests are RED: they will fail until Builder implements FixtureValidator.Validate.
/// </summary>
public sealed class FixtureValidatorTests
{
    /// <summary>
    /// Absolute path to the Fixtures directory shipped alongside the test assembly.
    /// All quest JSON and fixture JSON test data lives here.
    /// </summary>
    private static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // Reusable minimal valid fixture referencing quest 66130 in the test data tree.
    private static FixtureModel ValidFixture => new(
        SchemaVersion: "1.0.0",
        Description: "Test fixture",
        InitialState: "fresh",
        Capabilities:
        [
            "predicate:isQuestComplete",
            "predicate:playerNear",
            "predicate:questSequence",
            "step:talk",
            "step:travel"
        ],
        QuestFile: "quests/arr/msq/66130-coming-to-uldah.json",
        ExpectedTransitions:
        [
            new TransitionEntry("travel-to-wymond", "navigate"),
            new TransitionEntry("talk-to-wymond",   "interact"),
            new TransitionEntry("travel-to-momodi", "navigate"),
            new TransitionEntry("turn-in-to-momodi","interact")
        ],
        TerminalOutcome: "done");

    // -------------------------------------------------------------------------
    // Scenario 8 — Valid fixture → IsClean
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ValidFixture_IsClean()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given a fixture whose questFile exists, all step IDs are valid,
         *           capabilities exactly match the quest, and terminalOutcome is "done",
         *           When  Validate(fixture),
         *           Then  result.IsClean == true (zero errors, zero warnings).
         *
         * BUILDER GUIDANCE:
         *   - Run all rules in §7.3 and §7.4.
         *   - A clean result requires no errors and no warnings.
         *   - The capability set ["step:travel","step:talk","predicate:playerNear",
         *     "predicate:questSequence","predicate:isQuestComplete"] should match
         *     exactly what CapabilityInferrer.Infer returns for quest 66130.
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);

        // Act
        var result = validator.Validate(ValidFixture);

        // Assert
        Assert.True(result.IsClean,
            $"Expected clean validation. Errors: [{string.Join(", ", result.Errors.Select(e => e.Code))}] " +
            $"Warnings: [{string.Join(", ", result.Warnings.Select(w => w.Code))}]");
    }

    // -------------------------------------------------------------------------
    // Scenario 9 — Missing questFile → fixture/quest-file-missing error
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_MissingQuestFile_ProducesQuestFileMissingError()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given a fixture referencing "quests/missing.json" which does not exist,
         *           When  Validate,
         *           Then  errors include an issue with Code == "fixture/quest-file-missing".
         *
         * BUILDER GUIDANCE:
         *   - Check Path.Combine(questDataRoot, fixture.QuestFile) exists.
         *   - If not, add a Failure with Code = "fixture/quest-file-missing".
         *   - When the quest file is missing, no further structural rules can be checked.
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var fixture = ValidFixture with { QuestFile = "quests/missing.json" };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.True(result.HasErrors, "Should have at least one error.");
        Assert.Contains(result.Errors, e => e.Code == "fixture/quest-file-missing");
    }

    // -------------------------------------------------------------------------
    // Scenario 10 — Unknown step ID → fixture/step-id-unknown error per offending transition
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_UnknownStepId_ProducesStepIdUnknownError()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given a fixture with transition ("does-not-exist","navigate"),
         *           When  Validate against quest 66130 (step IDs: travel-to-wymond, talk-to-wymond,
         *                 travel-to-momodi, turn-in-to-momodi),
         *           Then  errors include fixture/step-id-unknown with StepId == "does-not-exist".
         *
         * BUILDER GUIDANCE:
         *   - Collect all step IDs from all sequences in the loaded QuestDefinition.
         *   - For each non-null transition.StepId, check it is in that set.
         *   - Emit one issue per offending transition.
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var badTransitions = new[]
        {
            new TransitionEntry("does-not-exist", "navigate"),
            new TransitionEntry("travel-to-wymond", "navigate")  // valid — only one error expected
        };
        var fixture = ValidFixture with { ExpectedTransitions = badTransitions };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.True(result.HasErrors);
        var stepErr = Assert.Single(result.Errors, e => e.Code == "fixture/step-id-unknown");
        Assert.Equal("does-not-exist", stepErr.StepId);
    }

    // -------------------------------------------------------------------------
    // Scenario 11 — Unknown terminal outcome → fixture/terminal-outcome-unknown error
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_UnknownTerminalOutcome_ProducesTerminalOutcomeUnknownError()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given fixture.TerminalOutcome == "exploded",
         *           When  Validate,
         *           Then  errors include fixture/terminal-outcome-unknown.
         *
         * BUILDER GUIDANCE:
         *   - Allowlist is {"done", "awaitUser"}.
         *   - null is allowed (means no RunEnd was seen); "exploded" is not.
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var fixture = ValidFixture with { TerminalOutcome = "exploded" };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Code == "fixture/terminal-outcome-unknown");
    }

    // -------------------------------------------------------------------------
    // Scenario 12 — Missing capability → fixture/capability-missing warning
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_MissingCapability_ProducesCapabilityMissingWarning()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate
         *      and CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a quest that uses step:cutscene but fixture.Capabilities omits it,
         *           When  Validate,
         *           Then  warnings include fixture/capability-missing for "step:cutscene".
         *
         * IMPLEMENTATION NOTE: Quest 66130 does not have a CutsceneStep.
         *   We fabricate this scenario by providing a fixture that claims FEWER capabilities
         *   than what the quest actually uses. We use a modified quest that would include a
         *   cutscene, or alternatively test via a fixture that omits a capability the real
         *   quest 66130 demonstrably uses (e.g. step:travel).
         *
         * BUILDER GUIDANCE:
         *   - Call CapabilityInferrer.Infer on the loaded quest.
         *   - Compute: (inferredCaps) minus (fixtureCaps) → these are missing.
         *   - Emit fixture/capability-missing warning for each.
         */

        // Arrange — remove "step:travel" from the fixture's capabilities list so it is "missing"
        var validator = new FixtureValidator(FixturesDir);
        var capsWithoutTravel = ValidFixture.Capabilities
            .Where(c => c != "step:travel")
            .ToList();
        var fixture = ValidFixture with { Capabilities = capsWithoutTravel };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.Contains(result.Warnings,
            w => w.Code == "fixture/capability-missing" && w.Message.Contains("step:travel"));
    }

    // -------------------------------------------------------------------------
    // Scenario 13 — Extra capability → fixture/capability-extra warning
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ExtraCapability_ProducesCapabilityExtraWarning()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate
         *      and CapabilityInferrer.Infer.
         *
         * CONTRACT: Given fixture lists "step:combat" but quest 66130 has no CombatStep,
         *           When  Validate,
         *           Then  warnings include fixture/capability-extra for "step:combat".
         *
         * BUILDER GUIDANCE:
         *   - Compute: (fixtureCaps) minus (inferredCaps) → these are extra.
         *   - Emit fixture/capability-extra warning for each.
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var capsWithExtra = ValidFixture.Capabilities.Append("step:combat").ToList();
        var fixture = ValidFixture with { Capabilities = capsWithExtra };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.Contains(result.Warnings,
            w => w.Code == "fixture/capability-extra" && w.Message.Contains("step:combat"));
    }

    // -------------------------------------------------------------------------
    // Additional: unsupported schema version → fixture/schema-version-unsupported error
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_UnsupportedSchemaVersion_ProducesSchemaVersionUnsupportedError()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given fixture.SchemaVersion == "99.0.0",
         *           When  Validate,
         *           Then  errors include fixture/schema-version-unsupported.
         *
         * BUILDER GUIDANCE:
         *   - Supported versions: {"1.0.0"}.
         *   - Check before any other rule (early-exit pattern or separate first rule).
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var fixture = ValidFixture with { SchemaVersion = "99.0.0" };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Code == "fixture/schema-version-unsupported");
    }

    // -------------------------------------------------------------------------
    // Additional: unknown initialState → fixture/initial-state-unknown warning
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_UnknownInitialState_ProducesInitialStateUnknownWarning()
    {
        /*
         * RED: Will fail until Builder implements FixtureValidator.Validate.
         *
         * CONTRACT: Given fixture.InitialState == "mid-quest",
         *           When  Validate,
         *           Then  warnings include fixture/initial-state-unknown.
         *
         * BUILDER GUIDANCE:
         *   - Recognised initial states: {"fresh"}.
         *   - "mid-quest" is not in the allowlist → warning (not error).
         */

        // Arrange
        var validator = new FixtureValidator(FixturesDir);
        var fixture = ValidFixture with { InitialState = "mid-quest" };

        // Act
        var result = validator.Validate(fixture);

        // Assert
        Assert.Contains(result.Warnings, w => w.Code == "fixture/initial-state-unknown");
    }
}
