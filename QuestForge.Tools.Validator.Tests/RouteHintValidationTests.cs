using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// RED PHASE: Tests for the new validator rule that checks TravelStep.RouteHint.Aethernet
/// (Issue #25, Component 8).
///
/// The new rules are:
///   - Error   "structural/route-hint-aethernet-to-missing"   when To == 0
///   - Warning "structural/route-hint-aethernet-from-missing" when From == null
///
/// ALL tests will fail until Builder:
///   1. Adds AethernetRouteHint record to QuestForge.Schema
///   2. Changes RouteHint.Aethernet from string[]? to AethernetRouteHint? in the tools-side schema
///   3. Adds the validation rule in StructuralValidator that walks TravelStep entries
/// </summary>
public sealed class RouteHintValidationTests
{
    // =========================================================================
    // RHV_1 — TravelStep with valid RouteHint.Aethernet (From + To) → no error
    // =========================================================================

    [Fact]
    public void RHV_1_ValidAethernetHint_FromAndTo_NoError()
    {
        /*
         * RED: Will fail to compile until Builder adds AethernetRouteHint to Schema
         * and changes RouteHint.Aethernet from string[]? to AethernetRouteHint?.
         *
         * CONTRACT: Given a TravelStep with RouteHint.Aethernet = { From=125, To=77 },
         *           When validated,
         *           Then no validation errors are produced for route-hint codes.
         *
         * BUILDER GUIDANCE: A fully specified AethernetRouteHint with To > 0 and From set
         * passes validation cleanly.
         */

        // RED: AethernetRouteHint does not exist yet
        var step = new TravelStep
        {
            Id          = "aethernet-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: 125u, To: 77u))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-to-missing");
        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-from-missing");
    }

    // =========================================================================
    // RHV_2 — TravelStep with Aethernet.To > 0 and From == null → Warning only
    // =========================================================================

    [Fact]
    public void RHV_2_ValidTo_FromNull_ProducesFromMissingWarning()
    {
        /*
         * RED: Will fail to compile until Builder adds AethernetRouteHint.
         *
         * CONTRACT: Given RouteHint.Aethernet = { From=null, To=77 },
         *           When validated,
         *           Then exactly one warning "structural/route-hint-aethernet-from-missing" is produced.
         *           No error "structural/route-hint-aethernet-to-missing" is produced.
         *
         * BUILDER GUIDANCE: From is optional (nullable); its absence produces a Warning
         * (not Error) so authors can commit partial data and fix later.
         */

        // RED: AethernetRouteHint does not exist yet
        var step = new TravelStep
        {
            Id          = "aethernet-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: null, To: 77u))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-to-missing");

        var fromWarnings = errors
            .Where(e => e.Code == "structural/route-hint-aethernet-from-missing")
            .ToList();
        Assert.True(fromWarnings.Count == 1,
            $"Expected 1 warning for route-hint-aethernet-from-missing but got {fromWarnings.Count}.");
        Assert.Equal(Severity.Warning, fromWarnings[0].Severity);
    }

    // =========================================================================
    // RHV_3 — TravelStep with Aethernet.To == 0 → Error
    // =========================================================================

    [Fact]
    public void RHV_3_ToIsZero_ProducesToMissingError()
    {
        /*
         * RED: Will fail to compile until Builder adds AethernetRouteHint.
         *
         * CONTRACT: Given RouteHint.Aethernet = { From=125, To=0 },
         *           When validated,
         *           Then exactly one error "structural/route-hint-aethernet-to-missing" is produced.
         *
         * BUILDER GUIDANCE: To == 0 is the sentinel for "not set". Since To is the
         * mandatory field (the shard the engine will teleport to), a zero value is invalid.
         */

        // RED: AethernetRouteHint does not exist yet
        var step = new TravelStep
        {
            Id          = "aethernet-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: 125u, To: 0u))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        var toErrors = errors
            .Where(e => e.Code == "structural/route-hint-aethernet-to-missing")
            .ToList();
        Assert.True(toErrors.Count == 1,
            $"Expected 1 error for route-hint-aethernet-to-missing but got {toErrors.Count}.");
        Assert.Equal(Severity.Error, toErrors[0].Severity);
    }

    // =========================================================================
    // RHV_4 — TravelStep with Aethernet.To == 0 AND From == null → Error + Warning
    // =========================================================================

    [Fact]
    public void RHV_4_ToZeroAndFromNull_ProducesBothErrors()
    {
        /*
         * RED: Will fail to compile until Builder adds AethernetRouteHint.
         *
         * CONTRACT: Given RouteHint.Aethernet = { From=null, To=0 },
         *           When validated,
         *           Then both errors are produced:
         *             - Error "structural/route-hint-aethernet-to-missing"
         *             - Warning "structural/route-hint-aethernet-from-missing"
         *
         * BUILDER GUIDANCE: Both rules are independent; each must fire separately.
         */

        // RED: AethernetRouteHint does not exist yet
        var step = new TravelStep
        {
            Id          = "aethernet-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: null, To: 0u))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        Assert.Contains(errors, e => e.Code == "structural/route-hint-aethernet-to-missing"
                                     && e.Severity == Severity.Error);
        Assert.Contains(errors, e => e.Code == "structural/route-hint-aethernet-from-missing"
                                     && e.Severity == Severity.Warning);
    }

    // =========================================================================
    // RHV_5 — TravelStep with no RouteHint → no route-hint errors
    // =========================================================================

    [Fact]
    public void RHV_5_NoRouteHint_NoRouteHintErrors()
    {
        /*
         * CONTRACT: Given a TravelStep with RouteHint == null,
         *           When validated,
         *           Then no route-hint-aethernet errors are produced.
         *
         * BUILDER GUIDANCE: The validation rule only fires when RouteHint?.Aethernet is
         * non-null. A null RouteHint is not subject to these rules.
         */

        var step = new TravelStep
        {
            Id          = "travel-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = null
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-to-missing");
        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-from-missing");
    }

    // =========================================================================
    // RHV_6 — TravelStep with RouteHint.Aetheryte only (no Aethernet) → no route-hint errors
    // =========================================================================

    [Fact]
    public void RHV_6_RouteHintAetheryteOnly_NoRouteHintErrors()
    {
        /*
         * CONTRACT: Given a TravelStep with RouteHint.Aetheryte set and Aethernet == null,
         *           When validated,
         *           Then no route-hint-aethernet errors are produced.
         *
         * BUILDER GUIDANCE: Aethernet is null → the rule does not apply.
         */

        var step = new TravelStep
        {
            Id          = "travel-step",
            Destination = new TravelDestination(130),
            RouteHint   = new RouteHint(Aetheryte: "Limsa Lominsa Lower Decks", Aethernet: null)
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-to-missing");
        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-from-missing");
    }

    // =========================================================================
    // RHV_7 — Multiple TravelSteps: only the bad one produces errors
    // =========================================================================

    [Fact]
    public void RHV_7_MultipleTravelSteps_OnlyBadOneErrors()
    {
        /*
         * RED: Will fail to compile until Builder adds AethernetRouteHint.
         *
         * CONTRACT: Given two TravelSteps — one with valid Aethernet, one with To==0,
         *           When validated,
         *           Then exactly one error for route-hint-aethernet-to-missing
         *                (only for the bad step, not the valid one).
         *
         * BUILDER GUIDANCE: The validation walk iterates over all steps; only steps
         * with non-null Aethernet where To==0 produce the error.
         */

        // RED: AethernetRouteHint does not exist yet
        var goodStep = new TravelStep
        {
            Id          = "good-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: 125u, To: 77u))
        };
        var badStep = new TravelStep
        {
            Id          = "bad-step",
            Destination = new TravelDestination(131, new Position3(5f, 0f, 5f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: 125u, To: 0u))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, goodStep, badStep)])).ToList();

        var toErrors = errors
            .Where(e => e.Code == "structural/route-hint-aethernet-to-missing")
            .ToList();
        Assert.Single(toErrors);
        Assert.Equal("bad-step", toErrors[0].StepId);
    }

    // =========================================================================
    // RHV_8 — Non-TravelStep (TalkStep) with no RouteHint → rule does not apply
    // =========================================================================

    [Fact]
    public void RHV_8_NonTravelStep_NoRouteHintChecked()
    {
        /*
         * CONTRACT: Given a TalkStep (not a TravelStep),
         *           When validated,
         *           Then no route-hint-aethernet codes appear in errors.
         *
         * BUILDER GUIDANCE: The rule walks only TravelStep entries. TalkStep, TurnInStep,
         * and other step types are not subject to this check.
         */

        var step = new TalkStep
        {
            Id     = "talk-step",
            Target = new NpcLocation(1000789u, 130, new Position3(0f, 0f, 0f))
        };

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-to-missing");
        QuestBuilder.AssertNoError(errors, "structural/route-hint-aethernet-from-missing");
    }

    // =========================================================================
    // RHV_9 — BranchStep with nested TravelStep whose Aethernet.To == 0 → Error reported
    // =========================================================================

    [Fact]
    public void RHV_9_BranchStep_NestedTravelWithMissingTo_ErrorReported()
    {
        /*
         * CONTRACT: Given a BranchStep containing a nested TravelStep where
         *           RouteHint.Aethernet.To == 0,
         *           When validated,
         *           Then exactly one error "structural/route-hint-aethernet-to-missing"
         *                is produced with the nested TravelStep's ID ("nested-bad-step").
         *
         * BUILDER GUIDANCE: The validator must recurse into BranchStep.Branches[].Steps
         * so that nested TravelSteps are subject to the same route-hint rules as
         * top-level steps. A validator that only walks top-level steps will miss this.
         */

        var nestedBadStep = new TravelStep
        {
            Id          = "nested-bad-step",
            Destination = new TravelDestination(130, new Position3(10f, 0f, 20f)),
            RouteHint   = new RouteHint(Aethernet: new AethernetRouteHint(From: 125u, To: 0u))
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

        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, branchStep)])).ToList();

        var toErrors = errors
            .Where(e => e.Code == "structural/route-hint-aethernet-to-missing")
            .ToList();
        Assert.True(toErrors.Count == 1,
            $"Expected 1 error for route-hint-aethernet-to-missing but got {toErrors.Count}.");
        Assert.Equal(Severity.Error, toErrors[0].Severity);
        Assert.Equal("nested-bad-step", toErrors[0].StepId);
    }
}
