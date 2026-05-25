using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class ParserTests
{
    // ------------------------------------------------------------------ //
    // Happy path — plan §4.2
    // ------------------------------------------------------------------ //

    [Fact]
    public void Parse_QuestSequenceGtEq3()
    {
        // "questSequence(65) >= 3"
        var result = PredicateParser.Parse("questSequence(65) >= 3");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var cmp = Assert.IsType<PredicateAst.Comparison>(result.Ast);
        Assert.Equal(ComparisonOp.GtEq, cmp.Op);
        var call = Assert.IsType<PredicateAst.FunctionCall>(cmp.Left);
        Assert.Equal("questSequence", call.Name);
        Assert.Single(call.Args);
        Assert.Equal(new PredicateAst.IntLiteral(65), call.Args[0]);
        Assert.Equal(new PredicateAst.IntLiteral(3), cmp.Right);
    }

    [Fact]
    public void Parse_PlayerZoneEqAndNotPlayerInCombat()
    {
        // "playerZone() == 155 and not playerInCombat()"
        var result = PredicateParser.Parse("playerZone() == 155 and not playerInCombat()");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var and = Assert.IsType<PredicateAst.And>(result.Ast);

        var left = Assert.IsType<PredicateAst.Comparison>(and.Left);
        Assert.Equal(ComparisonOp.Eq, left.Op);
        var leftCall = Assert.IsType<PredicateAst.FunctionCall>(left.Left);
        Assert.Equal("playerZone", leftCall.Name);
        Assert.Empty(leftCall.Args);
        Assert.Equal(new PredicateAst.IntLiteral(155), left.Right);

        var not = Assert.IsType<PredicateAst.Not>(and.Right);
        var rightCall = Assert.IsType<PredicateAst.FunctionCall>(not.Inner);
        Assert.Equal("playerInCombat", rightCall.Name);
        Assert.Empty(rightCall.Args);
    }

    [Fact]
    public void Parse_QuestFlagAll_VariadicArgs()
    {
        // "questFlagAll(65, 1, 2, 3)"
        var result = PredicateParser.Parse("questFlagAll(65, 1, 2, 3)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal("questFlagAll", call.Name);
        Assert.Equal(4, call.Args.Count);
        Assert.Equal(new PredicateAst.IntLiteral(65), call.Args[0]);
        Assert.Equal(new PredicateAst.IntLiteral(1),  call.Args[1]);
        Assert.Equal(new PredicateAst.IntLiteral(2),  call.Args[2]);
        Assert.Equal(new PredicateAst.IntLiteral(3),  call.Args[3]);
    }

    [Fact]
    public void Parse_QuestFlagAny_Or_QuestSequence()
    {
        // "questFlagAny(65, 5, 6, 7) or questSequence(65) >= 5"
        var result = PredicateParser.Parse("questFlagAny(65, 5, 6, 7) or questSequence(65) >= 5");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var or = Assert.IsType<PredicateAst.Or>(result.Ast);

        var left = Assert.IsType<PredicateAst.FunctionCall>(or.Left);
        Assert.Equal("questFlagAny", left.Name);
        Assert.Equal(4, left.Args.Count);

        var right = Assert.IsType<PredicateAst.Comparison>(or.Right);
        Assert.Equal(ComparisonOp.GtEq, right.Op);
        Assert.Equal(new PredicateAst.IntLiteral(5), right.Right);
    }

    [Fact]
    public void Parse_PlayerStartingClass_StringComparison()
    {
        // "playerStartingClass() == \"Gladiator\""
        var result = PredicateParser.Parse("playerStartingClass() == \"Gladiator\"");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var cmp = Assert.IsType<PredicateAst.Comparison>(result.Ast);
        Assert.Equal(ComparisonOp.Eq, cmp.Op);
        var call = Assert.IsType<PredicateAst.FunctionCall>(cmp.Left);
        Assert.Equal("playerStartingClass", call.Name);
        Assert.Equal(new PredicateAst.StringLiteral("Gladiator"), cmp.Right);
    }

    [Fact]
    public void Parse_Default()
    {
        var result = PredicateParser.Parse("default");
        Assert.True(result.IsSuccess, FormatErrors(result));
        Assert.IsType<PredicateAst.DefaultLiteral>(result.Ast);
    }

    [Fact]
    public void Parse_PlayerNear_WithPositionAndRadius()
    {
        // playerNear({x:6.91, y:-1.92, z:47.29}, 5.0)
        // Note: position keys are bare identifiers (not strings in predicate syntax)
        var result = PredicateParser.Parse("playerNear({x:6.91,y:-1.92,z:47.29}, 5.0)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal("playerNear", call.Name);
        Assert.Equal(2, call.Args.Count);

        var pos = Assert.IsType<PredicateAst.PositionLiteral>(call.Args[0]);
        Assert.Equal(6.91f,  pos.X, 3);
        Assert.Equal(-1.92f, pos.Y, 3);
        Assert.Equal(47.29f, pos.Z, 3);

        // 5.0 is whole number, so treated as IntLiteral(5)
        Assert.Equal(new PredicateAst.IntLiteral(5), call.Args[1]);
    }

    // ------------------------------------------------------------------ //
    // Precedence — plan §4.3
    // ------------------------------------------------------------------ //

    [Fact]
    public void Precedence_AndTighterThanOr()
    {
        // "isQuestComplete(1) and isQuestAccepted(1) or playerInCombat()"
        // → Or(And(isQuestComplete, isQuestAccepted), playerInCombat)
        var result = PredicateParser.Parse("isQuestComplete(1) and isQuestAccepted(1) or playerInCombat()");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var or = Assert.IsType<PredicateAst.Or>(result.Ast);
        var and = Assert.IsType<PredicateAst.And>(or.Left);
        Assert.IsType<PredicateAst.FunctionCall>(and.Left);
        Assert.IsType<PredicateAst.FunctionCall>(and.Right);
        Assert.IsType<PredicateAst.FunctionCall>(or.Right);
    }

    [Fact]
    public void Precedence_NotTighterThanAnd()
    {
        // "not playerInCombat() and isQuestComplete(1)"
        // → And(Not(playerInCombat), isQuestComplete)
        var result = PredicateParser.Parse("not playerInCombat() and isQuestComplete(1)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var and = Assert.IsType<PredicateAst.And>(result.Ast);
        var not = Assert.IsType<PredicateAst.Not>(and.Left);
        Assert.IsType<PredicateAst.FunctionCall>(not.Inner);
        Assert.IsType<PredicateAst.FunctionCall>(and.Right);
    }

    [Fact]
    public void Precedence_GroupingOverridesDefault()
    {
        // "not (playerInCombat() and isQuestComplete(1))"
        // → Not(And(...))
        var result = PredicateParser.Parse("not (playerInCombat() and isQuestComplete(1))");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var not = Assert.IsType<PredicateAst.Not>(result.Ast);
        Assert.IsType<PredicateAst.And>(not.Inner);
    }

    [Fact]
    public void Precedence_TwoComparisons_CombinedWithAnd()
    {
        // "playerZone() == 1 and questSequence(1) >= 2"
        // → And(Comparison(...), Comparison(...))
        var result = PredicateParser.Parse("playerZone() == 1 and questSequence(1) >= 2");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var and = Assert.IsType<PredicateAst.And>(result.Ast);
        Assert.IsType<PredicateAst.Comparison>(and.Left);
        Assert.IsType<PredicateAst.Comparison>(and.Right);
    }

    // ------------------------------------------------------------------ //
    // Error recovery — plan §4.4
    // ------------------------------------------------------------------ //

    [Fact]
    public void ErrorRecovery_EmptyArg_StillParsesSecondCall()
    {
        // "questFlag(65, ) and questFlag(65, 2)"
        // Parser recovers at 'and' — second call must appear in the AST
        var result = PredicateParser.Parse("questFlag(65, ) and questFlag(65, 2)");
        Assert.NotEmpty(result.Errors);
        // Recovery: the And node must exist with the second questFlag on the right
        var and = Assert.IsType<PredicateAst.And>(result.Ast);
        var right = Assert.IsType<PredicateAst.FunctionCall>(and.Right);
        Assert.Equal("questFlag", right.Name);
        Assert.Equal(2, right.Args.Count);
        Assert.Equal(new PredicateAst.IntLiteral(2), right.Args[1]);
    }

    [Fact]
    public void ErrorRecovery_EmptyInput_NullAst()
    {
        var result = PredicateParser.Parse("");
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Ast);
    }

    [Fact]
    public void ErrorRecovery_WhitespaceOnly_NullAst()
    {
        var result = PredicateParser.Parse("   ");
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Ast);
    }

    // ------------------------------------------------------------------ //
    // Position literal details
    // ------------------------------------------------------------------ //

    [Fact]
    public void PositionLiteral_NegativeY_Works()
    {
        // "playerNear({x:1,y:-2,z:3}, 5)"
        var result = PredicateParser.Parse("playerNear({x:1,y:-2,z:3}, 5)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        var pos = Assert.IsType<PredicateAst.PositionLiteral>(call.Args[0]);
        Assert.Equal(1f,  pos.X, 3);
        Assert.Equal(-2f, pos.Y, 3);
        Assert.Equal(3f,  pos.Z, 3);
    }

    [Fact]
    public void PositionLiteral_AnyKeyOrder_Works()
    {
        // Keys can be in any order
        var result = PredicateParser.Parse("playerNear({z:3,x:1,y:2}, 5)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        var pos = Assert.IsType<PredicateAst.PositionLiteral>(call.Args[0]);
        Assert.Equal(1f, pos.X, 3);
        Assert.Equal(2f, pos.Y, 3);
        Assert.Equal(3f, pos.Z, 3);
    }

    // ------------------------------------------------------------------ //
    // playerNear radius rules
    // ------------------------------------------------------------------ //

    [Fact]
    public void PlayerNear_WholeFloatRadius_ProducesIntLiteral()
    {
        // 5.0 is a whole number — should produce IntLiteral(5)
        var result = PredicateParser.Parse("playerNear({x:1,y:2,z:3}, 5.0)");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal(new PredicateAst.IntLiteral(5), call.Args[1]);
    }

    [Fact]
    public void PlayerNear_NonWholeFloatRadius_ProducesError()
    {
        // 2.5 is not a whole number — parse error
        var result = PredicateParser.Parse("playerNear({x:1,y:2,z:3}, 2.5)");
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "parse-error");
    }

    // ------------------------------------------------------------------ //
    // Parameter references
    // ------------------------------------------------------------------ //

    [Fact]
    public void ParameterRef_WithNullScope_ProducesError()
    {
        // "${pos}" with null scope → parse error
        var result = PredicateParser.Parse("${pos}", null);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "parse-error");
    }

    [Fact]
    public void ParameterRef_WithScope_KnownParam_ProducesParameterRef()
    {
        var scope = new TestScope(("pos", PredicateType.Position));
        var result = PredicateParser.Parse("${pos}", scope);
        Assert.True(result.IsSuccess, FormatErrors(result));
        var paramRef = Assert.IsType<PredicateAst.ParameterRef>(result.Ast);
        Assert.Equal("pos", paramRef.Name);
    }

    [Fact]
    public void ParameterRef_WithScope_UnknownParam_ProducesUnknownParameterError()
    {
        var scope = new TestScope(("pos", PredicateType.Position));
        var result = PredicateParser.Parse("${missing}", scope);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "unknown-parameter");
    }

    // ------------------------------------------------------------------ //
    // Bare identifier (no parentheses) → FunctionCall with empty args
    // ------------------------------------------------------------------ //

    [Fact]
    public void BareIdentifier_ProducesFunctionCallWithNoArgs()
    {
        // An identifier without () is treated as FunctionCall(name, [])
        var result = PredicateParser.Parse("playerInCombat");
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal("playerInCombat", call.Name);
        Assert.Empty(call.Args);
    }

    // ------------------------------------------------------------------ //
    // Unary minus outside position literal keys → parse error (plan §3.1)
    // ------------------------------------------------------------------ //

    [Fact]
    public void UnaryMinus_OnRhsOfComparison_ProducesError()
    {
        // "questSequence(65) >= -3" — minus only valid inside position literal keys
        var result = PredicateParser.Parse("questSequence(65) >= -3");
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "parse-error");
    }

    [Fact]
    public void UnaryMinus_InFunctionArg_ProducesError()
    {
        var result = PredicateParser.Parse("questSequence(-3)");
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "parse-error");
    }

    // ------------------------------------------------------------------ //
    // Float literals in non-playerNear functions produce error
    // ------------------------------------------------------------------ //

    [Fact]
    public void FloatLiteral_InOtherFunction_ProducesError()
    {
        // questSequence takes Int; float arg should be a parse error
        var result = PredicateParser.Parse("questSequence(1.5)");
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code == "parse-error");
    }

    // ------------------------------------------------------------------ //
    // Comparison operators — all six
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("questSequence(1) == 2",  ComparisonOp.Eq)]
    [InlineData("questSequence(1) != 2",  ComparisonOp.NotEq)]
    [InlineData("questSequence(1) > 2",   ComparisonOp.Gt)]
    [InlineData("questSequence(1) < 2",   ComparisonOp.Lt)]
    [InlineData("questSequence(1) >= 2",  ComparisonOp.GtEq)]
    [InlineData("questSequence(1) <= 2",  ComparisonOp.LtEq)]
    public void ComparisonOperators_AllSix(string source, ComparisonOp expectedOp)
    {
        var result = PredicateParser.Parse(source);
        Assert.True(result.IsSuccess, FormatErrors(result));
        var cmp = Assert.IsType<PredicateAst.Comparison>(result.Ast);
        Assert.Equal(expectedOp, cmp.Op);
    }

    // ------------------------------------------------------------------ //
    // Grouped expression
    // ------------------------------------------------------------------ //

    [Fact]
    public void GroupedExpression_Parentheses()
    {
        var result = PredicateParser.Parse("(playerInCombat())");
        Assert.True(result.IsSuccess, FormatErrors(result));
        Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
    }

    // =========================================================================
    // questVariable parser tests (PA1-PA3 from QUEST_VARIABLE_PREDICATE_PLAN.md)
    // No parser change is needed — parsing follows from the registry (D2).
    // These tests pin that the parser accepts questVariable once the registry
    // entry exists, and that range checking is a semantic (checker) concern only.
    // =========================================================================

    [Fact]
    public void QuestVariable_ParsesToFunctionCall()
    {
        /*
         * RED: Will fail until Builder adds the questVariable registry entry.
         *      The parser currently reports an unknown-function parse error for unregistered names.
         *
         * CONTRACT (PA1): Given "questVariable(66104, 0)",
         *                 When PredicateParser.Parse is called,
         *                 Then IsSuccess == true
         *                      Ast is FunctionCall("questVariable", [IntLiteral(66104), IntLiteral(0)]).
         *
         * BUILDER GUIDANCE: Add new("questVariable", new Fixed(2), [Int, Int], Int) to
         *   FunctionRegistry.cs. No Parser.cs change is required.
         */

        // Act
        var result = PredicateParser.Parse("questVariable(66104, 0)");

        // Assert
        Assert.True(result.IsSuccess, FormatErrors(result));
        var call = Assert.IsType<PredicateAst.FunctionCall>(result.Ast);
        Assert.Equal("questVariable", call.Name);
        Assert.Equal(2, call.Args.Count);
        Assert.Equal(new PredicateAst.IntLiteral(66104L), call.Args[0]);
        Assert.Equal(new PredicateAst.IntLiteral(0L), call.Args[1]);
    }

    [Fact]
    public void QuestVariable_ComposesWithComparison()
    {
        /*
         * RED: Will fail until Builder adds the questVariable registry entry.
         *
         * CONTRACT (PA2): Given "questVariable(66104, 0) >= 3",
         *                 When parsed,
         *                 Then Ast is Comparison(FunctionCall("questVariable", [IntLiteral(66104), IntLiteral(0)]),
         *                                        GtEq, IntLiteral(3)).
         *                 Confirms it composes with comparison exactly like questSequence.
         */

        // Act
        var result = PredicateParser.Parse("questVariable(66104, 0) >= 3");

        // Assert
        Assert.True(result.IsSuccess, FormatErrors(result));
        var cmp = Assert.IsType<PredicateAst.Comparison>(result.Ast);
        Assert.Equal(ComparisonOp.GtEq, cmp.Op);
        var call = Assert.IsType<PredicateAst.FunctionCall>(cmp.Left);
        Assert.Equal("questVariable", call.Name);
        Assert.Equal(2, call.Args.Count);
        Assert.Equal(new PredicateAst.IntLiteral(66104L), call.Args[0]);
        Assert.Equal(new PredicateAst.IntLiteral(0L), call.Args[1]);
        Assert.Equal(new PredicateAst.IntLiteral(3L), cmp.Right);
    }

    [Fact]
    public void QuestVariable_OutOfRangeIndex_StillParsesSuccessfully()
    {
        /*
         * RED: Will fail until Builder adds the questVariable registry entry.
         *
         * CONTRACT (PA3): Given "questVariable(66104, 6)" (index 6 is out-of-range),
         *                 When parsed,
         *                 Then IsSuccess == true — the PARSER does not range-check.
         *                 Range validation is the checker's job (a semantic error, not syntactic).
         *
         * BUILDER GUIDANCE: The parser never validates argument values, only shape.
         *   Once the registry entry exists, "questVariable(66104, 6)" parses fine.
         *   The checker's CK4 test covers the semantic rejection of index 6.
         */

        // Act
        var result = PredicateParser.Parse("questVariable(66104, 6)");

        // Assert — parser must accept it; range is a semantic concern only
        Assert.True(result.IsSuccess,
            $"Parser should accept questVariable(66104, 6) even though index 6 is out-of-range. " +
            $"Range checking belongs to the checker, not the parser. Errors: {FormatErrors(result)}");
        var cmp_or_call = result.Ast;
        Assert.NotNull(cmp_or_call);
    }

    // ------------------------------------------------------------------ //
    // Helper
    // ------------------------------------------------------------------ //

    private static string FormatErrors(ParseResult result)
        => string.Join("; ", result.Errors.Select(e => $"[{e.Code}@{e.Column}] {e.Message}"));
}