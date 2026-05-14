using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class RequiredFieldTests
{
    private const string Code = "structural/required-field-missing";

    [Fact]
    public void ValidQuest_ProducesNoErrors()
    {
        var errors = QuestBuilder.Validate(QuestBuilder.Valid());
        QuestBuilder.AssertNoErrors(errors);
    }

    [Fact]
    public void MissingSchemaVersion_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { SchemaVersion = null! };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void EmptySchemaVersion_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { SchemaVersion = "" };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void MissingSupportStatus_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { SupportStatus = null };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void MissingLastVerifiedPatch_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { LastVerifiedPatch = null };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void EmptyLastVerifiedPatch_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { LastVerifiedPatch = "" };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void MissingRequirements_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { Requirements = null };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void MissingAcceptFrom_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { AcceptFrom = null };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), Code);
    }

    [Fact]
    public void EmptySequences_ReportsError()
    {
        var quest = QuestBuilder.Valid() with { Sequences = [] };
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/sequences-empty");
    }

    [Fact]
    public void MultipleRequiredFieldsMissing_ReportsOneErrorEach()
    {
        var quest = QuestBuilder.Valid() with
        {
            SupportStatus     = null,
            LastVerifiedPatch = null,
            Requirements      = null,
            AcceptFrom        = null
        };

        var errors = QuestBuilder.Validate(quest).ToList();
        Assert.Equal(4, errors.Count(e => e.Code == Code));
    }
}