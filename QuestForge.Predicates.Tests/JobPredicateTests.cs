using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

/// <summary>
/// Tests for the three job-predicate functions: playerJobId() → Int,
/// isDiscipleOfWar() → Bool, isDiscipleOfMagic() → Bool.
///
/// RED PHASE: all tests in this file fail until the Builder adds the three
/// registry entries to FunctionRegistry.s_functions. No grammar or parser
/// changes are required — these are zero-arg calls which the parser already
/// handles.
/// </summary>
public class JobPredicateTests
{
    // ---- helpers (mirror PredicateCheckerTests) ---------------------------

    private static PredicateAst ParseMustSucceed(string source)
    {
        var r = PredicateParser.Parse(source);
        Assert.True(r.IsSuccess, $"Parse failed unexpectedly: {Fmt(r.Errors)}");
        return r.Ast!;
    }

    private static IReadOnlyList<ParseError> Semantic(string source)
        => PredicateChecker.Check(ParseMustSucceed(source));

    private static void AssertNoSemanticErrors(string source)
    {
        var errors = Semantic(source);
        Assert.True(errors.Count == 0, $"Expected no errors but got: {Fmt(errors)}");
    }

    private static void AssertSingleSemanticError(string source, string code)
    {
        var errors = Semantic(source);
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal(code, errors[0].Code);
    }

    private static string Fmt(IReadOnlyList<ParseError> errors)
        => string.Join("; ", errors.Select(e => $"[{e.Code}] {e.Message}"));

    // =========================================================================
    // Registry tests (RG-J*)
    // RED: TryGet returns false for all three until Builder adds the entries.
    // =========================================================================

    [Fact]
    public void PlayerJobId_Signature_IsCorrect()
    {
        /*
         * RED: TryGet returns false (not registered yet).
         *
         * CONTRACT (RG-J1): Given FunctionRegistry.TryGet("playerJobId", out var sig),
         *                   Then found == true,
         *                        sig.Name == "playerJobId",
         *                        sig.Arity is Fixed(0),
         *                        sig.ParameterTypes is empty,
         *                        sig.ReturnType == Int.
         *
         * BUILDER GUIDANCE: new("playerJobId", new Fixed(0), [], Int)
         *   adjacent to other zero-arg player-state entries (playerZone, playerInCombat, etc.).
         */
        var found = FunctionRegistry.TryGet("playerJobId", out var sig);

        Assert.True(found, "playerJobId should be registered in FunctionRegistry");
        Assert.Equal("playerJobId", sig.Name);
        var fixed0 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(0, fixed0.Count);
        Assert.Empty(sig.ParameterTypes);
        Assert.Equal(PredicateType.Int, sig.ReturnType);
    }

    [Fact]
    public void IsDiscipleOfWar_Signature_IsCorrect()
    {
        /*
         * RED: TryGet returns false (not registered yet).
         *
         * CONTRACT (RG-J2): Given FunctionRegistry.TryGet("isDiscipleOfWar", out var sig),
         *                   Then found == true,
         *                        sig.Name == "isDiscipleOfWar",
         *                        sig.Arity is Fixed(0),
         *                        sig.ParameterTypes is empty,
         *                        sig.ReturnType == Bool.
         *
         * BUILDER GUIDANCE: new("isDiscipleOfWar", new Fixed(0), [], Bool)
         */
        var found = FunctionRegistry.TryGet("isDiscipleOfWar", out var sig);

        Assert.True(found, "isDiscipleOfWar should be registered in FunctionRegistry");
        Assert.Equal("isDiscipleOfWar", sig.Name);
        var fixed0 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(0, fixed0.Count);
        Assert.Empty(sig.ParameterTypes);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    [Fact]
    public void IsDiscipleOfMagic_Signature_IsCorrect()
    {
        /*
         * RED: TryGet returns false (not registered yet).
         *
         * CONTRACT (RG-J3): Given FunctionRegistry.TryGet("isDiscipleOfMagic", out var sig),
         *                   Then found == true,
         *                        sig.Name == "isDiscipleOfMagic",
         *                        sig.Arity is Fixed(0),
         *                        sig.ParameterTypes is empty,
         *                        sig.ReturnType == Bool.
         *
         * BUILDER GUIDANCE: new("isDiscipleOfMagic", new Fixed(0), [], Bool)
         */
        var found = FunctionRegistry.TryGet("isDiscipleOfMagic", out var sig);

        Assert.True(found, "isDiscipleOfMagic should be registered in FunctionRegistry");
        Assert.Equal("isDiscipleOfMagic", sig.Name);
        var fixed0 = Assert.IsType<Arity.Fixed>(sig.Arity);
        Assert.Equal(0, fixed0.Count);
        Assert.Empty(sig.ParameterTypes);
        Assert.Equal(PredicateType.Bool, sig.ReturnType);
    }

    [Theory]
    [InlineData("playerJobId",      PredicateType.Int)]
    [InlineData("isDiscipleOfWar",  PredicateType.Bool)]
    [InlineData("isDiscipleOfMagic",PredicateType.Bool)]
    public void AllThree_TryGet_ReturnsCorrectReturnType(string name, PredicateType expectedReturn)
    {
        /*
         * RED: All three TryGet calls return false (not registered yet).
         *
         * CONTRACT (RG-J4): Each of the three functions TryGet returns true with the
         *                   expected return type.
         */
        Assert.True(FunctionRegistry.TryGet(name, out var sig),
            $"{name} should be registered in FunctionRegistry");
        Assert.Equal(expectedReturn, sig.ReturnType);
    }

    [Fact]
    public void PlayerJobId_SuggestSimilar_SuggestsForTypo()
    {
        /*
         * RED: SuggestSimilar("playerJobid") returns empty (not registered yet).
         *
         * CONTRACT (RG-J5): Given typo "playerJobid" (lowercase 'i'),
         *                   When FunctionRegistry.SuggestSimilar("playerJobid"),
         *                   Then the result contains "playerJobId".
         *                   Levenshtein("playerJobid", "playerJobId") == 1 ≤ maxDistance 2.
         */
        var suggestions = FunctionRegistry.SuggestSimilar("playerJobid");

        Assert.Contains("playerJobId", suggestions);
    }

    [Fact]
    public void IsDiscipleOfWar_SuggestSimilar_SuggestsForTypo()
    {
        /*
         * RED: Not registered yet.
         *
         * CONTRACT (RG-J6): Given typo "isDiscipleofWar" (lowercase 'o'),
         *                   When FunctionRegistry.SuggestSimilar("isDiscipleofWar"),
         *                   Then the result contains "isDiscipleOfWar".
         *                   Levenshtein distance == 1.
         */
        var suggestions = FunctionRegistry.SuggestSimilar("isDiscipleofWar");

        Assert.Contains("isDiscipleOfWar", suggestions);
    }

    // =========================================================================
    // Checker — valid (CK-J-V*)
    // RED: all fail with "unknown-function" until the registry entries exist.
    // =========================================================================

    [Fact]
    public void PlayerJobId_ComparedToInt_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V1): "playerJobId() == 19" → no semantic errors.
         *                     playerJobId() returns Int; comparing Int == Int is valid.
         *                     Mirrors playerZone() == 132 (existing zero-arg Int function).
         */
        AssertNoSemanticErrors("playerJobId() == 19");
    }

    [Fact]
    public void PlayerJobId_RelationalComparison_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V2): "playerJobId() >= 1" → no semantic errors.
         *                     Relational operators are valid for Int.
         */
        AssertNoSemanticErrors("playerJobId() >= 1");
    }

    [Fact]
    public void PlayerJobId_InOrExpression_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V3): "playerJobId() == 1 or playerJobId() == 2" → no semantic errors.
         *                     Two Int comparisons joined with or.
         */
        AssertNoSemanticErrors("playerJobId() == 1 or playerJobId() == 2");
    }

    [Fact]
    public void IsDiscipleOfWar_StandaloneBoolean_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V4): "isDiscipleOfWar()" → no semantic errors.
         *                     A zero-arg Bool function is a valid top-level predicate
         *                     (mirrors playerInCombat() which also returns Bool).
         */
        AssertNoSemanticErrors("isDiscipleOfWar()");
    }

    [Fact]
    public void IsDiscipleOfMagic_StandaloneBoolean_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V5): "isDiscipleOfMagic()" → no semantic errors.
         *                     Same parity as isDiscipleOfWar().
         */
        AssertNoSemanticErrors("isDiscipleOfMagic()");
    }

    [Fact]
    public void IsDiscipleOfWar_NegatedWithNot_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V6): "not(isDiscipleOfWar())" → no semantic errors.
         *                     not() wraps any Bool expression.
         */
        AssertNoSemanticErrors("not(isDiscipleOfWar())");
    }

    [Fact]
    public void IsDiscipleOfMagic_NegatedWithNot_NoError()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-V7): "not(isDiscipleOfMagic())" → no semantic errors.
         */
        AssertNoSemanticErrors("not(isDiscipleOfMagic())");
    }

    [Fact]
    public void IsDiscipleOfWar_AndIsDiscipleOfMagic_NoError()
    {
        /*
         * RED: Fails with unknown-function until both entries registered.
         *
         * CONTRACT (CK-J-V8): "isDiscipleOfWar() and isDiscipleOfMagic()" → no semantic errors.
         *                     Both sides are Bool; and is valid.
         *                     (Logically impossible but syntactically/semantically valid.)
         */
        AssertNoSemanticErrors("isDiscipleOfWar() and isDiscipleOfMagic()");
    }

    [Fact]
    public void PlayerJobId_WithIsDiscipleOfWar_OrExpression_NoError()
    {
        /*
         * RED: Fails with unknown-function until entries registered.
         *
         * CONTRACT (CK-J-V9): "playerJobId() == 1 or isDiscipleOfWar()" → no semantic errors.
         *                     Left branch is Bool (Int == Int comparison), right is Bool;
         *                     or composes two Bools.
         */
        AssertNoSemanticErrors("playerJobId() == 1 or isDiscipleOfWar()");
    }

    [Fact]
    public void IsDiscipleOfWar_AndQuestFlag_NoError()
    {
        /*
         * RED: Fails with unknown-function for isDiscipleOfWar until entry registered.
         *
         * CONTRACT (CK-J-V10): "isDiscipleOfWar() and questFlag(65, 1)" → no semantic errors.
         *                      Combining new Bool function with existing Bool function.
         */
        AssertNoSemanticErrors("isDiscipleOfWar() and questFlag(65, 1)");
    }

    // =========================================================================
    // Checker — invalid (CK-J-E*)
    // RED: all fail with wrong error count or wrong code until registry entries exist.
    //
    // Note on "Bool == Int" (CK-J-E5): CheckComparison infers left=Bool, right=Int,
    // left != right → emits "type-mismatch" ("cannot compare Bool to Int").
    // This mirrors the existing test Comparison_BoolToInt_ReportsError
    // ("isQuestComplete(65) == 1" → "type-mismatch"). Confirmed from PredicateChecker.cs
    // line 131-134.
    //
    // Note on bare Int (CK-J-E1): Check() infers type=Int, type != Bool → emits
    // "type-mismatch" ("bare predicate expression must be Bool; got Int"). Code: "type-mismatch".
    // Mirrors BareExpression_IntReturn_ReportsTypeMismatch test for questSequence(65).
    // =========================================================================

    [Fact]
    public void PlayerJobId_BareWithoutComparison_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function (count 1, wrong code) until entry registered;
         *      then passes (type-mismatch fires as expected).
         *
         * CONTRACT (CK-J-E1): "playerJobId()" used bare (no comparison) → exactly one
         *                     "type-mismatch" ("bare predicate expression must be Bool; got Int").
         *                     Mirrors questSequence(65) bare behaviour.
         */
        AssertSingleSemanticError("playerJobId()", "type-mismatch");
    }

    [Fact]
    public void PlayerJobId_WithOneArg_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered;
         *      then fails with "arity-mismatch" as expected.
         *
         * CONTRACT (CK-J-E2): "playerJobId(1) == 19" → exactly one "arity-mismatch"
         *                     ("function 'playerJobId' requires exactly 0 argument(s); got 1").
         *                     Mirrors Arity_ZeroArg_WithArg_ReportsError for playerZone.
         */
        AssertSingleSemanticError("playerJobId(1) == 19", "arity-mismatch");
    }

    [Fact]
    public void PlayerJobId_ComparedToString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered;
         *      then fails with "type-mismatch" as expected.
         *
         * CONTRACT (CK-J-E3): "playerJobId() == \"WAR\"" → exactly one "type-mismatch"
         *                     ("cannot compare Int to String").
         *                     CheckComparison sees left=Int, right=String, left != right.
         */
        AssertSingleSemanticError("playerJobId() == \"WAR\"", "type-mismatch");
    }

    [Fact]
    public void IsDiscipleOfWar_ComparedToInt_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered;
         *      then fails with "type-mismatch" as expected.
         *
         * CONTRACT (CK-J-E4): "isDiscipleOfWar() == 1" → exactly one "type-mismatch"
         *                     ("cannot compare Bool to Int").
         *                     CheckComparison: left=Bool, right=Int, left != right → error.
         *                     Mirrors Comparison_BoolToInt_ReportsError (isQuestComplete(65) == 1).
         */
        AssertSingleSemanticError("isDiscipleOfWar() == 1", "type-mismatch");
    }

    [Fact]
    public void IsDiscipleOfMagic_ComparedToInt_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-E5): "isDiscipleOfMagic() == 1" → exactly one "type-mismatch".
         *                     Same as CK-J-E4 for the sister function.
         */
        AssertSingleSemanticError("isDiscipleOfMagic() == 1", "type-mismatch");
    }

    [Fact]
    public void IsDiscipleOfWar_WithOneArg_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-E6): "isDiscipleOfWar(1)" → exactly one "arity-mismatch"
         *                     ("function 'isDiscipleOfWar' requires exactly 0 argument(s); got 1").
         */
        AssertSingleSemanticError("isDiscipleOfWar(1)", "arity-mismatch");
    }

    [Fact]
    public void IsDiscipleOfMagic_WithOneArg_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until entry registered.
         *
         * CONTRACT (CK-J-E7): "isDiscipleOfMagic(1)" → exactly one "arity-mismatch".
         */
        AssertSingleSemanticError("isDiscipleOfMagic(1)", "arity-mismatch");
    }

    [Fact]
    public void PlayerJobId_Typo_ReportsUnknownFunctionWithSuggestion()
    {
        /*
         * RED: unknown-function fires immediately (entry absent) BUT the suggestion
         *      assertion fails — SuggestSimilar returns nothing for an unregistered name.
         *      Once registered the suggestion becomes available.
         *
         * CONTRACT (CK-J-E8): "playerJobid() == 19" → exactly one "unknown-function"
         *                     with a suggestion containing "playerJobId".
         *                     Levenshtein("playerJobid", "playerJobId") == 1.
         */
        var errors = Semantic("playerJobid() == 19");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("playerJobId", errors[0].Suggestion);
    }

    [Fact]
    public void IsDiscipleOfWar_Typo_ReportsUnknownFunctionWithSuggestion()
    {
        /*
         * RED: suggestion assertion fails until entry registered.
         *
         * CONTRACT (CK-J-E9): "isDiscipleofWar()" → exactly one "unknown-function"
         *                     with a suggestion containing "isDiscipleOfWar".
         *                     Levenshtein("isDiscipleofWar", "isDiscipleOfWar") == 1.
         */
        var errors = Semantic("isDiscipleofWar()");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("isDiscipleOfWar", errors[0].Suggestion);
    }
}
