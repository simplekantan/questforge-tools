namespace QuestForge.Predicates;

public static class PredicateChecker
{
    public static IReadOnlyList<ParseError> Check(PredicateAst ast, IFragmentParameterScope? scope = null)
    {
        var errors = new List<ParseError>();

        if (ast is PredicateAst.DefaultLiteral)
            return errors; // 'default' alone is always valid

        var type = InferType(ast, scope, errors);

        if (type != null && type != PredicateType.Bool)
            errors.Add(new ParseError("type-mismatch",
                $"bare predicate expression must be Bool; got {type}", 0));

        return errors;
    }

    // Returns the inferred type of the node, or null when an error was emitted.
    private static PredicateType? InferType(
        PredicateAst node, IFragmentParameterScope? scope, List<ParseError> errors) =>
        node switch
        {
            PredicateAst.DefaultLiteral     => DefaultInComposition(errors),
            PredicateAst.IntLiteral         => PredicateType.Int,
            PredicateAst.StringLiteral      => PredicateType.String,
            PredicateAst.PositionLiteral    => PredicateType.Position,
            PredicateAst.ParameterRef r     => ResolveParam(r, scope),
            PredicateAst.FunctionCall call  => CheckCall(call, scope, errors),
            PredicateAst.Comparison cmp     => CheckComparison(cmp, scope, errors),
            PredicateAst.And and            => CheckBinary(and.Left, and.Right, scope, errors),
            PredicateAst.Or or              => CheckBinary(or.Left, or.Right, scope, errors),
            PredicateAst.Not not            => CheckNot(not, scope, errors),
            _                               => null
        };

    private static PredicateType? DefaultInComposition(List<ParseError> errors)
    {
        errors.Add(new ParseError("default-not-composable",
            "the 'default' keyword must be the entire predicate", 0));
        return null;
    }

    private static PredicateType? ResolveParam(PredicateAst.ParameterRef r, IFragmentParameterScope? scope)
    {
        if (scope == null || !scope.TryGetParameterType(r.Name, out var t)) return null;
        return t;
    }

    private static PredicateType? CheckCall(
        PredicateAst.FunctionCall call, IFragmentParameterScope? scope, List<ParseError> errors)
    {
        if (!FunctionRegistry.TryGet(call.Name, out var sig))
        {
            var suggestions = FunctionRegistry.SuggestSimilar(call.Name);
            var hint = suggestions.Count > 0 ? $"did you mean '{suggestions[0]}'?" : null;
            errors.Add(new ParseError("unknown-function",
                $"'{call.Name}' is not a known function", 0, hint));
            return null;
        }

        // Arity check
        var count = call.Args.Count;
        var arityOk = sig.Arity switch
        {
            Arity.Fixed f         => count == f.Count,
            Arity.OptionalTail ot => count >= ot.Required && count <= ot.Required + ot.Optional,
            Arity.VariadicMin vm  => count >= vm.Minimum,
            _                    => true
        };

        if (!arityOk)
        {
            var expected = sig.Arity switch
            {
                Arity.Fixed f         => $"exactly {f.Count}",
                Arity.OptionalTail ot => ot.Required == 0
                    ? $"0 or {ot.Optional}"
                    : $"{ot.Required} or {ot.Required + ot.Optional}",
                Arity.VariadicMin vm  => $"at least {vm.Minimum}",
                _                    => "?"
            };
            errors.Add(new ParseError("arity-mismatch",
                $"function '{call.Name}' requires {expected} argument(s); got {count}", 0));
        }

        // Arg type checks
        for (var i = 0; i < call.Args.Count; i++)
        {
            var argType = InferType(call.Args[i], scope, errors);
            if (argType == null || sig.ParameterTypes.Count == 0) continue;

            var paramIdx = Math.Min(i, sig.ParameterTypes.Count - 1);
            var expected = sig.ParameterTypes[paramIdx];
            if (argType != expected)
                errors.Add(new ParseError("type-mismatch",
                    $"argument {i + 1} of '{call.Name}' expects {expected}, got {argType}", 0));
        }

        return sig.ReturnType;
    }

    private static PredicateType? CheckComparison(
        PredicateAst.Comparison cmp, IFragmentParameterScope? scope, List<ParseError> errors)
    {
        var left  = InferType(cmp.Left,  scope, errors);
        var right = InferType(cmp.Right, scope, errors);

        if (left == null || right == null) return PredicateType.Bool;

        if (left == PredicateType.Position || right == PredicateType.Position)
        {
            errors.Add(new ParseError("type-mismatch", "Position values cannot be compared", 0));
            return PredicateType.Bool;
        }

        if (left != right)
        {
            errors.Add(new ParseError("type-mismatch",
                $"cannot compare {left} to {right}", 0));
            return PredicateType.Bool;
        }

        var isRelational = cmp.Op is ComparisonOp.Gt or ComparisonOp.Lt or ComparisonOp.GtEq or ComparisonOp.LtEq;
        if (isRelational && left != PredicateType.Int)
            errors.Add(new ParseError("type-mismatch",
                $"relational operator '{OpSymbol(cmp.Op)}' is not supported for {left}", 0));

        return PredicateType.Bool;
    }

    private static PredicateType? CheckBinary(
        PredicateAst left, PredicateAst right, IFragmentParameterScope? scope, List<ParseError> errors)
    {
        InferType(left,  scope, errors);
        InferType(right, scope, errors);
        return PredicateType.Bool;
    }

    private static PredicateType? CheckNot(
        PredicateAst.Not not, IFragmentParameterScope? scope, List<ParseError> errors)
    {
        InferType(not.Inner, scope, errors);
        return PredicateType.Bool;
    }

    private static string OpSymbol(ComparisonOp op) => op switch
    {
        ComparisonOp.Eq    => "==",
        ComparisonOp.NotEq => "!=",
        ComparisonOp.Gt    => ">",
        ComparisonOp.Lt    => "<",
        ComparisonOp.GtEq  => ">=",
        ComparisonOp.LtEq  => "<=",
        _                  => op.ToString()
    };
}