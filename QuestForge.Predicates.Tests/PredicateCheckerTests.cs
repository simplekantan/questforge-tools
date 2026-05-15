using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

/// <summary>
/// Tests for PredicateChecker — semantic validation over a parsed AST.
/// Each test parses first (must succeed), then calls PredicateChecker.Check.
/// Parse errors are a precondition failure, not the subject under test here.
/// </summary>
public class PredicateCheckerTests
{
    // ---- helpers ---------------------------------------------------------

    private static PredicateAst ParseMustSucceed(string source, IFragmentParameterScope? scope = null)
    {
        var r = PredicateParser.Parse(source, scope);
        Assert.True(r.IsSuccess, $"Parse failed unexpectedly: {Fmt(r.Errors)}");
        return r.Ast!;
    }

    private static IReadOnlyList<ParseError> Semantic(string source, IFragmentParameterScope? scope = null)
        => PredicateChecker.Check(ParseMustSucceed(source, scope), scope);

    private static void AssertSingleSemanticError(string source, string code, IFragmentParameterScope? scope = null)
    {
        var errors = Semantic(source, scope);
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal(code, errors[0].Code);
    }

    private static void AssertNoSemanticErrors(string source, IFragmentParameterScope? scope = null)
    {
        var errors = Semantic(source, scope);
        Assert.True(errors.Count == 0, $"Expected no errors but got: {Fmt(errors)}");
    }

    private static string Fmt(IReadOnlyList<ParseError> errors)
        => string.Join("; ", errors.Select(e => $"[{e.Code}] {e.Message}"));

    // ---- §4.5 unknown-function -------------------------------------------

    [Fact]
    public void KnownFunction_ProducesNoError()
        => AssertNoSemanticErrors("questSequence(65) >= 3");

    [Fact]
    public void UnknownFunction_Typo_ReportsExactlyOneErrorWithSuggestion()
    {
        var errors = Semantic("questSequnece(65) >= 3");
        Assert.Equal(1, errors.Count);
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("questSequence", errors[0].Suggestion);
    }

    [Fact]
    public void UnknownFunction_CaseWrong_ReportsExactlyOneErrorWithSuggestion()
    {
        var errors = Semantic("isQuestcomplete(65)");
        Assert.Equal(1, errors.Count);
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.Contains("isQuestComplete", errors[0].Suggestion ?? "");
    }

    [Fact]
    public void UnknownFunction_TooFarAway_ReportsExactlyOneErrorWithNoSuggestion()
    {
        var errors = Semantic("frobnicate(1) >= 0");
        Assert.Equal(1, errors.Count);
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.Null(errors[0].Suggestion);
    }

    [Fact]
    public void BareIdentifier_ReportsExactlyOneUnknownFunctionError()
    {
        var errors = Semantic("Gladiator");
        Assert.Equal(1, errors.Count);
        Assert.Equal("unknown-function", errors[0].Code);
    }

    [Fact]
    public void UnknownFunction_InCompoundExpression_OnlyErrorsForThatPart()
    {
        // Left side has typo; right side (questFlag) is valid.
        // Checker must walk both sides — not short-circuit after the first error.
        var errors = Semantic("questSequnece(65) >= 3 and questFlag(65, 1)");
        Assert.Equal(1, errors.Count);
        Assert.Equal("unknown-function", errors[0].Code);
    }

    // ---- §4.6 arity-mismatch --------------------------------------------

    [Fact]
    public void Arity_Fixed_TooFewArgs_ReportsError()
        => AssertSingleSemanticError("questFlag(65)", "arity-mismatch");

    [Fact]
    public void Arity_Fixed_TooManyArgs_ReportsError()
        => AssertSingleSemanticError("questSequence(65, 1) >= 3", "arity-mismatch");

    [Fact]
    public void Arity_Fixed_Correct_NoError()
        => AssertNoSemanticErrors("questFlag(65, 1)");

    [Fact]
    public void Arity_VariadicMin_TooFew_ReportsError()
        => AssertSingleSemanticError("questFlagAny(65)", "arity-mismatch");

    [Fact]
    public void Arity_VariadicMin_AtMinimum_NoError()
        => AssertNoSemanticErrors("questFlagAny(65, 1)");

    [Fact]
    public void Arity_VariadicMin_ManyArgs_NoError()
        => AssertNoSemanticErrors("questFlagAny(65, 1, 2, 3, 4, 5)");

    [Fact]
    public void Arity_OptionalTail_OnlyRequired_NoError()
        => AssertNoSemanticErrors("playerHasItem(123)");

    [Fact]
    public void Arity_OptionalTail_WithOptional_NoError()
        => AssertNoSemanticErrors("playerHasItem(123, 5)");

    [Fact]
    public void Arity_OptionalTail_TooMany_ReportsError()
        => AssertSingleSemanticError("playerHasItem(123, 5, 7)", "arity-mismatch");

    [Fact]
    public void Arity_ZeroArg_WithArg_ReportsError()
        => AssertSingleSemanticError("playerZone(132) == 1", "arity-mismatch");

    [Fact]
    public void Arity_OptionalAll_NoArgs_NoError()
        => AssertNoSemanticErrors("playerLevel() >= 1");

    [Fact]
    public void Arity_OptionalAll_WithArg_NoError()
        => AssertNoSemanticErrors("playerLevel(\"Gladiator\") >= 1");

    // ---- §4.7 type-mismatch ---------------------------------------------

    [Fact]
    public void ArgType_StringWhereIntExpected_ReportsError()
        => AssertSingleSemanticError("questSequence(\"65\") >= 3", "type-mismatch");

    [Fact]
    public void Comparison_IntToString_ReportsError()
        => AssertSingleSemanticError("questSequence(65) >= \"three\"", "type-mismatch");

    [Fact]
    public void Comparison_BoolToInt_ReportsError()
        => AssertSingleSemanticError("isQuestComplete(65) == 1", "type-mismatch");

    // Note: the plan GWT "isQuestComplete(65) > isQuestAccepted(65)" cannot parse
    // because the grammar restricts RhsLiteral to number | string — function calls
    // on the RHS are not valid. The relational-on-Bool rule is effectively unreachable
    // in v1. Covered by the Bool-to-Int mismatch test instead.
    [Fact]
    public void Comparison_BoolToIntRelational_ReportsError()
        => AssertSingleSemanticError("isQuestComplete(65) > 1", "type-mismatch");

    [Fact]
    public void Comparison_RelationalOnString_ReportsError()
        => AssertSingleSemanticError("currentJob() > \"Gladiator\"", "type-mismatch");

    [Fact]
    public void Comparison_StringEq_NoError()
        => AssertNoSemanticErrors("currentJob() != \"Pugilist\"");

    [Fact]
    public void Comparison_IntEq_NoError()
        => AssertNoSemanticErrors("questSequence(65) == 3");

    [Fact]
    public void BareExpression_IntReturn_ReportsTypeMismatch()
        => AssertSingleSemanticError("questSequence(65)", "type-mismatch");

    [Fact]
    public void BareExpression_BoolReturn_NoError()
        => AssertNoSemanticErrors("isQuestComplete(65)");

    [Fact]
    public void ArgType_IntWherePositionExpected_ReportsError()
        // playerNear returns Bool; bare Bool is fine at root — only 1 error (arg type)
        => AssertSingleSemanticError("playerNear(132, 5)", "type-mismatch");

    [Fact]
    public void PlayerNear_ValidPositionAndRadius_NoError()
        => AssertNoSemanticErrors("playerNear({x:1,y:2,z:3}, 5)");

    // ---- §4.8 default-not-composable ------------------------------------

    [Fact]
    public void Default_Alone_NoError()
        => AssertNoSemanticErrors("default");

    [Fact]
    public void Default_InAnd_Left_ReportsError()
        => AssertSingleSemanticError("default and questFlag(65, 1)", "default-not-composable");

    [Fact]
    public void Default_InAnd_Right_ReportsError()
        => AssertSingleSemanticError("questFlag(65, 1) and default", "default-not-composable");

    [Fact]
    public void Default_InOr_ReportsError()
        => AssertSingleSemanticError("default or questFlag(65, 1)", "default-not-composable");

    // ---- §4.9 fragment parameter types ----------------------------------

    private static IFragmentParameterScope FragScope(params (string name, PredicateType type)[] ps)
        => new TestScope(ps);

    [Fact]
    public void FragmentParam_PositionUsedWherePositionExpected_NoError()
    {
        var scope = FragScope(("finalPosition", PredicateType.Position));
        AssertNoSemanticErrors("playerNear(${finalPosition}, 3)", scope);
    }

    [Fact]
    public void FragmentParam_IntUsedWhereIntExpected_NoError()
    {
        var scope = FragScope(("count", PredicateType.Int));
        AssertNoSemanticErrors("playerHasItem(${count}, 1)", scope);
    }

    [Fact]
    public void FragmentParam_IntUsedWherePositionExpected_ReportsError()
    {
        // ${count} has type Int, playerNear arg 0 expects Position
        var scope = FragScope(("count", PredicateType.Int));
        AssertSingleSemanticError("playerNear(${count}, 3)", "type-mismatch", scope);
    }
}