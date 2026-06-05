using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class FunctionRegistryTests
{
    // =========================================================================
    // questVariableLow / questVariableHigh registry tests (RG-N* from NIBBLE_PREDICATES_PLAN.md)
    // RED: all fail until Builder adds the two nibble entries to FunctionRegistry.
    // =========================================================================

    [Fact]
    public void All_Contains41Functions()
    {
        Assert.Equal(41, FunctionRegistry.All.Count);
    }

    [Fact]
    public void QuestVariableLow_Signature_IsCorrect()
    {
        /*
         * RED: TryGet returns false (not registered yet).
         *
         * CONTRACT (RG-N1): Given FunctionRegistry.TryGet("questVariableLow", out var sig),
         *                   Then found == true,
         *                        sig.Name == "questVariableLow",
         *                        sig.Arity is Fixed(2),
         *                        sig.ParameterTypes == [Int, Int],
         *                        sig.ReturnType == Int.
         *
         * BUILDER GUIDANCE: new("questVariableLow", new Fixed(2), [Int, Int], Int)
         *   adjacent to the questVariable entry in s_functions.
         */
        var found = FunctionRegistry.TryGet("questVariableLow", out var sig);

        Assert.True(found, "questVariableLow should be registered in FunctionRegistry");
        Assert.Equal("questVariableLow", sig.Name);
        var fixed2 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(2, fixed2.Count);
        Assert.Equal(2, sig.ParameterTypes.Count);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[1]);
        Assert.Equal(PredicateType.Int, sig.ReturnType);
    }

    [Fact]
    public void QuestVariableHigh_Signature_IsCorrect()
    {
        /*
         * RED: TryGet returns false (not registered yet).
         *
         * CONTRACT (RG-N2): Given FunctionRegistry.TryGet("questVariableHigh", out var sig),
         *                   Then found == true,
         *                        sig.Name == "questVariableHigh",
         *                        sig.Arity is Fixed(2),
         *                        sig.ParameterTypes == [Int, Int],
         *                        sig.ReturnType == Int.
         */
        var found = FunctionRegistry.TryGet("questVariableHigh", out var sig);

        Assert.True(found, "questVariableHigh should be registered in FunctionRegistry");
        Assert.Equal("questVariableHigh", sig.Name);
        var fixed2 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(2, fixed2.Count);
        Assert.Equal(2, sig.ParameterTypes.Count);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[1]);
        Assert.Equal(PredicateType.Int, sig.ReturnType);
    }

    [Fact]
    public void QuestVariableLow_SuggestSimilar_SuggestsForTypo()
    {
        /*
         * RED: SuggestSimilar("questVariableLwo") returns empty (entry not registered yet).
         *
         * CONTRACT (RG-N4): Given the typo "questVariableLwo" (transposed 'w' and 'o'),
         *                   When FunctionRegistry.SuggestSimilar("questVariableLwo"),
         *                   Then the result contains "questVariableLow".
         *                   Levenshtein("questVariableLwo", "questVariableLow") == 2 ≤ maxDistance 2.
         */
        var suggestions = FunctionRegistry.SuggestSimilar("questVariableLwo");

        Assert.Contains("questVariableLow", suggestions);
    }

    [Fact]
    public void QuestVariableHigh_SuggestSimilar_ExactMatchReturnsSelf()
    {
        /*
         * RED: Entry not registered yet — SuggestSimilar returns empty.
         *
         * CONTRACT (RG-N5, first clause): SuggestSimilar("questVariableHigh") contains
         *   "questVariableHigh" (exact match distance 0 ≤ 2).
         */
        var suggestions = FunctionRegistry.SuggestSimilar("questVariableHigh");

        Assert.Contains("questVariableHigh", suggestions);
    }

    // =========================================================================
    // questVariable registry tests (RG1-RG3 from QUEST_VARIABLE_PREDICATE_PLAN.md)
    // =========================================================================

    [Fact]
    public void QuestVariable_Signature_IsCorrect()
    {
        /*
         * RED: Will fail until Builder adds the questVariable entry.
         *
         * CONTRACT (RG1): Given FunctionRegistry.TryGet("questVariable", out var sig),
         *                 Then found == true,
         *                      sig.Name == "questVariable",
         *                      sig.Arity is Fixed(2),
         *                      sig.ParameterTypes == [Int, Int],
         *                      sig.ReturnType == Int.
         *
         * BUILDER GUIDANCE: new("questVariable", new Fixed(2), [Int, Int], Int)
         *   — two Int parameters (questId, index), returns Int (the byte value widened).
         */

        // Act
        var found = FunctionRegistry.TryGet("questVariable", out var sig);

        // Assert
        Assert.True(found, "questVariable should be registered in FunctionRegistry");
        Assert.Equal("questVariable", sig.Name);
        var fixed2 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(2, fixed2.Count);
        Assert.Equal(2, sig.ParameterTypes.Count);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[1]);
        Assert.Equal(PredicateType.Int, sig.ReturnType);
    }

    [Fact]
    public void QuestVariable_SuggestSimilar_SuggestsForTypo()
    {
        /*
         * RED: Will fail until Builder adds the questVariable entry.
         *
         * CONTRACT (RG3): Given the typo "questVaraible" (transposed 'i' and 'a'),
         *                 When FunctionRegistry.SuggestSimilar("questVaraible"),
         *                 Then the result contains "questVariable".
         *
         * BUILDER GUIDANCE: Levenshtein distance between "questVaraible" and "questVariable"
         *   is 2, which is <= the default maxDistance of 2.
         */

        // Act
        var suggestions = FunctionRegistry.SuggestSimilar("questVaraible");

        // Assert
        Assert.Contains("questVariable", suggestions);
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

    [Fact]
    public void JobGearsetExists_Signature_IsCorrect_RGP5()
    {
        var found = FunctionRegistry.TryGet("jobGearsetExists", out var sig);

        Assert.True(found, "jobGearsetExists should be registered in FunctionRegistry");
        Assert.Equal("jobGearsetExists", sig.Name);
        var fixed1 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(1, fixed1.Count);
        Assert.Single(sig.ParameterTypes);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    // =========================================================================
    // inventoryHasCoffers registry test (OC-10 from OPEN_COFFERS_STEP_PLAN.md)
    // =========================================================================

    [Fact]
    public void InventoryHasCoffers_Signature_IsCorrect()
    {
        /*
         * CONTRACT (OC-10): Given FunctionRegistry.TryGet("inventoryHasCoffers", out var sig),
         *                   Then found == true,
         *                        sig.Name == "inventoryHasCoffers",
         *                        sig.Arity is Fixed(0),
         *                        sig.ParameterTypes is empty,
         *                        sig.ReturnType == Bool.
         */
        var found = FunctionRegistry.TryGet("inventoryHasCoffers", out var sig);

        Assert.True(found, "inventoryHasCoffers should be registered in FunctionRegistry");
        Assert.Equal("inventoryHasCoffers", sig.Name);
        var fixed0 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(0, fixed0.Count);
        Assert.Empty(sig.ParameterTypes);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    // =========================================================================
    // isAetherCurrentAttuned / npcExistsNearby registry tests (IO-TT1 / IO-TT2)
    // =========================================================================

    [Fact]
    public void IsAetherCurrentAttuned_Signature_IsCorrect_IOTT1()
    {
        /*
         * CONTRACT (IO-TT1): Given FunctionRegistry.TryGet("isAetherCurrentAttuned", out var sig),
         *                    Then found == true,
         *                         sig.Name == "isAetherCurrentAttuned",
         *                         sig.Arity is Fixed(1),
         *                         sig.ParameterTypes == [Int],
         *                         sig.ReturnType == Bool.
         */
        var found = FunctionRegistry.TryGet("isAetherCurrentAttuned", out var sig);

        Assert.True(found, "isAetherCurrentAttuned should be registered in FunctionRegistry");
        Assert.Equal("isAetherCurrentAttuned", sig.Name);
        var fixed1 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(1, fixed1.Count);
        Assert.Single(sig.ParameterTypes);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    [Fact]
    public void NpcExistsNearby_Signature_IsCorrect_IOTT2()
    {
        /*
         * CONTRACT (IO-TT2): Given FunctionRegistry.TryGet("npcExistsNearby", out var sig),
         *                    Then found == true,
         *                         sig.Name == "npcExistsNearby",
         *                         sig.Arity is Fixed(1),
         *                         sig.ParameterTypes == [Int],
         *                         sig.ReturnType == Bool.
         */
        var found = FunctionRegistry.TryGet("npcExistsNearby", out var sig);

        Assert.True(found, "npcExistsNearby should be registered in FunctionRegistry");
        Assert.Equal("npcExistsNearby", sig.Name);
        var fixed1 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(1, fixed1.Count);
        Assert.Single(sig.ParameterTypes);
        Assert.Equal(PredicateType.Int, sig.ParameterTypes[0]);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }
}