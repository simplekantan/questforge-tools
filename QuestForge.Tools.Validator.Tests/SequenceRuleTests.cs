using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class SequenceRuleTests
{
    [Fact]
    public void SingleSequenceZero_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences: [QuestBuilder.Seq(0, QuestBuilder.Step("a"))]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void SequencesWithGap_IsValid()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,   QuestBuilder.Step("a")),
            QuestBuilder.Seq(5,   QuestBuilder.Step("b")),
            QuestBuilder.Seq(255, QuestBuilder.Step("c"))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest));
    }

    [Fact]
    public void MissingSequenceZero_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(1,   QuestBuilder.Step("a")),
            QuestBuilder.Seq(255, QuestBuilder.Step("b"))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/sequence-zero-missing");
    }

    [Fact]
    public void NonIncreasingSequence_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,   QuestBuilder.Step("a")),
            QuestBuilder.Seq(5,   QuestBuilder.Step("b")),
            QuestBuilder.Seq(3,   QuestBuilder.Step("c")),
            QuestBuilder.Seq(255, QuestBuilder.Step("d"))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/sequence-not-increasing");
    }

    [Fact]
    public void DuplicateSequenceNumbers_ReportsErrors()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0,   QuestBuilder.Step("a")),
            QuestBuilder.Seq(0,   QuestBuilder.Step("b")),
            QuestBuilder.Seq(255, QuestBuilder.Step("c"))
        ]);

        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Contains(errors, e => e.Code == "structural/sequence-duplicate");
        Assert.Contains(errors, e => e.Code == "structural/sequence-not-increasing");
    }
}