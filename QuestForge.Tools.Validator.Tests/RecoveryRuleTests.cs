using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class RecoveryRuleTests
{
    // =========================================================================
    // structural/recovery-goto-unresolved
    // =========================================================================

    [Fact]
    public void GotoRecoverAction_ReferencingExistingStep_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("origin",
                    new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "target" } }),
                QuestBuilder.Step("target"))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void GotoRecoverAction_ReferencingNonExistentStep_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a",
                    new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "does-not-exist" } }))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recovery-goto-unresolved");
    }

    [Fact]
    public void GotoRecoverAction_AllTriggerSlotsChecked()
    {
        // Five trigger slots — all missing targets should each produce an error.
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a",
                    new RecoverConfig
                    {
                        OnTimeout            = new GotoRecoverAction { StepId = "ghost-1" },
                        OnObstacle           = new GotoRecoverAction { StepId = "ghost-2" },
                        OnAdapterError       = new GotoRecoverAction { StepId = "ghost-3" },
                        OnPostconditionFailed = new GotoRecoverAction { StepId = "ghost-4" },
                        OnPlayerDefeated     = new GotoRecoverAction { StepId = "ghost-5" }
                    }))
        ]);
        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Equal(5, errors.Count(e => e.Code == "structural/recovery-goto-unresolved"));
    }

    [Fact]
    public void GotoRecoverAction_ReferencingDuplicateId_IsSuppressed()
    {
        // The target ID exists but is ambiguous (duplicated).
        // Neither goto-unresolved nor goto-cross-branch should fire;
        // step-id-duplicate already covers the problem.
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("shared"),
                QuestBuilder.Step("shared"),
                QuestBuilder.Step("origin",
                    new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "shared" } }))
        ]);
        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.DoesNotContain(errors, e => e.Code == "structural/recovery-goto-unresolved");
        Assert.DoesNotContain(errors, e => e.Code == "structural/recovery-goto-cross-branch");
        Assert.Contains(errors, e => e.Code == "structural/step-id-duplicate");
    }

    // =========================================================================
    // structural/recovery-goto-cross-branch
    // =========================================================================

    [Fact]
    public void GotoRecoverAction_SameScopeTarget_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a"),
                QuestBuilder.Step("step-b",
                    new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "step-a" } }))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void GotoRecoverAction_SelfReference_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a",
                    new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "step-a" } }))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void GotoRecoverAction_OuterStepTargetingInsideBranch_ReportsCrossBranch()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("default", [QuestBuilder.Step("inner-step")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var outer = QuestBuilder.Step("outer-step",
            new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "inner-step" } });

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, outer, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recovery-goto-cross-branch");
    }

    [Fact]
    public void GotoRecoverAction_InsideBranchTargetingOuterStep_ReportsCrossBranch()
    {
        var innerStep = QuestBuilder.Step("inner-step",
            new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "outer-step" } });

        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("default", [innerStep])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, QuestBuilder.Step("outer-step"), branch)
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recovery-goto-cross-branch");
    }

    [Fact]
    public void GotoRecoverAction_BetweenDifferentBranchCases_ReportsCrossBranch()
    {
        var caseZeroStep = QuestBuilder.Step("case-zero-step",
            new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "case-one-step" } });

        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", [caseZeroStep]),
                new BranchCase("default",             [QuestBuilder.Step("case-one-step")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recovery-goto-cross-branch");
    }

    [Fact]
    public void GotoRecoverAction_WithinSameBranchCase_IsValid()
    {
        var caseBStep = QuestBuilder.Step("case-step-b",
            new RecoverConfig { OnTimeout = new GotoRecoverAction { StepId = "case-step-a" } });

        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("default", [QuestBuilder.Step("case-step-a"), caseBStep])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    // =========================================================================
    // structural/recover-reason-too-long
    // =========================================================================

    [Fact]
    public void AwaitUserStep_ReasonExactly200Chars_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new AwaitUserStep
            {
                Id     = "await-step",
                Reason = new string('a', 200),
                Expect = null
            })
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void AwaitUserStep_Reason201Chars_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new AwaitUserStep
            {
                Id     = "await-step",
                Reason = new string('a', 201),
                Expect = null
            })
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recover-reason-too-long");
    }

    [Fact]
    public void AwaitUserRecoverAction_ReasonExactly200Chars_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a",
                    new RecoverConfig { OnTimeout = new AwaitUserRecoverAction { Reason = new string('a', 200) } }))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void AwaitUserRecoverAction_Reason201Chars_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                QuestBuilder.Step("step-a",
                    new RecoverConfig { OnTimeout = new AwaitUserRecoverAction { Reason = new string('a', 201) } }))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/recover-reason-too-long");
    }
}