using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

public class FragmentRuleTests
{
    // Creates a minimal FragmentDefinition with the given parameters and steps.
    private static FragmentDefinition Frag(
        string id,
        FragmentParameter[] parameters,
        Step[]? steps = null) => new()
    {
        SchemaVersion = "1.0.0",
        FragmentId    = id,
        Parameters    = parameters,
        Steps         = steps ?? [QuestBuilder.Step("frag-inner-step")]
    };

    // Creates a FragmentStep referencing a fragment.
    private static FragmentStep FragStep(
        string stepId,
        string fragmentRef,
        Dictionary<string, JsonElement>? @params = null) => new()
    {
        Id     = stepId,
        Ref    = fragmentRef,
        Params = @params,
        Expect = null
    };

    // Parses a JSON string into a JsonElement for use in Params dictionaries.
    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    // =========================================================================
    // structural/fragment-not-found
    // =========================================================================

    [Fact]
    public void FragmentStep_RefResolvesInRegistry_IsValid()
    {
        var registry = new InMemoryFragmentRegistry([Frag("travel/somewhere", [])]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere"))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest, registry: registry));
    }

    [Fact]
    public void FragmentStep_RefNotInRegistry_ReportsError()
    {
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/nonexistent"))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/fragment-not-found");
    }

    [Fact]
    public void FragmentStep_NotFound_SuppressesParamChecks()
    {
        // Per plan GWT: fragment-not-found → no additional param errors
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/nonexistent",
                new Dictionary<string, JsonElement> { ["finalPosition"] = Json("""{"x":1,"y":2,"z":3}""") }))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest), "structural/fragment-not-found");
    }

    // =========================================================================
    // structural/fragment-missing-param
    // =========================================================================

    [Fact]
    public void FragmentStep_AllRequiredParamsProvided_IsValid()
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("finalPosition", "position", Required: true)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere",
                new Dictionary<string, JsonElement> { ["finalPosition"] = Json("""{"x":1,"y":2,"z":3}""") }))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest, registry: registry));
    }

    [Fact]
    public void FragmentStep_MissingRequiredParam_ReportsError()
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("finalPosition", "position", Required: true)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere", @params: null))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest, registry: registry),
            "structural/fragment-missing-param");
    }

    [Fact]
    public void FragmentStep_OptionalParamOmitted_IsValid()
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("routeHint", "string", Required: false)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere", @params: null))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest, registry: registry));
    }

    [Fact]
    public void FragmentStep_MultipleRequiredParamsMissing_ReportsOneErrorPerParam()
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("paramA", "npcId",    Required: true),
            new FragmentParameter("paramB", "position", Required: true)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere", @params: null))
        ]);
        var errors = QuestBuilder.Validate(quest, registry: registry).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Equal(2, errors.Count(e => e.Code == "structural/fragment-missing-param"));
    }

    // =========================================================================
    // structural/fragment-param-type-mismatch
    // =========================================================================

    [Theory]
    [InlineData("position", """{"x":1,"y":2,"z":3}""")]
    [InlineData("npcId",    "1000789")]
    [InlineData("itemId",   "12345")]
    [InlineData("string",   "\"Ul'dah - Steps of Nald\"")]
    public void FragmentStep_ParamTypeMatches_IsValid(string declaredType, string json)
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("param", declaredType, Required: true)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere",
                new Dictionary<string, JsonElement> { ["param"] = Json(json) }))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest, registry: registry));
    }

    [Theory]
    [InlineData("position", "42")]       // expected object, got number
    [InlineData("npcId",    "\"abc\"")] // expected number, got string
    [InlineData("string",   "99")]       // expected string, got number
    public void FragmentStep_ParamTypeMismatch_ReportsError(string declaredType, string json)
    {
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("param", declaredType, Required: true)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere",
                new Dictionary<string, JsonElement> { ["param"] = Json(json) }))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest, registry: registry),
            "structural/fragment-param-type-mismatch");
    }

    [Fact]
    public void FragmentStep_OptionalParamProvidedWithWrongType_ReportsError()
    {
        // Optional params must still match the declared type when provided.
        var frag = Frag("travel/somewhere",
        [
            new FragmentParameter("routeHint", "string", Required: false)
        ]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere",
                new Dictionary<string, JsonElement> { ["routeHint"] = Json("42") })) // string expected, number given
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest, registry: registry),
            "structural/fragment-param-type-mismatch");
    }

    // =========================================================================
    // structural/fragment-nested
    // =========================================================================

    [Fact]
    public void FragmentStep_FragmentHasNoFragmentSteps_IsValid()
    {
        var frag = Frag("travel/somewhere", [],
            steps: [QuestBuilder.Step("inner-travel")]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere"))
        ]);
        QuestBuilder.AssertNoErrors(QuestBuilder.Validate(quest, registry: registry));
    }

    [Fact]
    public void FragmentStep_FragmentContainsFragmentStep_ReportsError()
    {
        // Schema §5.3: fragments cannot reference other fragments.
        var nested = new FragmentStep
        {
            Id  = "nested-ref",
            Ref = "common/some-other-fragment"
        };
        var frag = Frag("travel/somewhere", [], steps: [nested]);
        var registry = new InMemoryFragmentRegistry([frag]);
        var quest = QuestBuilder.Valid(sequences:
        [
            QuestBuilder.Seq(0, FragStep("frag-a", "travel/somewhere"))
        ]);
        QuestBuilder.AssertSingleError(QuestBuilder.Validate(quest, registry: registry),
            "structural/fragment-nested");
    }
}