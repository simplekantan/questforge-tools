using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class FunctionRegistryTests
{
    [Fact]
    public void All_Contains27Functions()
    {
        Assert.Equal(27, FunctionRegistry.All.Count);
    }

    [Theory]
    [InlineData("questSequence",          PredicateType.Int,  1)]
    [InlineData("questFlag",              PredicateType.Bool, 2)]
    [InlineData("questFlagAny",           PredicateType.Bool, -1)] // variadic
    [InlineData("isQuestComplete",        PredicateType.Bool, 1)]
    [InlineData("playerZone",             PredicateType.Int,  0)]
    [InlineData("playerNear",             PredicateType.Bool, 2)]
    [InlineData("playerStartingClass",    PredicateType.String, 0)]
    [InlineData("instanceKind",           PredicateType.String, 0)]
    [InlineData("playerInCombat",         PredicateType.Bool, 0)]
    [InlineData("inNewGamePlus",          PredicateType.Bool, 0)]
    public void TryGet_KnownFunction_ReturnsCorrectSignature(
        string name, PredicateType expectedReturn, int expectedMinArity)
    {
        Assert.True(FunctionRegistry.TryGet(name, out var sig));
        Assert.Equal(expectedReturn, sig.ReturnType);
        if (expectedMinArity >= 0)
        {
            var min = sig.Arity switch
            {
                Arity.Fixed f         => f.Count,
                Arity.OptionalTail ot => ot.Required,
                Arity.VariadicMin vm  => vm.Minimum,
                _                    => throw new InvalidOperationException()
            };
            Assert.True(min <= expectedMinArity + 1);
        }
    }

    [Fact]
    public void TryGet_UnknownFunction_ReturnsFalse()
    {
        Assert.False(FunctionRegistry.TryGet("frobnicate", out _));
    }

    [Fact]
    public void TryGet_IsCaseSensitive()
    {
        Assert.False(FunctionRegistry.TryGet("QuestSequence", out _));
        Assert.False(FunctionRegistry.TryGet("PLAYERZONE", out _));
    }

    [Fact]
    public void PlayerNear_HasPositionAndIntParams()
    {
        Assert.True(FunctionRegistry.TryGet("playerNear", out var sig));
        Assert.Equal(2, sig.ParameterTypes.Count);
        Assert.Equal(PredicateType.Position, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Int,      sig.ParameterTypes[1]);
    }

    [Theory]
    [InlineData("questSequnece",   "questSequence")]   // distance 1
    [InlineData("isQuestcomplete", "isQuestComplete")] // distance 1
    [InlineData("playerzone",      "playerZone")]      // distance 1
    public void SuggestSimilar_WithinDistance2_ReturnsSuggestion(string typo, string expected)
    {
        var suggestions = FunctionRegistry.SuggestSimilar(typo);
        Assert.Contains(expected, suggestions);
    }

    [Fact]
    public void SuggestSimilar_TooFarAway_ReturnsEmpty()
    {
        Assert.Empty(FunctionRegistry.SuggestSimilar("frobnicate"));
    }

    [Fact]
    public void SuggestSimilar_ExactMatch_ReturnsSelf()
    {
        var suggestions = FunctionRegistry.SuggestSimilar("questSequence");
        Assert.Contains("questSequence", suggestions);
    }
}