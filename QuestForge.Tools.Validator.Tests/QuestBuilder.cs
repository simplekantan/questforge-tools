using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Builds minimal valid QuestDefinitions for tests.
/// Start from <see cref="Valid"/> and mutate one property to isolate the rule under test.
/// </summary>
internal static class QuestBuilder
{
    public static QuestDefinition Valid(
        uint id = 65657,
        string schemaVersion = "1.0.0",
        QuestSequence[]? sequences = null) => new()
    {
        SchemaVersion    = schemaVersion,
        Id               = id,
        Name             = "Test Quest",
        Expansion        = "arr",
        Category         = "msq",
        Enabled          = true,
        SupportStatus    = new SupportStatus { Implementation = "complete", KnownIssues = [] },
        LastVerifiedPatch = "7.4",
        Requirements     = new Requirements(),
        AcceptFrom       = new NpcLocation(1000789, 128, new Position3(0f, 0f, 0f)),
        Sequences        = sequences ?? [Seq(0, Step("step-a"))]
    };

    public static QuestSequence Seq(int number, params Step[] steps) => new()
    {
        Sequence = number,
        Steps    = steps
    };

    public static TravelStep Step(string id, string? expect = null) => new()
    {
        Id          = id,
        Destination = new TravelDestination(128),
        Expect      = expect is null ? null : new PredicateExpect { Predicate = expect }
    };

    public static ValidationContext Ctx(uint questId = 65657) =>
        new("test-quest.json", questId);

    public static IEnumerable<ValidationError> Validate(
        QuestDefinition quest,
        IFragmentRegistry? registry = null,
        ValidationContext? ctx = null)
    {
        var validator = new StructuralValidator(registry ?? EmptyFragmentRegistry.Instance);
        return validator.Validate(quest, ctx ?? Ctx(quest.Id));
    }

    public static void AssertSingleError(
        IEnumerable<ValidationError> errors,
        string code,
        string? stepId = null)
    {
        var list = errors.ToList();
        var matching = list.Where(e => e.Code == code).ToList();
        Assert.True(matching.Count == 1,
            $"Expected exactly 1 error with code '{code}' but got {matching.Count}. " +
            $"All errors: [{string.Join(", ", list.Select(e => e.Code))}]");

        if (stepId is not null)
            Assert.Equal(stepId, matching[0].StepId);
    }

    public static void AssertNoError(IEnumerable<ValidationError> errors, string code)
    {
        var matching = errors.Where(e => e.Code == code).ToList();
        Assert.True(matching.Count == 0,
            $"Expected no error with code '{code}' but got {matching.Count}.");
    }

    public static void AssertNoErrors(IEnumerable<ValidationError> errors)
    {
        var list = errors.ToList();
        Assert.True(list.Count == 0,
            $"Expected no errors but got: [{string.Join(", ", list.Select(e => $"{e.Code}: {e.Message}"))}]");
    }
}