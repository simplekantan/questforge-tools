using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class StepTypeRuleTests
{
    // =========================================================================
    // structural/step-target-conflict  (TalkStep: target + targets mutually exclusive)
    // =========================================================================

    [Fact]
    public void TalkStep_TargetOnly_IsValid()
    {
        var step = new TalkStep
        {
            Id     = "talk-a",
            Target = new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f)),
            Expect = null
        };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void TalkStep_TargetsOnly_IsValid()
    {
        var step = new TalkStep
        {
            Id      = "talk-a",
            Targets = [new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f))],
            Expect  = null
        };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void TalkStep_NeitherTargetNorTargets_IsValid()
    {
        // Both Target and Targets are nullable â€” no "must have one" rule exists.
        // The engine advances dialogue without moving when neither is set.
        var step = new TalkStep { Id = "talk-a", Target = null, Targets = null, Expect = null };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void TalkStep_BothTargetAndTargets_ReportsConflict()
    {
        var step = new TalkStep
        {
            Id      = "talk-a",
            Target  = new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f)),
            Targets = [new NpcLocation(1000790, 128, new Position3(1f, 0f, 0f))],
            Expect  = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/step-target-conflict");
    }

    // =========================================================================
    // structural/duty-missing-required-field (kind: "duty")
    // =========================================================================

    private static DutyStep ValidDutyKindDuty() => new()
    {
        Id                       = "duty-a",
        Kind                     = "duty",
        ContentFinderConditionId = 2,
        Expect                   = null
    };

    [Fact]
    public void DutyStep_ValidDutyKind_IsValid()
    {
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, ValidDutyKindDuty())])));
    }

    [Fact]
    public void DutyStep_ValidSpd_IsValid()
    {
        var step = new DutyStep { Id = "duty-a", Kind = "spd", Expect = null };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void DutyStep_DutyKindMissingCfcId_ReportsError()
    {
        var step = new DutyStep
        {
            Id   = "duty-a",
            Kind = "duty",
            ContentFinderConditionId = null,
            Expect = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/duty-missing-required-field");
    }


    // =========================================================================
    // UseItemStep rules â€” new flat schema shape (Decision UI11 / USE_ITEM_STEP_PLAN.md)
    // Replaces the six old UseItemStep_* tests that referenced the deleted UseItemTarget shape.
    //
    //   - ItemKind enum in SharedValueTypes.cs
    //   - UseItemStep { Kind: ItemKind, ItemId: uint, TargetNpcId: uint?, TargetPosition: Position3? }
    // Until then the tests produce compile errors â€” intended RED state.
    // =========================================================================

    // structural/use-item-itemid-zero â€” mirrors engine-side E13

    [Fact]
    public void UseItemStep_SelfCast_IsValid()
    {
        // Both target fields null + non-zero ItemId â†’ valid (self-cast pattern)
        var step = new UseItemStep
        {
            Id          = "use-a",
            Kind        = ItemKind.InventoryItem,
            ItemId      = 12345u,
            TargetNpcId = null,
            TargetPosition = null,
            Expect      = null
        };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void UseItemStep_NpcTarget_IsValid()
    {
        var step = new UseItemStep
        {
            Id          = "use-b",
            Kind        = ItemKind.KeyItem,
            ItemId      = 2000456u,
            TargetNpcId = 1000789u,
            TargetPosition = null,
            Expect      = null
        };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void UseItemStep_PositionTarget_IsValid()
    {
        var step = new UseItemStep
        {
            Id             = "use-c",
            Kind           = ItemKind.KeyItem,
            ItemId         = 2000123u,
            TargetNpcId    = null,
            TargetPosition = new Position3(1f, 0f, 2f),
            Expect         = null
        };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void UseItemStep_ItemIdZero_ReportsError()
    {
        var step = new UseItemStep
        {
            Id          = "use-a",
            Kind        = ItemKind.KeyItem,
            ItemId      = 0u,               // zero is never valid
            TargetNpcId = null,
            TargetPosition = null,
            Expect      = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-itemid-zero");
    }

    [Fact]
    public void UseItemStep_NpcTargetZero_ReportsError()
    {
        // Explicit zero is invalid for TargetNpcId; null means "no NPC target"
        var step = new UseItemStep
        {
            Id          = "use-a",
            Kind        = ItemKind.KeyItem,
            ItemId      = 12345u,
            TargetNpcId = 0u,               // explicit zero is always invalid
            TargetPosition = null,
            Expect      = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-target-npc-id-zero");
    }

    [Fact]
    public void UseItemStep_BothTargetsSet_ReportsError()
    {
        // TargetNpcId and TargetPosition are mutually exclusive (Decision UI4 / E15 / UI11)
        var step = new UseItemStep
        {
            Id             = "use-a",
            Kind           = ItemKind.KeyItem,
            ItemId         = 2000456u,
            TargetNpcId    = 1000789u,          // both set â€” ambiguous target
            TargetPosition = new Position3(1f, 2f, 3f),
            Expect         = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-ambiguous-target");
    }

    // =========================================================================
    // structural/dialogue-choice-type-invalid
    // =========================================================================

    [Theory]
    [InlineData("list",  "TEXT_JOBDRK301_02054_A1_000_116")]  // text sheet reference
    [InlineData("yesno", "yes")]                               // literal yes/no
    [InlineData("talk",  null)]                                // no answer for talk
    public void TalkStep_ValidDialogueChoiceType_IsValid(string type, string? answer)
    {
        var step = new TalkStep
        {
            Id              = "talk-a",
            DialogueChoices = [new DialogueChoice(type, "prompt", answer)],
            Expect          = null
        };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void TalkStep_InvalidDialogueChoiceType_ReportsError()
    {
        var step = new TalkStep
        {
            Id              = "talk-a",
            DialogueChoices = [new DialogueChoice("checkbox", "prompt")],
            Expect          = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/dialogue-choice-type-invalid");
    }

    [Fact]
    public void TurnInStep_InvalidDialogueChoiceType_ReportsError()
    {
        // Dialogue choice validation applies to TurnInStep as well
        var step = new TurnInStep
        {
            Id              = "turn-in-a",
            Target          = new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f)),
            DialogueChoices = [new DialogueChoice("select", "prompt")],
            Expect          = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/dialogue-choice-type-invalid");
    }

    // =========================================================================
    // structural/dialogue-choice-answer-invalid
    // =========================================================================

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    public void TalkStep_YesNoWithValidAnswer_IsValid(string answer)
    {
        var step = new TalkStep
        {
            Id              = "talk-a",
            DialogueChoices = [new DialogueChoice("yesno", "prompt", answer)],
            Expect          = null
        };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData(null)]
    public void TalkStep_YesNoWithInvalidAnswer_ReportsError(string? answer)
    {
        var step = new TalkStep
        {
            Id              = "talk-a",
            DialogueChoices = [new DialogueChoice("yesno", "prompt", answer)],
            Expect          = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/dialogue-choice-answer-invalid");
    }

    // =========================================================================
    // structural/cutscene-skip-invalid
    // =========================================================================

    [Theory]
    [InlineData("never")]
    [InlineData("ifAllowed")]
    public void CutsceneStep_ValidSkip_IsValid(string skip)
    {
        var step = new CutsceneStep { Id = "cut-a", Skip = skip, Expect = null };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Theory]
    [InlineData("always")]   // valid for minigame but NOT cutscene
    [InlineData("skip")]
    public void CutsceneStep_InvalidSkip_ReportsError(string skip)
    {
        var step = new CutsceneStep { Id = "cut-a", Skip = skip, Expect = null };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/cutscene-skip-invalid");
    }

    // =========================================================================
    // structural/minigame-kind-invalid
    // =========================================================================

    [Theory]
    [InlineData("sniping")]
    [InlineData("memory")]
    [InlineData("aiming")]
    [InlineData("rhythm")]
    [InlineData("selection")]
    [InlineData("other")]
    public void MinigameStep_ValidKind_IsValid(string kind)
    {
        var step = new MinigameStep { Id = "mini-a", Kind = kind, Expect = null };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void MinigameStep_InvalidKind_ReportsError()
    {
        var step = new MinigameStep { Id = "mini-a", Kind = "jumping-puzzle", Expect = null };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/minigame-kind-invalid");
    }

    // =========================================================================
    // structural/prereq-state-invalid
    // =========================================================================

    [Theory]
    [InlineData("complete")]
    [InlineData("accepted")]
    public void Prereq_ValidState_IsValid(string state)
    {
        var quest = QuestBuilder.Valid() with
        {
            Requirements = new Requirements { Prereqs = [new PrerequisiteRef(65656, state)] }
        };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void Prereq_InvalidState_ReportsError()
    {
        var quest = QuestBuilder.Valid() with
        {
            Requirements = new Requirements { Prereqs = [new PrerequisiteRef(65656, "finished")] }
        };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/prereq-state-invalid");
    }

    // =========================================================================
    // structural/chain-missing-default and structural/chain-when-empty
    // =========================================================================

    [Fact]
    public void Chain_NoChain_IsValid()
    {
        var quest = QuestBuilder.Valid() with { Chain = null };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void Chain_LastEntryIsDefault_IsValid()
    {
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain { Next = [new ChainNext("default", 65658)] }
        };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void Chain_EmptyNext_IsValid()
    {
        // Empty next = terminus â€” no default required
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain { Next = [] }
        };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void Chain_LastEntryNotDefault_ReportsMissingDefault()
    {
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain
            {
                Next =
                [
                    new ChainNext("playerStartingClass() == \"Gladiator\"", 65700),
                    new ChainNext("playerStartingClass() == \"Pugilist\"",  65710)
                    // missing default
                ]
            }
        };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/chain-missing-default");
    }

    [Fact]
    public void Chain_EmptyWhen_ReportsError()
    {
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain
            {
                Next =
                [
                    new ChainNext("", 65700),       // empty when
                    new ChainNext("default", 65658)
                ]
            }
        };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/chain-when-empty");
    }
}