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
    public void Comparison_IntEq_currentJob_NoError()
        => AssertNoSemanticErrors("currentJob() != 19");

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

    // =========================================================================
    // questVariable checker tests (CK1-CK14 from QUEST_VARIABLE_PREDICATE_PLAN.md)
    // RED PHASE: all fail until Builder adds the questVariable entry + range check.
    //
    // Note on CK6 (negative index "-1"): DROPPED. The lexer emits '-' as a
    // TokenKind.Error token; ParseArg does not handle it outside position literal
    // keys. So "questVariable(66104, -1)" does NOT parse (produces a parse error,
    // not a checker error). Out-of-range behaviour is fully covered by CK4/CK5.
    // =========================================================================

    // ---- CK1-CK3: in-range indices produce no error ----------------------

    [Fact]
    public void QuestVariable_InRange_HappyPath_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK1): "questVariable(66104, 0) >= 3" → no semantic errors.
         *                 (Index 0 is in 0–5.)
         */
        AssertNoSemanticErrors("questVariable(66104, 0) >= 3");
    }

    [Fact]
    public void QuestVariable_BoundaryLow_IndexZero_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK2): "questVariable(66104, 0) >= 1" → no error (index 0 is valid lower bound).
         */
        AssertNoSemanticErrors("questVariable(66104, 0) >= 1");
    }

    [Fact]
    public void QuestVariable_BoundaryHigh_IndexFive_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK3): "questVariable(66104, 5) >= 1" → no error (index 5 is valid upper bound).
         */
        AssertNoSemanticErrors("questVariable(66104, 5) >= 1");
    }

    // ---- CK4-CK5: out-of-range literal indices emit quest-variable-index-out-of-range ---

    [Fact]
    public void QuestVariable_OutOfRange_IndexSix_ReportsError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry;
         *      then fails with wrong/missing error code until Builder adds the range check.
         *
         * CONTRACT (CK4): "questVariable(66104, 6) >= 1" → exactly one error,
         *                 code "quest-variable-index-out-of-range".
         *                 (Index 6 is the first value past the valid 0–5 range.)
         *
         * BUILDER GUIDANCE: Add arm in CheckCall gated on call.Name == "questVariable"
         *   && call.Args.Count == 2 && call.Args[1] is IntLiteral { Value: var idx }
         *   && (idx < 0 || idx > 5). Message: "questVariable index must be a literal in 0–5; got 6".
         */
        AssertSingleSemanticError("questVariable(66104, 6) >= 1", "quest-variable-index-out-of-range");
    }

    [Fact]
    public void QuestVariable_OutOfRange_IndexSeven_ReportsError()
    {
        /*
         * RED: Same as CK4.
         *
         * CONTRACT (CK5): "questVariable(66104, 7) == 0" → exactly one
         *                 "quest-variable-index-out-of-range".
         */
        AssertSingleSemanticError("questVariable(66104, 7) == 0", "quest-variable-index-out-of-range");
    }

    // ---- CK7-CK8: wrong arity emits arity-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariable_WrongArity_TooFew_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK7): "questVariable(66104) >= 1" → exactly one "arity-mismatch".
         *                 The range check is suppressed when arity is wrong (no double-reporting).
         */
        AssertSingleSemanticError("questVariable(66104) >= 1", "arity-mismatch");
    }

    [Fact]
    public void QuestVariable_WrongArity_TooMany_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK8): "questVariable(66104, 0, 1) >= 1" → exactly one "arity-mismatch".
         */
        AssertSingleSemanticError("questVariable(66104, 0, 1) >= 1", "arity-mismatch");
    }

    // ---- CK9-CK10: wrong type emits type-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariable_WrongType_QuestIdIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK9): questVariable("66104", 0) >= 1 → exactly one "type-mismatch"
         *                 (and NOT a range error — the questId arg is the wrong type).
         */
        AssertSingleSemanticError("questVariable(\"66104\", 0) >= 1", "type-mismatch");
    }

    [Fact]
    public void QuestVariable_WrongType_IndexIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK10): questVariable(66104, "0") >= 1 → exactly one "type-mismatch"
         *                  (index arg is a string, not IntLiteral — range check is skipped).
         */
        AssertSingleSemanticError("questVariable(66104, \"0\") >= 1", "type-mismatch");
    }

    // ---- CK11: typo produces unknown-function with suggestion ------------

    [Fact]
    public void QuestVariable_Typo_UnknownFunction_WithSuggestion()
    {
        /*
         * RED: Will pass even before the registry entry exists (unknown-function fires for any
         *      unknown name) — BUT the suggestion assertion fails until questVariable is
         *      registered, because SuggestSimilar returns nothing for an unregistered name.
         *
         * CONTRACT (CK11): "questVaraible(66104, 0) >= 1" → exactly one "unknown-function"
         *                  with a suggestion containing "questVariable".
         *
         * BUILDER GUIDANCE: Once the entry is registered, SuggestSimilar("questVaraible")
         *   returns ["questVariable"] (Levenshtein distance 2).
         */
        var errors = Semantic("questVaraible(66104, 0) >= 1");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("questVariable", errors[0].Suggestion);
    }

    // ---- CK12: bare Int misuse → type-mismatch (questSequence parity) ---

    [Fact]
    public void QuestVariable_BareIntMisuse_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry;
         *      then fails with wrong error code until implementation is complete.
         *
         * CONTRACT (CK12): "questVariable(66104, 0)" used bare (not composed with a
         *                  comparison) → exactly one "type-mismatch"
         *                  ("bare predicate expression must be Bool; got Int").
         *                  Mirrors questSequence bare behaviour.
         */
        AssertSingleSemanticError("questVariable(66104, 0)", "type-mismatch");
    }

    // ---- CK13-CK14: fragment parameter index ----------------------------

    [Fact]
    public void QuestVariable_FragmentParamIndex_IntType_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK13): With a scope declaring "slot: Int",
         *                  "questVariable(66104, ${slot}) >= 1" → no semantic errors.
         *                  A ParameterRef index is not range-checked (value unknown at
         *                  validation time). The engine guards range at runtime (D5).
         */
        var scope = FragScope(("slot", PredicateType.Int));
        AssertNoSemanticErrors("questVariable(66104, ${slot}) >= 1", scope);
    }

    [Fact]
    public void QuestVariable_FragmentParamIndex_WrongType_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK14): With a scope declaring "slot: String",
         *                  "questVariable(66104, ${slot}) >= 1" → exactly one "type-mismatch"
         *                  (the existing param-type check fires; NOT a range error).
         *
         * BUILDER GUIDANCE: The range check gate is `call.Args[1] is IntLiteral` — a
         *   ParameterRef does not match this pattern, so only the type check fires.
         */
        var scope = FragScope(("slot", PredicateType.String));
        AssertSingleSemanticError("questVariable(66104, ${slot}) >= 1", "type-mismatch", scope);
    }

    // =========================================================================
    // questVariableLow checker tests (CK-L* from NIBBLE_PREDICATES_PLAN.md)
    // RED: all fail (unknown-function) until Builder adds the registry entry +
    //      widens the index-range gate to the three quest-variable function names.
    // =========================================================================

    // ---- CK-L1/CK-L2/CK-L3: in-range indices produce no error ---------------

    [Fact]
    public void QuestVariableLow_InRange_HappyPath_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L1): "questVariableLow(65847, 0) >= 3" → no semantic errors.
         */
        AssertNoSemanticErrors("questVariableLow(65847, 0) >= 3");
    }

    [Fact]
    public void QuestVariableLow_BoundaryLow_IndexZero_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L2): "questVariableLow(65847, 0) >= 1" → no error (index 0 is valid lower bound).
         */
        AssertNoSemanticErrors("questVariableLow(65847, 0) >= 1");
    }

    [Fact]
    public void QuestVariableLow_BoundaryHigh_IndexFive_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L3): "questVariableLow(65847, 5) >= 1" → no error (index 5 is valid upper bound).
         */
        AssertNoSemanticErrors("questVariableLow(65847, 5) >= 1");
    }

    // ---- CK-L4/CK-L5: out-of-range literal indices emit quest-variable-index-out-of-range ---

    [Fact]
    public void QuestVariableLow_OutOfRange_IndexSix_ReportsError()
    {
        /*
         * RED: Fails with unknown-function until entry added; then fails (no range error)
         *      until Builder widens the checker gate to include questVariableLow.
         *
         * CONTRACT (CK-L4): "questVariableLow(65847, 6) >= 1" → exactly one
         *                   "quest-variable-index-out-of-range".
         */
        AssertSingleSemanticError("questVariableLow(65847, 6) >= 1", "quest-variable-index-out-of-range");
    }

    [Fact]
    public void QuestVariableLow_OutOfRange_IndexSeven_ReportsError()
    {
        /*
         * RED: Same as CK-L4.
         *
         * CONTRACT (CK-L5): "questVariableLow(65847, 7) == 0" → exactly one
         *                   "quest-variable-index-out-of-range".
         */
        AssertSingleSemanticError("questVariableLow(65847, 7) == 0", "quest-variable-index-out-of-range");
    }

    // ---- CK-L6/CK-L7: wrong arity emits arity-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariableLow_WrongArity_TooFew_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L6): "questVariableLow(65847) >= 1" → exactly one "arity-mismatch".
         *                   Range check is suppressed (no double-reporting).
         */
        AssertSingleSemanticError("questVariableLow(65847) >= 1", "arity-mismatch");
    }

    [Fact]
    public void QuestVariableLow_WrongArity_TooMany_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L7): "questVariableLow(65847, 0, 1) >= 1" → exactly one "arity-mismatch".
         */
        AssertSingleSemanticError("questVariableLow(65847, 0, 1) >= 1", "arity-mismatch");
    }

    // ---- CK-L8/CK-L9: wrong type emits type-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariableLow_WrongType_QuestIdIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L8): questVariableLow("65847", 0) >= 1 → exactly one "type-mismatch"
         *                   (NOT a range error — questId arg is the wrong type).
         */
        AssertSingleSemanticError("questVariableLow(\"65847\", 0) >= 1", "type-mismatch");
    }

    [Fact]
    public void QuestVariableLow_WrongType_IndexIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L9): questVariableLow(65847, "0") >= 1 → exactly one "type-mismatch"
         *                   (index arg is a string, not IntLiteral — range check is skipped).
         */
        AssertSingleSemanticError("questVariableLow(65847, \"0\") >= 1", "type-mismatch");
    }

    // ---- CK-L10: typo produces unknown-function with suggestion ---------------

    [Fact]
    public void QuestVariableLow_Typo_UnknownFunction_WithSuggestion()
    {
        /*
         * RED: The unknown-function error fires (that part passes), but the suggestion
         *      assertion fails until questVariableLow is registered.
         *
         * CONTRACT (CK-L10): "questVariableLwo(65847, 0) >= 1" → exactly one "unknown-function"
         *                    with a suggestion containing "questVariableLow".
         *                    Levenshtein("questVariableLwo", "questVariableLow") == 2.
         */
        var errors = Semantic("questVariableLwo(65847, 0) >= 1");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("questVariableLow", errors[0].Suggestion);
    }

    // ---- CK-L11: bare Int misuse → type-mismatch -------------------------

    [Fact]
    public void QuestVariableLow_BareIntMisuse_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry;
         *      then fails with wrong error code until implementation is complete.
         *
         * CONTRACT (CK-L11): "questVariableLow(65847, 0)" used bare → exactly one "type-mismatch"
         *                    ("bare predicate expression must be Bool; got Int").
         *                    Mirrors questVariable bare behaviour.
         */
        AssertSingleSemanticError("questVariableLow(65847, 0)", "type-mismatch");
    }

    // ---- CK-L12/CK-L13: fragment parameter index -------------------------

    [Fact]
    public void QuestVariableLow_FragmentParamIndex_IntType_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L12): With a scope declaring "slot: Int",
         *                    "questVariableLow(65847, ${slot}) >= 1" → no semantic errors.
         *                    A ParameterRef index is not range-checked (value unknown at validation time).
         */
        var scope = FragScope(("slot", PredicateType.Int));
        AssertNoSemanticErrors("questVariableLow(65847, ${slot}) >= 1", scope);
    }

    [Fact]
    public void QuestVariableLow_FragmentParamIndex_WrongType_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-L13): With a scope declaring "slot: String",
         *                    "questVariableLow(65847, ${slot}) >= 1" → exactly one "type-mismatch"
         *                    (the existing param-type check fires; NOT a range error).
         */
        var scope = FragScope(("slot", PredicateType.String));
        AssertSingleSemanticError("questVariableLow(65847, ${slot}) >= 1", "type-mismatch", scope);
    }

    // =========================================================================
    // questVariableHigh checker tests (CK-H* from NIBBLE_PREDICATES_PLAN.md)
    // Symmetric to the CK-L* group above.
    // RED: all fail (unknown-function) until Builder adds the registry entry +
    //      widens the index-range gate to include questVariableHigh.
    // =========================================================================

    // ---- CK-H1/CK-H2/CK-H3: in-range indices produce no error ---------------

    [Fact]
    public void QuestVariableHigh_InRange_HappyPath_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H1): "questVariableHigh(65847, 1) >= 3" → no semantic errors.
         */
        AssertNoSemanticErrors("questVariableHigh(65847, 1) >= 3");
    }

    [Fact]
    public void QuestVariableHigh_BoundaryLow_IndexZero_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H2): "questVariableHigh(65847, 0) >= 1" → no error.
         */
        AssertNoSemanticErrors("questVariableHigh(65847, 0) >= 1");
    }

    [Fact]
    public void QuestVariableHigh_BoundaryHigh_IndexFive_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H3): "questVariableHigh(65847, 5) >= 1" → no error (index 5 is valid upper bound).
         */
        AssertNoSemanticErrors("questVariableHigh(65847, 5) >= 1");
    }

    // ---- CK-H4/CK-H5: out-of-range literal indices emit quest-variable-index-out-of-range ---

    [Fact]
    public void QuestVariableHigh_OutOfRange_IndexSix_ReportsError()
    {
        /*
         * RED: Fails with unknown-function until entry added; then fails (no range error)
         *      until Builder widens the checker gate to include questVariableHigh.
         *
         * CONTRACT (CK-H4): "questVariableHigh(65847, 6) >= 1" → exactly one
         *                   "quest-variable-index-out-of-range".
         */
        AssertSingleSemanticError("questVariableHigh(65847, 6) >= 1", "quest-variable-index-out-of-range");
    }

    [Fact]
    public void QuestVariableHigh_OutOfRange_IndexSeven_ReportsError()
    {
        /*
         * RED: Same as CK-H4.
         *
         * CONTRACT (CK-H5): "questVariableHigh(65847, 7) == 0" → exactly one
         *                   "quest-variable-index-out-of-range".
         */
        AssertSingleSemanticError("questVariableHigh(65847, 7) == 0", "quest-variable-index-out-of-range");
    }

    // ---- CK-H6/CK-H7: wrong arity emits arity-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariableHigh_WrongArity_TooFew_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H6): "questVariableHigh(65847) >= 1" → exactly one "arity-mismatch".
         */
        AssertSingleSemanticError("questVariableHigh(65847) >= 1", "arity-mismatch");
    }

    [Fact]
    public void QuestVariableHigh_WrongArity_TooMany_ReportsArityMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H7): "questVariableHigh(65847, 0, 1) >= 1" → exactly one "arity-mismatch".
         */
        AssertSingleSemanticError("questVariableHigh(65847, 0, 1) >= 1", "arity-mismatch");
    }

    // ---- CK-H8/CK-H9: wrong type emits type-mismatch (range check suppressed) ---

    [Fact]
    public void QuestVariableHigh_WrongType_QuestIdIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H8): questVariableHigh("65847", 0) >= 1 → exactly one "type-mismatch".
         */
        AssertSingleSemanticError("questVariableHigh(\"65847\", 0) >= 1", "type-mismatch");
    }

    [Fact]
    public void QuestVariableHigh_WrongType_IndexIsString_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H9): questVariableHigh(65847, "0") >= 1 → exactly one "type-mismatch"
         *                   (index arg is a string — range check is skipped).
         */
        AssertSingleSemanticError("questVariableHigh(65847, \"0\") >= 1", "type-mismatch");
    }

    // ---- CK-H10: typo produces unknown-function with suggestion ---------------

    [Fact]
    public void QuestVariableHigh_Typo_UnknownFunction_WithSuggestion()
    {
        /*
         * RED: The unknown-function error fires (that part passes), but the suggestion
         *      assertion fails until questVariableHigh is registered.
         *
         * CONTRACT (CK-H10): "questVariableHihg(65847, 0) >= 1" → exactly one "unknown-function"
         *                    with a suggestion containing "questVariableHigh".
         *                    Levenshtein("questVariableHihg", "questVariableHigh") == 2.
         */
        var errors = Semantic("questVariableHihg(65847, 0) >= 1");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("unknown-function", errors[0].Code);
        Assert.NotNull(errors[0].Suggestion);
        Assert.Contains("questVariableHigh", errors[0].Suggestion);
    }

    // ---- CK-H11: bare Int misuse → type-mismatch -------------------------

    [Fact]
    public void QuestVariableHigh_BareIntMisuse_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H11): "questVariableHigh(65847, 0)" used bare → exactly one "type-mismatch".
         */
        AssertSingleSemanticError("questVariableHigh(65847, 0)", "type-mismatch");
    }

    // ---- CK-H12/CK-H13: fragment parameter index -------------------------

    [Fact]
    public void QuestVariableHigh_FragmentParamIndex_IntType_NoError()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H12): With a scope declaring "slot: Int",
         *                    "questVariableHigh(65847, ${slot}) >= 1" → no semantic errors.
         *                    A ParameterRef index is not range-checked.
         */
        var scope = FragScope(("slot", PredicateType.Int));
        AssertNoSemanticErrors("questVariableHigh(65847, ${slot}) >= 1", scope);
    }

    [Fact]
    public void QuestVariableHigh_FragmentParamIndex_WrongType_ReportsTypeMismatch()
    {
        /*
         * RED: Fails with unknown-function until Builder adds the registry entry.
         *
         * CONTRACT (CK-H13): With a scope declaring "slot: String",
         *                    "questVariableHigh(65847, ${slot}) >= 1" → exactly one "type-mismatch".
         */
        var scope = FragScope(("slot", PredicateType.String));
        AssertSingleSemanticError("questVariableHigh(65847, ${slot}) >= 1", "type-mismatch", scope);
    }

    // ---- CK-MSG: error message names the offending function (not hardcoded "questVariable") ---

    [Fact]
    public void QuestVariableLow_OutOfRange_ErrorMessage_ContainsFunctionName()
    {
        /*
         * RED: Fails with unknown-function until entry added; then fails (wrong message text)
         *      until Builder uses call.Name in the error message instead of a hardcoded string.
         *
         * CONTRACT (CK-MSG): For "questVariableLow(65847, 6) >= 1", the single error's
         *                    Message contains "questVariableLow" — proving the gate uses
         *                    call.Name, not a hardcoded "questVariable".
         *                    Expected message prefix: "questVariableLow index must be a literal in 0–5; got 6"
         */
        var errors = Semantic("questVariableLow(65847, 6) >= 1");
        Assert.True(errors.Count == 1,
            $"Expected exactly 1 semantic error but got {errors.Count}: {Fmt(errors)}");
        Assert.Equal("quest-variable-index-out-of-range", errors[0].Code);
        Assert.Contains("questVariableLow", errors[0].Message);
    }
}