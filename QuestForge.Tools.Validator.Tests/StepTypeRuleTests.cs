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
        // Both Target and Targets are nullable — no "must have one" rule exists.
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
    // structural/duty-missing-required-field and structural/duty-invalid-field-for-kind
    // =========================================================================

    private static DutyStep ValidRegularDuty() => new()
    {
        Id       = "duty-a",
        Kind     = "regular",
        DutyId   = 56,
        EntryNpc = new NpcLocation(1014883, 419, new Position3(0f, 0f, 0f)),
        Expect   = null
    };

    private static DutyStep ValidSpdDuty() => new()
    {
        Id      = "duty-a",
        Kind    = "spd",
        Trigger = new DutyTrigger("npc", 128, new Position3(0f, 0f, 0f), NpcId: 1000789),
        Expect  = null
    };

    [Fact]
    public void DutyStep_ValidRegular_IsValid()
    {
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, ValidRegularDuty())])));
    }

    [Fact]
    public void DutyStep_ValidSpd_IsValid()
    {
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, ValidSpdDuty())])));
    }

    [Fact]
    public void DutyStep_RegularMissingDutyId_ReportsError()
    {
        var step = new DutyStep
        {
            Id       = "duty-a",
            Kind     = "regular",
            DutyId   = null,  // missing
            EntryNpc = new NpcLocation(1014883, 419, new Position3(0f, 0f, 0f)),
            Expect   = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/duty-missing-required-field");
    }

    [Fact]
    public void DutyStep_RegularMissingEntryNpc_ReportsError()
    {
        var step = new DutyStep
        {
            Id       = "duty-a",
            Kind     = "regular",
            DutyId   = 56,
            EntryNpc = null,  // missing
            Expect   = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/duty-missing-required-field");
    }

    [Fact]
    public void DutyStep_SpdMissingTrigger_ReportsError()
    {
        var step = new DutyStep
        {
            Id      = "duty-a",
            Kind    = "spd",
            Trigger = null,  // missing
            Expect  = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/duty-missing-required-field");
    }

    [Fact]
    public void DutyStep_SpdWithDutyId_ReportsInvalidField()
    {
        // Per plan GWT: kind "spd" + DutyId = 56 → duty-invalid-field-for-kind
        var step = new DutyStep
        {
            Id      = "duty-a",
            Kind    = "spd",
            DutyId  = 56,   // invalid for spd
            Trigger = new DutyTrigger("npc", 128, new Position3(0f, 0f, 0f), NpcId: 1000789),
            Expect  = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/duty-invalid-field-for-kind");
    }

    // =========================================================================
    // structural/use-item-target-mismatch
    // =========================================================================

    [Fact]
    public void UseItemStep_NoTarget_IsValid()
    {
        var step = new UseItemStep { Id = "use-a", ItemId = 12345, Target = null, Expect = null };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Theory]
    [InlineData("npc")]
    [InlineData("object")]
    [InlineData("position")]
    public void UseItemStep_TargetKindWithRequiredFields_IsValid(string kind)
    {
        var target = kind switch
        {
            "npc"      => new UseItemTarget { Kind = "npc",      NpcId = 1000789 },
            "object"   => new UseItemTarget { Kind = "object",   InteractableId = 2001234 },
            "position" => new UseItemTarget { Kind = "position", Position = new Position3(0f, 0f, 0f), Tolerance = 3.0f },
            _          => throw new InvalidOperationException()
        };
        var step = new UseItemStep { Id = "use-a", ItemId = 12345, Target = target, Expect = null };
        QuestBuilder.AssertNoErrors(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void UseItemStep_NpcTargetMissingNpcId_ReportsError()
    {
        var step = new UseItemStep
        {
            Id     = "use-a",
            ItemId = 12345,
            Target = new UseItemTarget { Kind = "npc", NpcId = null },
            Expect = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-target-mismatch");
    }

    [Fact]
    public void UseItemStep_ObjectTargetMissingInteractableId_ReportsError()
    {
        var step = new UseItemStep
        {
            Id     = "use-a",
            ItemId = 12345,
            Target = new UseItemTarget { Kind = "object", InteractableId = null },
            Expect = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-target-mismatch");
    }

    [Fact]
    public void UseItemStep_PositionTargetMissingPosition_ReportsError()
    {
        var step = new UseItemStep
        {
            Id     = "use-a",
            ItemId = 12345,
            Target = new UseItemTarget { Kind = "position", Position = null, Tolerance = 3.0f },
            Expect = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-target-mismatch");
    }

    [Fact]
    public void UseItemStep_PositionTargetMissingTolerance_ReportsError()
    {
        var step = new UseItemStep
        {
            Id     = "use-a",
            ItemId = 12345,
            Target = new UseItemTarget { Kind = "position", Position = new Position3(0f, 0f, 0f), Tolerance = null },
            Expect = null
        };
        QuestBuilder.AssertSingleError(
            QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])),
            "structural/use-item-target-mismatch");
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
        // Empty next = terminus — no default required
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