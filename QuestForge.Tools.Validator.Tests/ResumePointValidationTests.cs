using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// RED PHASE: Validator tests for the three new resume-point rules.
/// Issue #39 — group C acceptance criteria (C1–C11).
///
/// Rules:
///   R1: structural/resume-point-fragment-id-empty (Error)
///       Fires when ResumePointFragmentId is present but empty/whitespace.
///       Scope: quest files + fragment files; all step types; recurses into branches.
///   R2: structural/resume-point-nested (Error)
///       Fires when a step inside a FragmentDefinition.Steps sets ResumePointFragmentId.
///       Scope: fragment files only (depth-1 only, no nested resumes).
///   R3: structural/fragment-step-id-convention (Warning)
///       Fires when a step inside a FragmentDefinition.Steps has an id not starting with "fragment-".
///       Scope: fragment files only. Suppressed when R2 fired on the same step.
///
/// ALL tests will fail to compile until Builder adds:
///   public string? ResumePointFragmentId { get; init; }
/// to Step base class in QuestForge.Schema\Step.cs (tools-side copy), AND:
///   public RecoverAction? OnResumeFail { get; init; }
/// to RecoverConfig in QuestForge.Schema\RecoverAction.cs (tools-side copy).
///
/// C6–C11 tests reference StructuralValidator.ValidateFragmentDefinition, which does NOT exist yet.
///   // RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
/// The Builder must add a fragment-file validation entry point to StructuralValidator (§4.5).
/// </summary>
public sealed class ResumePointValidationTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static FragmentDefinition MakeFragment(string fragmentId, params Step[] steps) => new()
    {
        SchemaVersion = "1.0.0",
        FragmentId = fragmentId,
        Parameters = [],
        Steps = steps
    };

    private static TravelStep FragStep(string id, string? resumePointFragmentId = null) => new()
    {
        Id = id,
        Destination = new TravelDestination(128),
        ResumePointFragmentId = resumePointFragmentId  // RED: property does not exist yet
    };

    private static ValidationContext FragmentCtx(string fragmentId = "fragment-go-128") =>
        new($"fragments/{fragmentId}.json", 0);

    // Helper to call the yet-to-be-created ValidateFragmentDefinition method.
    // RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
    private static IEnumerable<ValidationError> ValidateFragment(FragmentDefinition fragment)
    {
        // RED: StructuralValidator.ValidateFragmentDefinition does not exist yet.
        // The Builder must add a method with the following signature (or equivalent):
        //   public IEnumerable<ValidationError> ValidateFragmentDefinition(
        //       FragmentDefinition fragment, ValidationContext ctx)
        // This call will not compile until that method is added.
        var validator = new StructuralValidator(EmptyFragmentRegistry.Instance);
        return validator.ValidateFragmentDefinition(fragment, FragmentCtx(fragment.FragmentId));  // RED: method does not exist yet
    }

    // =========================================================================
    // C1 — R1: ResumePointFragmentId set + non-empty → no error (positive case)
    // =========================================================================

    [Fact]
    public void C1_R1_ResumePointFragmentIdSetNonEmpty_NoError()
    {
        /*
         * AC C1 — Given a TravelStep with ResumePointFragmentId = "fragment-x" inside a valid quest,
         *          When validated,
         *          Then AssertNoError for "structural/resume-point-fragment-id-empty".
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: Will fail at runtime until Builder adds ValidateResumePoint to StructuralValidator.
         */

        // Arrange — RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "valid-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = "fragment-x"  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/resume-point-fragment-id-empty");
    }

    // =========================================================================
    // C2 — R1: ResumePointFragmentId null → no error
    // =========================================================================

    [Fact]
    public void C2_R1_ResumePointFragmentIdNull_NoError()
    {
        /*
         * AC C2 — Given a step with ResumePointFragmentId = null,
         *          When validated,
         *          Then no "structural/resume-point-fragment-id-empty" error.
         *          null (absent) is always legal — the rule fires only when present but empty.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         */

        // Arrange — RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "null-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = null  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/resume-point-fragment-id-empty");
    }

    // =========================================================================
    // C3 — R1: ResumePointFragmentId empty string → Error
    // =========================================================================

    [Fact]
    public void C3_R1_ResumePointFragmentIdEmpty_ProducesError()
    {
        /*
         * AC C3 — Given a step with ResumePointFragmentId = "" (empty string),
         *          When validated,
         *          Then exactly one "structural/resume-point-fragment-id-empty" Error
         *               with Severity.Error and StepId == the step's id.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: Will fail at runtime until Builder adds ValidateResumePoint rule to StructuralValidator.
         */

        // Arrange — RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "bad-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = ""  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertSingleError(errors, "structural/resume-point-fragment-id-empty", "bad-step");
        var err = errors.Single(e => e.Code == "structural/resume-point-fragment-id-empty");
        Assert.Equal(Severity.Error, err.Severity);
    }

    // =========================================================================
    // C4 — R1: ResumePointFragmentId whitespace → Error
    // =========================================================================

    [Fact]
    public void C4_R1_ResumePointFragmentIdWhitespace_ProducesError()
    {
        /*
         * AC C4 — Given ResumePointFragmentId = "   " (whitespace only),
         *          When validated,
         *          Then exactly one "structural/resume-point-fragment-id-empty" Error.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: Will fail at runtime until Builder adds ValidateResumePoint rule.
         */

        // Arrange — RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "whitespace-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = "   "  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertSingleError(errors, "structural/resume-point-fragment-id-empty", "whitespace-step");
        var err = errors.Single(e => e.Code == "structural/resume-point-fragment-id-empty");
        Assert.Equal(Severity.Error, err.Severity);
    }

    // =========================================================================
    // C5 — R1: Nested in BranchStep → walker recurses into Branches[].Steps
    // =========================================================================

    [Fact]
    public void C5_R1_NestedInBranchStep_ErrorReported()
    {
        /*
         * AC C5 — Given a BranchStep whose nested TravelStep has ResumePointFragmentId = "",
         *          When validated,
         *          Then exactly one "structural/resume-point-fragment-id-empty" Error
         *               with the nested step's id (walker recurses into Branches[].Steps).
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: Will fail at runtime until Builder's CheckResumePoint walker recurses into branches.
         */

        // Arrange — nested step with empty ResumePointFragmentId
        // RED: ResumePointFragmentId property does not exist yet
        var nestedStep = new TravelStep
        {
            Id = "nested-bad-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = ""  // RED: property does not exist yet
        };

        var branchStep = new BranchStep
        {
            Id = "branch-step",
            Branches =
            [
                new BranchCase(When: "default", Steps: [nestedStep])
            ]
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branchStep)])).ToList();

        // Assert — single error scoped to the nested step
        var rpeErrors = errors
            .Where(e => e.Code == "structural/resume-point-fragment-id-empty")
            .ToList();
        Assert.True(rpeErrors.Count == 1,
            $"Expected 1 resume-point-fragment-id-empty error but got {rpeErrors.Count}. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}] {e.Message}"))}]");
        Assert.Equal("nested-bad-step", rpeErrors[0].StepId);
        Assert.Equal(Severity.Error, rpeErrors[0].Severity);
    }

    // =========================================================================
    // C6 — R2: Fragment step declaring ResumePointFragmentId → Error
    // =========================================================================

    [Fact]
    public void C6_R2_FragmentStepDeclaringResumePointFragmentId_ProducesError()
    {
        /*
         * AC C6 — Given a FragmentDefinition one of whose steps sets ResumePointFragmentId = "fragment-y",
         *          When the fragment is validated,
         *          Then one "structural/resume-point-nested" Error with that step's id.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         * RED: Will fail at runtime until Builder implements R2 in ValidateFragmentDefinition.
         */

        // Arrange
        // RED: ResumePointFragmentId property does not exist yet
        var nestedStep = new TravelStep
        {
            Id = "fragment-nested-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = "fragment-y"  // RED: property does not exist yet — any value violates R2
        };

        var fragment = MakeFragment("fragment-go-128", nestedStep);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert — R2: any presence of ResumePointFragmentId inside a fragment fires the error
        var r2Errors = errors.Where(e => e.Code == "structural/resume-point-nested").ToList();
        Assert.True(r2Errors.Count >= 1,
            $"Expected at least 1 structural/resume-point-nested error but got 0. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}] {e.Message}"))}]");
        Assert.Contains(r2Errors, e => e.StepId == "fragment-nested-step");
        Assert.Equal(Severity.Error, r2Errors.First().Severity);
    }

    // =========================================================================
    // C7 — R2: Fragment step without ResumePointFragmentId → no R2 error
    // =========================================================================

    [Fact]
    public void C7_R2_FragmentStepWithoutResumePointFragmentId_NoR2Error()
    {
        /*
         * AC C7 — Given a FragmentDefinition whose steps all have ResumePointFragmentId = null,
         *          When validated,
         *          Then no "structural/resume-point-nested" error.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         */

        // Arrange — no ResumePointFragmentId set on any fragment step
        // RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "fragment-clean-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = null  // RED: property does not exist yet
        };

        var fragment = MakeFragment("fragment-go-128", step);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/resume-point-nested");
    }

    // =========================================================================
    // C8 — R3: Fragment step id not "fragment-*" → Warning
    // =========================================================================

    [Fact]
    public void C8_R3_FragmentStepIdNotFragmentPrefixed_ProducesWarning()
    {
        /*
         * AC C8 — Given a FragmentDefinition with a step id "go-to-uldah" (no "fragment-" prefix)
         *          and ResumePointFragmentId = null (so R2 does not fire),
         *          When validated,
         *          Then one "structural/fragment-step-id-convention" Warning with that step's id.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         * RED: Will fail at runtime until Builder implements R3 in ValidateFragmentDefinition.
         */

        // Arrange — step id lacks "fragment-" prefix; no R2 violation
        // RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "go-to-uldah",  // violates R3: no "fragment-" prefix
            Destination = new TravelDestination(128),
            ResumePointFragmentId = null  // RED: property does not exist yet — null so R2 does not fire
        };

        var fragment = MakeFragment("fragment-go-128", step);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert — R3 warning present
        var r3Warnings = errors.Where(e => e.Code == "structural/fragment-step-id-convention").ToList();
        Assert.True(r3Warnings.Count == 1,
            $"Expected 1 structural/fragment-step-id-convention warning but got {r3Warnings.Count}. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}][{e.Severity}] {e.Message}"))}]");
        Assert.Equal("go-to-uldah", r3Warnings[0].StepId);
        Assert.Equal(Severity.Warning, r3Warnings[0].Severity);
    }

    // =========================================================================
    // C9 — R3: Fragment step id with "fragment-*" prefix → no warning
    // =========================================================================

    [Fact]
    public void C9_R3_FragmentStepIdWithFragmentPrefix_NoWarning()
    {
        /*
         * AC C9 — Given a FragmentDefinition with step id "fragment-go-to-uldah" (correct prefix),
         *          When validated,
         *          Then no "structural/fragment-step-id-convention" warning.
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         */

        // Arrange — correct "fragment-" prefix
        // RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "fragment-go-to-uldah",  // satisfies R3
            Destination = new TravelDestination(128),
            ResumePointFragmentId = null  // RED: property does not exist yet
        };

        var fragment = MakeFragment("fragment-go-128", step);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/fragment-step-id-convention");
    }

    // =========================================================================
    // C10 — R3 suppressed when R2 fires on the same step
    // =========================================================================

    [Fact]
    public void C10_R3_SuppressedWhenR2Fires_OnSameStep()
    {
        /*
         * AC C10 — Given a fragment step with:
         *             id = "go" (violates R3: no "fragment-" prefix)
         *             ResumePointFragmentId = "x" (violates R2: any presence inside fragment)
         *           When validated,
         *           Then exactly ONE error/warning for that step:
         *             "structural/resume-point-nested" (R2, Error) IS present,
         *             "structural/fragment-step-id-convention" (R3, Warning) is NOT present
         *             (R3 suppressed because R2 fired on the same step).
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         * RED: Will fail at runtime until Builder implements R3 suppression when R2 fires.
         */

        // Arrange — both violations on the same step
        // RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "go",  // violates R3 (no "fragment-" prefix)
            Destination = new TravelDestination(128),
            ResumePointFragmentId = "x"  // RED: property does not exist yet — violates R2 (any presence)
        };

        var fragment = MakeFragment("fragment-go-128", step);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert R2 fires
        var r2Errors = errors.Where(e => e.Code == "structural/resume-point-nested").ToList();
        Assert.True(r2Errors.Count >= 1,
            $"Expected structural/resume-point-nested to be present but it was not. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}] {e.Message}"))}]");

        // Assert R3 is suppressed (must NOT fire on this step)
        var r3ForSameStep = errors
            .Where(e => e.Code == "structural/fragment-step-id-convention" && e.StepId == "go")
            .ToList();
        Assert.True(r3ForSameStep.Count == 0,
            $"Expected structural/fragment-step-id-convention to be SUPPRESSED for step 'go' (R2 fired) " +
            $"but got {r3ForSameStep.Count} warning(s). " +
            $"R3 must be suppressed when R2 fires on the same step.");
    }

    // =========================================================================
    // C11 — R1 in fragment file: empty ResumePointFragmentId inside a fragment → R2 + R1 interaction
    // =========================================================================

    [Fact]
    public void C11_R1_InFragmentFile_EmptyResumePointFragmentId_ResumeNestedPresent()
    {
        /*
         * AC C11 — Given a FragmentDefinition step with ResumePointFragmentId = "" (empty):
         *             - R2 fires: any presence of ResumePointFragmentId inside a fragment.
         *             - R1 fires (maybe): present but empty/whitespace.
         *           When validated,
         *           Assert: "structural/resume-point-nested" IS present (R2 always fires on presence).
         *           R1 interaction is documented but not over-constrained: the Builder decides
         *           whether R1 also fires (independent walkers) or R2 short-circuits R1.
         *           The test pins that resume-point-nested is present; R1 behavior is asserted
         *           via a softer check (documented below).
         *
         * RED: Will fail to compile until Builder adds ResumePointFragmentId to Step (tools copy).
         * RED: ValidateFragmentDefinition does not exist yet — Builder must add it.
         */

        // Arrange — empty ResumePointFragmentId inside a fragment
        // RED: ResumePointFragmentId property does not exist yet
        var step = new TravelStep
        {
            Id = "fragment-problem-step",
            Destination = new TravelDestination(128),
            ResumePointFragmentId = ""  // RED: property does not exist yet — empty: violates both R2 and R1
        };

        var fragment = MakeFragment("fragment-go-128", step);

        // Act — RED: ValidateFragmentDefinition does not exist yet
        var errors = ValidateFragment(fragment).ToList();

        // Assert R2 (load-bearing): resume-point-nested must be present
        var r2Errors = errors.Where(e => e.Code == "structural/resume-point-nested").ToList();
        Assert.True(r2Errors.Count >= 1,
            $"Expected structural/resume-point-nested to fire on empty ResumePointFragmentId inside a fragment, " +
            $"but it was not present. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}] {e.Message}"))}]");

        // Soft assert for R1 — document but do not over-constrain:
        // Builder may choose: (a) both R1 and R2 emit (independent walkers), or (b) R2 short-circuits R1.
        // Both behaviors are acceptable per §10 (C11 interaction). If R1 fires, verify it is an Error.
        var r1Errors = errors.Where(e => e.Code == "structural/resume-point-fragment-id-empty").ToList();
        if (r1Errors.Count > 0)
        {
            // R1 also fired — verify it is Error severity (not Warning)
            Assert.All(r1Errors, e => Assert.Equal(Severity.Error, e.Severity));
        }
        // else: R2 short-circuited R1 — also acceptable; no assertion failure.
    }
}
