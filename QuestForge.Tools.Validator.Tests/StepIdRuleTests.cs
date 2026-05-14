using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class StepIdRuleTests
{
    // -------------------------------------------------------------------------
    // Format rule: ^[a-z][a-z0-9-]*$
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("step-a")]
    [InlineData("go-to-npc")]
    [InlineData("fight")]
    [InlineData("a")]
    [InlineData("step-1")]
    [InlineData("ab12-cd34")]
    public void ValidStepId_ProducesNoErrors(string id)
    {
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, QuestBuilder.Step(id))]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Theory]
    [InlineData("Step-A",  "uppercase letters not allowed")]
    [InlineData("_step",   "leading underscore not allowed")]
    [InlineData("1step",   "must start with letter")]
    [InlineData("-step",   "must start with letter")]
    [InlineData("step a",  "spaces not allowed")]
    [InlineData("step.a",  "dots not allowed")]
    [InlineData("STEP",    "uppercase not allowed")]
    [InlineData("",        "empty id not allowed")]
    public void InvalidStepIdFormat_ReportsError(string id, string reason)
    {
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, QuestBuilder.Step(id))]);

        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.True(errors.Any(e => e.Code == "structural/step-id-invalid-format"),
            $"Expected format error for id '{id}' ({reason}). Got: [{string.Join(", ", errors.Select(e => e.Code))}]");
    }

    // -------------------------------------------------------------------------
    // Uniqueness rule: globally unique across all sequences and branch nesting
    // -------------------------------------------------------------------------

    [Fact]
    public void DuplicateStepIdInSameSequence_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, QuestBuilder.Step("talk-a"), QuestBuilder.Step("talk-a"))
        ]);

        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/step-id-duplicate");
    }

    [Fact]
    public void DuplicateStepIdAcrossSequences_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,   QuestBuilder.Step("shared-id")),
            QuestBuilder.Seq(255, QuestBuilder.Step("shared-id"))
        ]);

        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/step-id-duplicate");
    }

    [Fact]
    public void DuplicateStepIdInsideBranch_ReportsError()
    {
        var branch = new BranchStep
        {
            Id = "branch-step",
            Branches =
            [
                new BranchCase("default",
                [
                    QuestBuilder.Step("inner"),
                    QuestBuilder.Step("inner")   // duplicate within same branch
                ])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, branch)
        ]);

        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/step-id-duplicate");
    }

    [Fact]
    public void DuplicateStepIdBetweenOuterAndBranch_ReportsError()
    {
        var branch = new BranchStep
        {
            Id = "branch-step",
            Branches =
            [
                new BranchCase("default", [QuestBuilder.Step("shared-id")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        // "shared-id" appears both in outer sequence and inside branch
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, QuestBuilder.Step("shared-id"), branch)
        ]);

        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/step-id-duplicate");
    }

    [Fact]
    public void UniqueBranchStepIds_AcrossDifferentBranches_AreValid()
    {
        // Same step IDs in different branch cases are valid (each case is its own scope)
        // — wait, no: the plan says IDs must be globally unique across the entire quest.
        // Different branch cases are still within the same quest, so IDs must be unique.
        var branch = new BranchStep
        {
            Id = "branch-step",
            Branches =
            [
                new BranchCase("playerLevel() >= 15", [QuestBuilder.Step("case-zero-step")]),
                new BranchCase("default",             [QuestBuilder.Step("case-one-step")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void SameIdInDifferentBranchCases_ReportsDuplicate()
    {
        var branch = new BranchStep
        {
            Id = "branch-step",
            Branches =
            [
                new BranchCase("playerLevel() >= 15", [QuestBuilder.Step("shared")]),
                new BranchCase("default",             [QuestBuilder.Step("shared")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/step-id-duplicate");
    }

    [Fact]
    public void MultipleDuplicateIds_ReportsOneErrorEachId()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("dup-a"),
                QuestBuilder.Step("dup-a"),
                QuestBuilder.Step("dup-b"),
                QuestBuilder.Step("dup-b"),
                QuestBuilder.Step("unique"))
        ]);

        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Equal(2, errors.Count(e => e.Code == "structural/step-id-duplicate"));
    }
}