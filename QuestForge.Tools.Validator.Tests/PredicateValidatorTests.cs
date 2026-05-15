using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class PredicateValidatorTests
{
    private static IEnumerable<ValidationError> Validate(
        QuestDefinition quest, IFragmentRegistry? registry = null)
    {
        var reg = registry ?? EmptyFragmentRegistry.Instance;
        return new PredicateValidator(reg).Validate(quest, QuestBuilder.Ctx(quest.Id));
    }

    // ---- happy path --------------------------------------------------------

    [Fact]
    public void ValidPredicates_ProduceNoErrors()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                Expect      = new PredicateExpect { Predicate = "playerZone() == 128" }
            })
        ]);
        Assert.Empty(Validate(quest));
    }

    // ---- §4.10 location reporting -----------------------------------------

    [Fact]
    public void MalformedPredicate_ReportsErrorAtCorrectLocation()
    {
        // "questSequnece" is a typo → predicate/unknown-function
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,
                new TravelStep
                {
                    Id          = "step-a",
                    Destination = new TravelDestination(128),
                    Expect      = new PredicateExpect { Predicate = "questSequnece(65657) >= 1" }  // typo
                },
                new TravelStep
                {
                    Id          = "step-b",
                    Destination = new TravelDestination(128),
                    Expect      = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }  // valid
                })
        ]);

        var errors = Validate(quest).ToList();
        Assert.Equal(1, errors.Count);
        Assert.Equal("predicate/unknown-function", errors[0].Code);
        Assert.Equal("seq:0/step:step-a/expect",   errors[0].Location);
        Assert.Equal("step-a",                      errors[0].StepId);
    }

    // ---- §4.11 cache -------------------------------------------------------

    [Fact]
    public void RepeatedValidPredicate_ProducesNoErrors()
    {
        // Same predicate in 17 steps — cache must not break anything
        var steps = Enumerable.Range(0, 17)
            .Select(i => (Step)new TravelStep
            {
                Id          = $"step-{i}",
                Destination = new TravelDestination(128),
                Expect      = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
            })
            .ToArray();

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, steps)]);
        Assert.Empty(Validate(quest));
    }

    [Fact]
    public void RepeatedInvalidPredicate_ReportsOneErrorPerSite()
    {
        // Cache returns the same ParseResult — but each site still reports its own error
        var steps = Enumerable.Range(0, 3)
            .Select(i => (Step)new TravelStep
            {
                Id          = $"step-{i}",
                Destination = new TravelDestination(128),
                Expect      = new PredicateExpect { Predicate = "questSequnece(65657) >= 1" }
            })
            .ToArray();

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, steps)]);
        var errors = Validate(quest).ToList();
        // One error per site, even though Parse is only called once
        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Equal("predicate/unknown-function", e.Code));
    }

    // ---- predicate site coverage ------------------------------------------

    [Fact]
    public void SequenceSkipIf_IsValidated()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            new QuestSequence
            {
                Sequence = 0,
                SkipIf   = "questSequnece(65657) >= 1",  // typo
                Steps    = [QuestBuilder.Step("step-a")]
            }
        ]);

        var errors = Validate(quest).ToList();
        Assert.Contains(errors, e =>
            e.Code == "predicate/unknown-function" && e.Location == "seq:0");
    }

    [Fact]
    public void StepSkipIf_IsValidated()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                SkipIf      = new PredicateExpect { Predicate = "questSequnece(65657) >= 1" }
            })
        ]);

        var errors = Validate(quest).ToList();
        Assert.Contains(errors, e =>
            e.Code == "predicate/unknown-function" &&
            e.Location == "seq:0/step:step-a/skipIf");
    }

    [Fact]
    public void AllExpect_EachElementValidated()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                Expect      = new AllExpect { All =
                [
                    "questSequnece(65657) >= 1",  // typo — index 0
                    "playerZone() == 128"          // valid  — index 1
                ]}
            })
        ]);

        var errors = Validate(quest).ToList();
        Assert.Equal(1, errors.Count);
        Assert.Equal("predicate/unknown-function", errors[0].Code);
        Assert.Equal("seq:0/step:step-a/expect/all[0]", errors[0].Location);
    }

    [Fact]
    public void AnyExpect_EachElementValidated()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                Expect      = new AnyExpect { Any =
                [
                    "playerZone() == 128",         // valid  — index 0
                    "questSequnece(65657) >= 1"    // typo   — index 1
                ]}
            })
        ]);

        var errors = Validate(quest).ToList();
        Assert.Equal(1, errors.Count);
        Assert.Equal("seq:0/step:step-a/expect/any[1]", errors[0].Location);
    }

    [Fact]
    public void ChainNextWhen_IsValidated()
    {
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain
            {
                Next = [new ChainNext("questSequnece(65657) >= 1", null)]  // typo
            }
        };

        var errors = Validate(quest).ToList();
        Assert.Contains(errors, e =>
            e.Code == "predicate/unknown-function" &&
            e.Location == "chain/next[0]/when");
    }

    [Fact]
    public void ChainNextWhen_Default_IsValid()
    {
        var quest = QuestBuilder.Valid() with
        {
            Chain = new Chain { Next = [new ChainNext("default", 65658)] }
        };
        Assert.Empty(Validate(quest));
    }

    [Fact]
    public void BranchWhen_IsValidated()
    {
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("questSequnece(65657) >= 1",  // typo
                    [QuestBuilder.Step("inner-a")]),
                new BranchCase("default",
                    [QuestBuilder.Step("inner-b")])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        var errors = Validate(quest).ToList();
        Assert.Contains(errors, e =>
            e.Code == "predicate/unknown-function" &&
            e.Location == "seq:0/step:the-branch/branches[0]/when");
    }

    [Fact]
    public void StepInsideBranch_ExpectIsValidated()
    {
        var innerStep = new TravelStep
        {
            Id          = "inner-step",
            Destination = new TravelDestination(128),
            Expect      = new PredicateExpect { Predicate = "questSequnece(65657) >= 1" }  // typo
        };
        var branch = new BranchStep
        {
            Id       = "the-branch",
            Branches = [new BranchCase("default", [innerStep])],
            Expect   = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };

        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        var errors = Validate(quest).ToList();
        Assert.Contains(errors, e =>
            e.Code == "predicate/unknown-function" &&
            e.Location.Contains("inner-step") &&
            e.Location.EndsWith("/expect"));
    }
}