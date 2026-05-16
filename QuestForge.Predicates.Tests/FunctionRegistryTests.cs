using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class FunctionRegistryTests
{
    [Fact]
    public void All_Contains28Functions()
    {
        // Updated from 27 to 28 for Phase 11B: isAttuned added.
        Assert.Equal(28, FunctionRegistry.All.Count);
    }

    [Fact]
    public void IsAttuned_Signature_IsCorrect()
    {
        /*
         * RED: Will fail until Builder adds the isAttuned entry.
         *
         * CONTRACT: Given FunctionRegistry.TryGet("isAttuned", out var sig),
         *           Then sig.Name == "isAttuned"
         *                sig.Arity is Fixed(1)
         *                sig.ParameterTypes is [Int]
         *                sig.ReturnType is Bool
         *
         * BUILDER GUIDANCE: new("isAttuned", new Fixed(1), [Int], Bool)
         *   — one Int parameter (the aetheryte ID), returns Bool.
         */

        // Act
        var found = FunctionRegistry.TryGet("isAttuned", out var sig);

        // Assert
        Assert.True(found, "isAttuned should be registered in FunctionRegistry");
        Assert.Equal("isAttuned", sig.Name);
        var fixed1 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(1, fixed1.Count);
        Assert.Single(sig.ParameterTypes);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    [Fact]
    public void IsAttuned_Parser_AcceptsValidCall()
    {
        /*
         * RED: Will fail until Builder adds the isAttuned entry (parser uses registry for validation).
         *
         * CONTRACT: Given the input string "isAttuned(53)",
         *           When PredicateParser.Parse is called,
         *           Then IsSuccess == true
         *                Ast is a FunctionCall("isAttuned", [IntLiteral(53)]).
         *
         * BUILDER GUIDANCE: PredicateParser validates function names/arity against FunctionRegistry.
         *   Once isAttuned is registered, "isAttuned(53)" should parse successfully.
         */

        // Act
        var result = PredicateParser.Parse("isAttuned(53)");

        // Assert
        Assert.True(result.IsSuccess, $"Parse should succeed. Errors: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal("isAttuned", call.Name);
        Assert.Single(call.Args);
        var arg = Assert.IsType<PredicateAst.IntLiteral>(call.Args[0]);
        Assert.Equal(53L, arg.Value);
    }

    [Fact]
    public void IsAttuned_SuggestSimilar_SuggestsForTypo()
    {
        /*
         * RED: Will fail until Builder adds the isAttuned entry.
         *
         * CONTRACT: Given the typo "isAtuned" (one 't' missing),
         *           When FunctionRegistry.SuggestSimilar("isAtuned"),
         *           Then the result includes "isAttuned".
         *
         * BUILDER GUIDANCE: Levenshtein distance between "isAtuned" and "isAttuned" is 1,
         *   which is <= the default maxDistance of 2. The suggestion should appear.
         */

        // Act
        var suggestions = FunctionRegistry.SuggestSimilar("isAtuned");

        // Assert
        Assert.Contains("isAttuned", suggestions);
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