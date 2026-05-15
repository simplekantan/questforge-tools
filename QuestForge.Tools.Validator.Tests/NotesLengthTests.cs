using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class NotesLengthTests
{
    // =========================================================================
    // Quest-level notes
    // =========================================================================

    [Fact]
    public void QuestNotes_Null_IsValid()
    {
        var quest = QuestBuilder.Valid() with { Notes = null };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void QuestNotes_Exactly500Chars_IsValid()
    {
        var quest = QuestBuilder.Valid() with { Notes = new string('a', 500) };
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void QuestNotes_501Chars_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { Notes = new string('a', 501) };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/notes-too-long");
    }

    // =========================================================================
    // Step-level notes
    // =========================================================================

    [Fact]
    public void StepNotes_Null_IsValid()
    {
        var step = QuestBuilder.Step("step-a");  // Notes defaults to null
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, step)])));
    }

    [Fact]
    public void StepNotes_Exactly500Chars_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                Notes       = new string('a', 500)
            })
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void StepNotes_501Chars_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, new TravelStep
            {
                Id          = "step-a",
                Destination = new TravelDestination(128),
                Notes       = new string('a', 501)
            })
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/notes-too-long");
    }

    [Fact]
    public void StepNotes_InsideBranch_501Chars_ReportsError()
    {
        // Notes check recurses into branch cases.
        var branch = new BranchStep
        {
            Id = "the-branch",
            Branches =
            [
                new BranchCase("default",
                [
                    new TravelStep
                    {
                        Id          = "inner-step",
                        Destination = new TravelDestination(128),
                        Notes       = new string('a', 501)
                    }
                ])
            ],
            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, branch)]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/notes-too-long");
    }
}