using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Tests for the PurchaseItemStep tools mirror: the "purchase-item" step type must deserialize
/// through the shared quest-file options and the structural validator enforces rules F1-F4.
///
/// Rules under test:
///   - Error "structural/purchase-item-id-zero"        when ItemId == 0
///   - Error "structural/purchase-quantity-nonpositive" when Quantity &lt; 1
///   - Error "structural/purchase-npc-id-zero"         when Target.NpcId == 0
/// </summary>
public sealed class PurchaseItemValidationTests
{
    // Valid baseline target used by every test that is NOT testing the npc-id rule.
    private static readonly NpcLocation ValidTarget =
        new NpcLocation(1001234u, 128, new Position3(10.5f, 0f, -20.0f));

    // =========================================================================
    // Schema: "purchase-item" steps deserialize via the shared quest-file options.
    // =========================================================================

    [Fact]
    public void PI_Deserialize_PurchaseItemStep_GilDefault_FromJson()
    {
        // Given the JSON an author would write (currency omitted → defaults to Gil),
        // When deserialized through QuestFileOptions (the qf-validate load path),
        // Then it resolves to a PurchaseItemStep with the expected field values.
        const string json = """
            {
              "type": "purchase-item",
              "id": "buy-x",
              "target": { "npcId": 1001234, "zone": 128, "position": { "x": 10.5, "y": 0, "z": -20 } },
              "itemId": 1601,
              "quantity": 1
            }
            """;

        var step = JsonSerializer.Deserialize<Step>(json, QuestForgeJsonContext.QuestFileOptions);

        var purchase = Assert.IsType<PurchaseItemStep>(step);
        Assert.Equal("buy-x", purchase.Id);
        Assert.Equal(1601u, purchase.ItemId);
        Assert.Equal(1, purchase.Quantity);
        Assert.Equal(PurchaseCurrency.Gil, purchase.Currency);
        Assert.Equal(1001234u, purchase.Target.NpcId);
    }

    [Fact]
    public void PI_Deserialize_PurchaseItemStep_GcSeals_FromJson()
    {
        // Given JSON with "currency": "gcSeals",
        // When deserialized,
        // Then Currency == GcSeals.
        const string json = """
            {
              "type": "purchase-item",
              "id": "buy-gc",
              "target": { "npcId": 1001234, "zone": 128, "position": { "x": 10.5, "y": 0, "z": -20 } },
              "itemId": 6141,
              "quantity": 3,
              "currency": "gcSeals"
            }
            """;

        var step = JsonSerializer.Deserialize<Step>(json, QuestForgeJsonContext.QuestFileOptions);

        var purchase = Assert.IsType<PurchaseItemStep>(step);
        Assert.Equal(PurchaseCurrency.GcSeals, purchase.Currency);
        Assert.Equal(6141u, purchase.ItemId);
        Assert.Equal(3, purchase.Quantity);
    }

    // =========================================================================
    // structural/purchase-item-id-zero (F1)
    // =========================================================================

    [Fact]
    public void PI_F1_ItemIdZero_ReportsError()
    {
        // Given a purchase-item step with ItemId == 0 (valid Target and Quantity),
        // Then exactly one structural/purchase-item-id-zero Error is produced.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 0,
            Quantity = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-item-id-zero");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    // =========================================================================
    // structural/purchase-quantity-nonpositive (F2)
    // =========================================================================

    [Fact]
    public void PI_F2_QuantityZero_ReportsError()
    {
        // Given a purchase-item step with Quantity == 0 (valid Target and ItemId),
        // Then exactly one structural/purchase-quantity-nonpositive Error is produced.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 1601,
            Quantity = 0
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-quantity-nonpositive");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void PI_F2_QuantityNegative_ReportsError()
    {
        // Given a purchase-item step with Quantity == -2,
        // Then the structural/purchase-quantity-nonpositive Error is produced.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 1601,
            Quantity = -2
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-quantity-nonpositive");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    // =========================================================================
    // structural/purchase-npc-id-zero (F3)
    // =========================================================================

    [Fact]
    public void PI_F3_NpcIdZero_ReportsError()
    {
        // Given a purchase-item step with Target.NpcId == 0 (valid ItemId and Quantity),
        // Then exactly one structural/purchase-npc-id-zero Error is produced.
        var zeroNpcTarget = new NpcLocation(0u, 128, new Position3(10.5f, 0f, -20.0f));
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = zeroNpcTarget,
            ItemId   = 1601,
            Quantity = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-npc-id-zero");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    // =========================================================================
    // F4 — all valid fields produce no purchase-* errors
    // =========================================================================

    [Fact]
    public void PI_F4_ValidStep_NoItemIdZeroError()
    {
        // Given a well-formed purchase-item step (ItemId>0, Quantity>=1, NpcId>0),
        // Then no structural/purchase-item-id-zero error is emitted.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 1601,
            Quantity = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-item-id-zero");
    }

    [Fact]
    public void PI_F4_ValidStep_NoQuantityNonpositiveError()
    {
        // Given a well-formed purchase-item step,
        // Then no structural/purchase-quantity-nonpositive error is emitted.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 1601,
            Quantity = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-quantity-nonpositive");
    }

    [Fact]
    public void PI_F4_ValidStep_NoNpcIdZeroError()
    {
        // Given a well-formed purchase-item step,
        // Then no structural/purchase-npc-id-zero error is emitted.
        var step = new PurchaseItemStep
        {
            Id       = "buy-a",
            Target   = ValidTarget,
            ItemId   = 1601,
            Quantity = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-npc-id-zero");
    }

    // =========================================================================
    // G-F — structural/purchase-gc-category-out-of-range,
    //       structural/purchase-gc-rank-tier-out-of-range,
    //       structural/purchase-gc-fields-on-gil
    // =========================================================================

    [Fact]
    public void GF1_GcCategoryAboveMax_ReportsError()
    {
        // Given a purchase-item step with Currency=GcSeals and GcCategory=4 (above max of 3),
        // When validated,
        // Then exactly one structural/purchase-gc-category-out-of-range Error is produced.
        var step = new PurchaseItemStep
        {
            Id         = "buy-a",
            Target     = ValidTarget,
            ItemId     = 1601,
            Quantity   = 1,
            Currency   = PurchaseCurrency.GcSeals,
            GcCategory = 4
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-category-out-of-range");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void GF2_GcCategoryNegative_ReportsError()
    {
        // Given a purchase-item step with Currency=GcSeals and GcCategory=-1,
        // When validated,
        // Then exactly one structural/purchase-gc-category-out-of-range Error is produced.
        var step = new PurchaseItemStep
        {
            Id         = "buy-a",
            Target     = ValidTarget,
            ItemId     = 1601,
            Quantity   = 1,
            Currency   = PurchaseCurrency.GcSeals,
            GcCategory = -1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-category-out-of-range");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void GF3_GcRankTierAboveMax_ReportsError()
    {
        // Given a purchase-item step with Currency=GcSeals and GcRankTier=3 (above max of 2),
        // When validated,
        // Then exactly one structural/purchase-gc-rank-tier-out-of-range Error is produced.
        var step = new PurchaseItemStep
        {
            Id          = "buy-a",
            Target      = ValidTarget,
            ItemId      = 1601,
            Quantity    = 1,
            Currency    = PurchaseCurrency.GcSeals,
            GcRankTier  = 3
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-rank-tier-out-of-range");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void GF4_GcRankTierNegative_ReportsError()
    {
        // Given a purchase-item step with Currency=GcSeals and GcRankTier=-1,
        // When validated,
        // Then exactly one structural/purchase-gc-rank-tier-out-of-range Error is produced.
        var step = new PurchaseItemStep
        {
            Id          = "buy-a",
            Target      = ValidTarget,
            ItemId      = 1601,
            Quantity    = 1,
            Currency    = PurchaseCurrency.GcSeals,
            GcRankTier  = -1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-rank-tier-out-of-range");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void GF5_GcCategoryZero_Weapons_NoErrorNoWarning()
    {
        // Given a purchase-item step with Currency=GcSeals, GcCategory=0, GcRankTier=0 (both at minimum valid),
        // When validated,
        // Then none of the three GC-related error codes are emitted.
        var step = new PurchaseItemStep
        {
            Id          = "buy-a",
            Target      = ValidTarget,
            ItemId      = 1601,
            Quantity    = 1,
            Currency    = PurchaseCurrency.GcSeals,
            GcCategory  = 0,
            GcRankTier  = 0
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-category-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-rank-tier-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-fields-on-gil");
    }

    [Fact]
    public void GF6_GilWithGcCategory_ReportsWarning()
    {
        // Given a purchase-item step with Currency=Gil and GcCategory=2,
        // When validated,
        // Then exactly one structural/purchase-gc-fields-on-gil Warning is produced.
        var step = new PurchaseItemStep
        {
            Id         = "buy-a",
            Target     = ValidTarget,
            ItemId     = 1601,
            Quantity   = 1,
            Currency   = PurchaseCurrency.Gil,
            GcCategory = 2
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-fields-on-gil");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void GF7_GilWithGcRankTier_ReportsWarning()
    {
        // Given a purchase-item step with Currency=Gil and GcRankTier=1,
        // When validated,
        // Then exactly one structural/purchase-gc-fields-on-gil Warning is produced.
        var step = new PurchaseItemStep
        {
            Id         = "buy-a",
            Target     = ValidTarget,
            ItemId     = 1601,
            Quantity   = 1,
            Currency   = PurchaseCurrency.Gil,
            GcRankTier = 1
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/purchase-gc-fields-on-gil");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void GF8_GilBothNull_NoWarning()
    {
        // Given a purchase-item step with Currency=Gil, GcCategory=null, GcRankTier=null,
        // When validated,
        // Then no structural/purchase-gc-fields-on-gil warning is emitted.
        var step = new PurchaseItemStep
        {
            Id         = "buy-a",
            Target     = ValidTarget,
            ItemId     = 1601,
            Quantity   = 1,
            Currency   = PurchaseCurrency.Gil,
            GcCategory = null,
            GcRankTier = null
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-fields-on-gil");
    }

    [Fact]
    public void GF9_GcSealsBothInRange_NoPurchaseGcErrors()
    {
        // Given a purchase-item step with Currency=GcSeals, GcCategory=2, GcRankTier=0 (both in-range),
        // When validated,
        // Then none of the three GC-related error codes are emitted.
        var step = new PurchaseItemStep
        {
            Id          = "buy-a",
            Target      = ValidTarget,
            ItemId      = 1601,
            Quantity    = 1,
            Currency    = PurchaseCurrency.GcSeals,
            GcCategory  = 2,
            GcRankTier  = 0
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-category-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-rank-tier-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-fields-on-gil");
    }

    [Fact]
    public void GF10_BothAtMax_NoError()
    {
        // Boundary-positive: locks the inclusive upper end of both ranges so an off-by-one
        // toward restrictive (e.g. validator using `> 2` instead of `> 3` for GcCategory)
        // would be caught here rather than slipping through external review.
        var step = new PurchaseItemStep
        {
            Id          = "buy-a",
            Target      = ValidTarget,
            ItemId      = 1601,
            Quantity    = 1,
            Currency    = PurchaseCurrency.GcSeals,
            GcCategory  = 3,
            GcRankTier  = 2
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-category-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-rank-tier-out-of-range");
        QuestBuilder.AssertNoError(errors, "structural/purchase-gc-fields-on-gil");
    }

    // =========================================================================
    // Branch recursion: purchase rule fires for nested PurchaseItemStep
    // =========================================================================

    [Fact]
    public void PI_PurchaseItemStepInBranch_ItemIdZero_ReportsError()
    {
        // Verifies the purchase-item-id-zero rule recurses into BranchStep cases.
        var purchase = new PurchaseItemStep
        {
            Id       = "buy-nested",
            Target   = ValidTarget,
            ItemId   = 0,
            Quantity = 1
        };
        var branch = new BranchStep
        {
            Id       = "branch-a",
            Branches = [new BranchCase("default", [purchase])],
            Expect   = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, branch)])).ToList();

        Assert.True(
            errors.Any(e => e.Code == "structural/purchase-item-id-zero"),
            $"Expected structural/purchase-item-id-zero for nested purchase step but got: " +
            $"[{string.Join(", ", errors.Select(e => $"{e.Code}: {e.Message}"))}]");
    }
}
