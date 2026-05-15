using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class BranchRuleTests
{
    // Builds a single-case branch (when: "default") with the given inner steps.
    // Useful for nesting depth tests where the cases themselves are not under test.
    private static BranchStep Branch(string id, params Step[] steps) => new()
    {
        Id       = id,
        Branches = [new BranchCase("default", steps)],
        Expect   = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
    };

    // =========================================================================
    // structural/branch-empty
    // =========================================================================

    [Fact]
    public void BranchStep_NonEmptyCase_IsValid()
    {
        // Two-case branch — both cases non-empty — isolates the empty rule from the default rule.
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", [QuestBuilder.Step("case-a")]),
                new BranchCase("default",             [QuestBuilder.Step("case-b")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void BranchStep_EmptyBranchCase_ReportsError()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", []),
                new BranchCase("default", [QuestBuilder.Step("case-b")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/branch-empty");
    }

    [Fact]
    public void BranchStep_MultipleEmptyCases_ReportsOneErrorPerCase()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", []),
                new BranchCase("default", [])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Equal(2, errors.Count(e => e.Code == "structural/branch-empty"));
    }

    // =========================================================================
    // structural/branch-nesting-too-deep
    // =========================================================================

    [Fact]
    public void BranchStep_DepthTwo_ReportsOneWarning()
    {
        // b1 (d1) → b2 (d2) triggers the warning threshold
        var d2    = Branch("b2", QuestBuilder.Step("leaf"));
        var d1    = Branch("b1", d2);
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, d1)]);

        var errors = QuestBuilder.Validate(quest).ToList();
        QuestBuilder.AssertSingleError(errors, "structural/branch-nesting-too-deep");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void BranchStep_DepthThree_ReportsTwoWarnings()
    {
        // depth 2 and depth 3 both warn — still within the ≤3 limit, no errors
        var d3    = Branch("b3", QuestBuilder.Step("leaf"));
        var d2    = Branch("b2", d3);
        var d1    = Branch("b1", d2);
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, d1)]);

        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Equal(2, errors.Count(e => e.Code == "structural/branch-nesting-too-deep" && e.Severity == Severity.Warning));
        Assert.DoesNotContain(errors, e => e.Severity == Severity.Error);
    }

    [Fact]
    public void BranchStep_DepthFour_ReportsError()
    {
        // d4 exceeds the ≤3 limit — an error fires in addition to the d2/d3 warnings
        var d4    = Branch("b4", QuestBuilder.Step("leaf"));
        var d3    = Branch("b3", d4);
        var d2    = Branch("b2", d3);
        var d1    = Branch("b1", d2);
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, d1)]);

        var errors = QuestBuilder.Validate(quest).ToList();
        // d2 and d3 still warn (not escalated to errors); d4 is the error
        Assert.Equal(2, errors.Count(e => e.Code == "structural/branch-nesting-too-deep" && e.Severity == Severity.Warning));
        Assert.Contains(errors, e => e.Code == "structural/branch-nesting-too-deep" && e.Severity == Severity.Error);
    }

    // =========================================================================
    // structural/branch-missing-default
    // =========================================================================

    [Fact]
    public void BranchStep_SingleDefaultCase_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, Branch("b", QuestBuilder.Step("step-a")))]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void BranchStep_MultiCaseLastIsDefault_IsValid()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", [QuestBuilder.Step("case-a")]),
                new BranchCase("default",             [QuestBuilder.Step("case-b")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void BranchStep_LastCaseNotDefault_ReportsMissingDefault()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("playerLevel() >= 20", [QuestBuilder.Step("case-a")]),
                new BranchCase("playerLevel() >= 10", [QuestBuilder.Step("case-b")])  // no default
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/branch-missing-default");
    }

    [Fact]
    public void BranchStep_DefaultNotLast_ReportsMissingDefault()
    {
        // "default" in a non-terminal position; schema §4.18 requires it last
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("default",             [QuestBuilder.Step("case-a")]),
                new BranchCase("playerLevel() >= 10", [QuestBuilder.Step("case-b")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/branch-missing-default");
    }
}
