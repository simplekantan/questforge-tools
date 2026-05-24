using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// RED PHASE: Tests for the new validator rule that checks Step.RequiredZone.
/// Issue #38 — group D acceptance criteria (D1–D8).
///
/// Rule: structural/required-zone-not-numeric (Severity.Error)
/// Fires when: step.RequiredZone is a non-empty string that is NOT a valid non-negative
///             decimal integer (i.e. uint.TryParse with NumberStyles.None rejects it).
/// null and empty string are always legal (absence is allowed).
/// The walker recurses into BranchStep.Branches[].Steps, identical to CheckRouteHints.
/// The rule applies to ALL Step types, not only TravelStep.
///
/// ALL tests will fail until Builder:
///   1. Adds RequiredZone property to Step base class in QuestForge.Schema (tools-side copy)
///   2. Adds ValidateRequiredZone / CheckRequiredZone walker to StructuralValidator
///   3. Emits "structural/required-zone-not-numeric" with Severity.Error for bad values
/// </summary>
public sealed class RequiredZoneValidationTests
{
    // =========================================================================
    // D1 — Numeric RequiredZone → no error
    // =========================================================================

    [Fact]
    public void D1_NumericRequiredZone_NoError()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema (tools copy).
         *
         * CONTRACT: Given a TravelStep with RequiredZone = "128" (valid numeric zone ID),
         *           When validated,
         *           Then AssertNoError for "structural/required-zone-not-numeric".
         */

        // Arrange — RED: RequiredZone property does not exist yet in tools-side Step.cs
        var step = new TravelStep
        {
            Id           = "valid-step",
            Destination  = new TravelDestination(128),
            RequiredZone = "128"  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/required-zone-not-numeric");
    }

    // =========================================================================
    // D2 — null RequiredZone → no error
    // =========================================================================

    [Fact]
    public void D2_NullRequiredZone_NoError()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema.
         *
         * CONTRACT: Given a TravelStep with RequiredZone = null,
         *           When validated,
         *           Then no "structural/required-zone-not-numeric" error.
         *
         * null means "not set" — absence is always legal.
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var step = new TravelStep
        {
            Id           = "null-zone-step",
            Destination  = new TravelDestination(128),
            RequiredZone = null  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/required-zone-not-numeric");
    }

    // =========================================================================
    // D3 — Empty string RequiredZone → no error
    // =========================================================================

    [Fact]
    public void D3_EmptyStringRequiredZone_NoError()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema.
         *
         * CONTRACT: Given RequiredZone = "" (empty string),
         *           When validated,
         *           Then no "structural/required-zone-not-numeric" error.
         *
         * The rule condition: rz is non-empty AND non-numeric → fire.
         * An empty string is not non-empty → rule does NOT fire.
         * (The factory never produces "", but an author might clear the field.)
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var step = new TravelStep
        {
            Id           = "empty-zone-step",
            Destination  = new TravelDestination(128),
            RequiredZone = ""  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertNoError(errors, "structural/required-zone-not-numeric");
    }

    // =========================================================================
    // D4 — Non-numeric RequiredZone → Error
    // =========================================================================

    [Fact]
    public void D4_NonNumericRequiredZone_ProducesError()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema and
         * adds the ValidateRequiredZone rule to StructuralValidator.
         *
         * CONTRACT: Given RequiredZone = "Ul'dah" (a place name, not a numeric ID),
         *           When validated,
         *           Then exactly one error "structural/required-zone-not-numeric"
         *                with Severity.Error and StepId == "bad-step".
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var step = new TravelStep
        {
            Id           = "bad-step",
            Destination  = new TravelDestination(128),
            RequiredZone = "Ul'dah"  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert — use AssertSingleError to guard against extra unexpected errors
        QuestBuilder.AssertSingleError(errors, "structural/required-zone-not-numeric", "bad-step");

        // Also verify severity is Error (not Warning)
        var err = errors.Single(e => e.Code == "structural/required-zone-not-numeric");
        Assert.Equal(Severity.Error, err.Severity);
    }

    // =========================================================================
    // D5 — Negative / garbage numeric string → Error
    // =========================================================================

    [Theory]
    [InlineData("-5")]    // negative integer — uint.TryParse(NumberStyles.None) rejects signs
    [InlineData("12a")]   // alphanumeric — not a pure decimal integer
    public void D5_NegativeOrGarbageRequiredZone_ProducesError(string badValue)
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema.
         *
         * CONTRACT: Given RequiredZone = "-5" or "12a",
         *           When validated,
         *           Then exactly one "structural/required-zone-not-numeric" Error.
         *
         * uint.TryParse with NumberStyles.None rejects signs and non-digit characters.
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var step = new TravelStep
        {
            Id           = "bad-step",
            Destination  = new TravelDestination(128),
            RequiredZone = badValue  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert
        QuestBuilder.AssertSingleError(errors, "structural/required-zone-not-numeric", "bad-step");
        Assert.Equal(Severity.Error,
            errors.Single(e => e.Code == "structural/required-zone-not-numeric").Severity);
    }

    // =========================================================================
    // D6 — Multiple steps: only the bad one produces an error
    // =========================================================================

    [Fact]
    public void D6_MultipleSteps_OnlyBadOneProducesError()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema.
         *
         * CONTRACT: Given two steps — one with RequiredZone = "128" (valid)
         *           and one with RequiredZone = "bad" (invalid),
         *           When validated,
         *           Then exactly one "structural/required-zone-not-numeric" error
         *                whose StepId is the bad step's id ("bad-step").
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var goodStep = new TravelStep
        {
            Id           = "good-step",
            Destination  = new TravelDestination(128),
            RequiredZone = "128"   // RED: property does not exist yet
        };
        var badStep = new TravelStep
        {
            Id           = "bad-step",
            Destination  = new TravelDestination(130),
            RequiredZone = "bad"   // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, goodStep, badStep)])).ToList();

        // Assert — exactly one error, scoped to the bad step
        var rznErrors = errors
            .Where(e => e.Code == "structural/required-zone-not-numeric")
            .ToList();
        Assert.Single(rznErrors);
        Assert.Equal("bad-step", rznErrors[0].StepId);
        Assert.Equal(Severity.Error, rznErrors[0].Severity);
    }

    // =========================================================================
    // D7 — Nested in BranchStep: walker recurses into Branches[].Steps
    // =========================================================================

    [Fact]
    public void D7_NestedInBranchStep_ErrorReported()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema and
         * the CheckRequiredZone walker recurses into BranchStep.Branches[].Steps.
         *
         * CONTRACT: Given a BranchStep whose nested TravelStep has RequiredZone = "bad",
         *           When validated,
         *           Then exactly one "structural/required-zone-not-numeric" Error
         *                with StepId == "nested-bad-step".
         *
         * This mirrors RHV_9's pattern — proves the walker is recursive, not top-level-only.
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var nestedBadStep = new TravelStep
        {
            Id           = "nested-bad-step",
            Destination  = new TravelDestination(128),
            RequiredZone = "bad"  // RED: property does not exist yet
        };

        var branchStep = new BranchStep
        {
            Id       = "branch-step",
            Branches =
            [
                new BranchCase(
                    When: "questSequence == 1",
                    Steps: [nestedBadStep])
            ]
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branchStep)])).ToList();

        // Assert
        var rznErrors = errors
            .Where(e => e.Code == "structural/required-zone-not-numeric")
            .ToList();
        Assert.True(rznErrors.Count == 1,
            $"Expected 1 structural/required-zone-not-numeric error but got {rznErrors.Count}. " +
            $"Errors: [{string.Join(", ", errors.Select(e => $"[{e.Code}] {e.Message}"))}]");
        Assert.Equal(Severity.Error, rznErrors[0].Severity);
        Assert.Equal("nested-bad-step", rznErrors[0].StepId);
    }

    // =========================================================================
    // D8 — Non-TravelStep (AcceptStep): rule walks ALL step types, not TravelStep-only
    // =========================================================================

    [Fact]
    public void D8_AcceptStep_NonNumericRequiredZone_ErrorReported()
    {
        /*
         * RED: Will fail to compile until Builder adds RequiredZone to Schema and the
         * rule walks all Step types (not just TravelStep, unlike the RouteHint rule).
         *
         * CONTRACT: Given an AcceptStep with RequiredZone = "bad",
         *           When validated,
         *           Then exactly one "structural/required-zone-not-numeric" Error
         *                with StepId == "accept-step".
         *
         * Distinct from D4 (TravelStep) — this proves the rule is not TravelStep-only.
         * The RouteHint rule (CheckRouteHints) is TravelStep-only; the RequiredZone rule
         * must check every Step subtype since RequiredZone is a base-class field.
         */

        // Arrange — RED: RequiredZone property does not exist yet
        var step = new AcceptStep
        {
            Id           = "accept-step",
            Target       = new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f)),
            RequiredZone = "bad"  // RED: property does not exist yet
        };

        // Act
        var errors = QuestBuilder.Validate(
            QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])).ToList();

        // Assert — single error scoped to the accept step
        QuestBuilder.AssertSingleError(errors, "structural/required-zone-not-numeric", "accept-step");
        Assert.Equal(Severity.Error,
            errors.Single(e => e.Code == "structural/required-zone-not-numeric").Severity);
    }
}
