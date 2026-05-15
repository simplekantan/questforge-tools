namespace QuestForge.Predicates;

internal sealed class Parser
{
    private readonly Token[] _tokens;
    private readonly string _source;
    private readonly IFragmentParameterScope? _scope;
    private int _pos;
    private readonly List<ParseError> _errors = new();

    public Parser(Token[] tokens, string source, IFragmentParameterScope? scope)
    {
        _tokens = tokens;
        _source = source;
        _scope = scope;
    }

    public ParseResult Parse()
    {
        // Empty or whitespace-only input
        if (Current.Kind == TokenKind.EndOfInput)
        {
            _errors.Add(new ParseError("parse-error", "predicate must not be empty", 0));
            return new ParseResult(null, _errors);
        }

        var ast = ParseOr();

        // If there's leftover input that isn't EndOfInput, we still return what we have
        // (Any unexpected tokens would have been caught during parsing)

        return new ParseResult(ast, _errors);
    }

    // ------------------------------------------------------------------ //
    // Grammar rules
    // ------------------------------------------------------------------ //

    // ParseOr := ParseAnd ( 'or' ParseAnd )*
    private PredicateAst? ParseOr()
    {
        var left = ParseAnd();

        while (Current.Kind == TokenKind.KeywordOr)
        {
            Advance(); // consume 'or'
            var right = ParseAnd();
            if (left != null && right != null)
                left = new PredicateAst.Or(left, right);
            else if (right != null)
                left = right;
            // If right is null, left remains (error was already added)
        }

        return left;
    }

    // ParseAnd := ParseNot ( 'and' ParseNot )*
    private PredicateAst? ParseAnd()
    {
        var left = ParseNot();

        while (Current.Kind == TokenKind.KeywordAnd)
        {
            Advance(); // consume 'and'
            var right = ParseNot();
            if (left != null && right != null)
                left = new PredicateAst.And(left, right);
            else if (right != null)
                left = right;
        }

        return left;
    }

    // ParseNot := 'not' ParseNot | ParseComparisonOrAtom
    private PredicateAst? ParseNot()
    {
        if (Current.Kind == TokenKind.KeywordNot)
        {
            Advance(); // consume 'not'
            var inner = ParseNot();
            if (inner == null) return null;
            return new PredicateAst.Not(inner);
        }
        return ParseComparisonOrAtom();
    }

    // ParseComparisonOrAtom := ParseAtom (comparison-op RhsLiteral)?
    private PredicateAst? ParseComparisonOrAtom()
    {
        var left = ParseAtom();
        if (left == null) return null;

        var op = TryParseComparisonOp();
        if (op == null) return left;

        var right = ParseRhsLiteral();
        if (right == null) return left; // error already added

        return new PredicateAst.Comparison(left, op.Value, right);
    }

    private ComparisonOp? TryParseComparisonOp()
    {
        return Current.Kind switch
        {
            TokenKind.Eq    => ConsumeThenReturn(ComparisonOp.Eq),
            TokenKind.NotEq => ConsumeThenReturn(ComparisonOp.NotEq),
            TokenKind.Gt    => ConsumeThenReturn(ComparisonOp.Gt),
            TokenKind.Lt    => ConsumeThenReturn(ComparisonOp.Lt),
            TokenKind.GtEq  => ConsumeThenReturn(ComparisonOp.GtEq),
            TokenKind.LtEq  => ConsumeThenReturn(ComparisonOp.LtEq),
            _               => null
        };
    }

    private ComparisonOp ConsumeThenReturn(ComparisonOp op)
    {
        Advance();
        return op;
    }

    private PredicateAst? ParseRhsLiteral()
    {
        int col = Current.Column;
        switch (Current.Kind)
        {
            case TokenKind.NumberInt:
            {
                var val = long.Parse(Current.Text);
                Advance();
                return new PredicateAst.IntLiteral(val);
            }
            case TokenKind.NumberFloat:
            {
                // Float on RHS — only allowed if whole number
                var fval = float.Parse(Current.Text, System.Globalization.CultureInfo.InvariantCulture);
                var col2 = Current.Column;
                Advance();
                if (fval == MathF.Floor(fval))
                    return new PredicateAst.IntLiteral((long)fval);
                _errors.Add(new ParseError("parse-error", "float literals are not supported in v1", col2));
                return null;
            }
            case TokenKind.String:
            {
                var val = Current.Text;
                Advance();
                return new PredicateAst.StringLiteral(val);
            }
            default:
                _errors.Add(new ParseError("parse-error", $"expected literal after comparison operator, got '{Current.Text}'", col));
                return null;
        }
    }

    // ParseAtom
    private PredicateAst? ParseAtom()
    {
        int col = Current.Column;

        switch (Current.Kind)
        {
            // Grouped expression
            case TokenKind.LParen:
            {
                Advance(); // consume '('
                var inner = ParseOr();
                if (Current.Kind == TokenKind.RParen)
                    Advance();
                else
                    _errors.Add(new ParseError("parse-error", "expected ')'", Current.Column));
                return inner;
            }

            // default keyword
            case TokenKind.KeywordDefault:
                Advance();
                return new PredicateAst.DefaultLiteral();

            // Function call or bare identifier
            case TokenKind.Identifier:
            {
                var name = Current.Text;
                Advance();

                if (Current.Kind == TokenKind.LParen)
                {
                    Advance(); // consume '('
                    var args = ParseArgList(name);
                    if (Current.Kind == TokenKind.RParen)
                        Advance();
                    else
                        _errors.Add(new ParseError("parse-error", $"expected ')' after arguments of '{name}'", Current.Column));
                    return new PredicateAst.FunctionCall(name, args);
                }

                // Bare identifier → FunctionCall with empty args
                return new PredicateAst.FunctionCall(name, Array.Empty<PredicateAst>());
            }

            // Number literals
            case TokenKind.NumberInt:
            {
                var val = long.Parse(Current.Text);
                Advance();
                return new PredicateAst.IntLiteral(val);
            }

            case TokenKind.NumberFloat:
            {
                var fval = float.Parse(Current.Text, System.Globalization.CultureInfo.InvariantCulture);
                var col2 = Current.Column;
                Advance();
                if (fval == MathF.Floor(fval))
                    return new PredicateAst.IntLiteral((long)fval);
                _errors.Add(new ParseError("parse-error", "float literals are not supported in v1", col2));
                return null;
            }

            // String literals
            case TokenKind.String:
            {
                var val = Current.Text;
                Advance();
                return new PredicateAst.StringLiteral(val);
            }

            // Position literal
            case TokenKind.LBrace:
                return ParsePositionLiteral();

            // Parameter reference
            case TokenKind.ParameterRef:
            {
                var name = Current.Text;
                var paramCol = Current.Column;
                Advance();
                return ResolveParameterRef(name, paramCol);
            }

            default:
                _errors.Add(new ParseError("parse-error", $"unexpected token '{Current.Text}'", col));
                Advance(); // skip the bad token
                return null;
        }
    }

    private PredicateAst? ResolveParameterRef(string name, int col)
    {
        if (_scope == null)
        {
            _errors.Add(new ParseError("parse-error", "parameter references are only legal inside fragment files", col));
            return null;
        }
        if (!_scope.TryGetParameterType(name, out _))
        {
            _errors.Add(new ParseError("unknown-parameter", $"unknown parameter '${{{name}}}'", col));
            return null;
        }
        return new PredicateAst.ParameterRef(name);
    }

    private PredicateAst? ParsePositionLiteral()
    {
        int col = Current.Column;
        Advance(); // consume '{'

        float? xVal = null, yVal = null, zVal = null;
        bool hasError = false;

        // Parse up to 3 key:value pairs
        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.EndOfInput)
        {
            // Expect identifier key (bare or quoted string, e.g. x or "x")
            if (Current.Kind != TokenKind.Identifier && Current.Kind != TokenKind.String)
            {
                _errors.Add(new ParseError("parse-error", $"expected key identifier in position literal, got '{Current.Text}'", Current.Column));
                hasError = true;
                SkipTo(TokenKind.RBrace, TokenKind.Comma);
                break;
            }

            var key = Current.Text;
            int keyCol = Current.Column;
            Advance();

            if (Current.Kind != TokenKind.Colon)
            {
                _errors.Add(new ParseError("parse-error", "expected ':' after key in position literal", Current.Column));
                hasError = true;
                SkipTo(TokenKind.RBrace, TokenKind.Comma);
                break;
            }
            Advance(); // consume ':'

            // Value: number, possibly negative
            float value = 0f;
            bool negative = false;
            if (Current.Kind == TokenKind.Error && Current.Text == "-")
            {
                negative = true;
                Advance();
            }

            if (Current.Kind == TokenKind.NumberInt)
            {
                value = (float)long.Parse(Current.Text);
                Advance();
            }
            else if (Current.Kind == TokenKind.NumberFloat)
            {
                value = float.Parse(Current.Text, System.Globalization.CultureInfo.InvariantCulture);
                Advance();
            }
            else
            {
                _errors.Add(new ParseError("parse-error", $"expected number value for key '{key}'", Current.Column));
                hasError = true;
                SkipTo(TokenKind.RBrace, TokenKind.Comma);
                break;
            }

            if (negative) value = -value;

            switch (key)
            {
                case "x": xVal = value; break;
                case "y": yVal = value; break;
                case "z": zVal = value; break;
                default:
                    _errors.Add(new ParseError("parse-error", $"unexpected key '{key}' in position literal; expected x, y, or z", keyCol));
                    hasError = true;
                    break;
            }

            if (Current.Kind == TokenKind.Comma)
                Advance(); // consume ','
            else
                break;
        }

        if (Current.Kind == TokenKind.RBrace)
            Advance(); // consume '}'
        else if (!hasError)
            _errors.Add(new ParseError("parse-error", "expected '}' to close position literal", Current.Column));

        if (hasError) return null;

        if (xVal == null || yVal == null || zVal == null)
        {
            _errors.Add(new ParseError("parse-error", "position literal must have exactly x, y, and z keys", col));
            return null;
        }

        return new PredicateAst.PositionLiteral(xVal.Value, yVal.Value, zVal.Value);
    }

    // ArgList := Arg (',' Arg)*
    private IReadOnlyList<PredicateAst> ParseArgList(string functionName)
    {
        var args = new List<PredicateAst>();

        // Empty arg list
        if (Current.Kind == TokenKind.RParen)
            return args;

        while (true)
        {
            var arg = ParseArg(functionName, args.Count);
            if (arg != null)
                args.Add(arg);
            else
            {
                // Error recovery: skip to next comma or close paren
                while (Current.Kind != TokenKind.Comma
                    && Current.Kind != TokenKind.RParen
                    && Current.Kind != TokenKind.EndOfInput)
                    Advance();
            }

            if (Current.Kind == TokenKind.Comma)
            {
                Advance(); // consume ','
                // If the next token is RParen, we have a trailing comma — that's an error
                if (Current.Kind == TokenKind.RParen)
                {
                    _errors.Add(new ParseError("parse-error", "unexpected ',' before ')'", Previous.Column));
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return args;
    }

    // Arg := number-literal | string-literal | position-literal | parameter-ref
    private PredicateAst? ParseArg(string functionName, int argIndex)
    {
        int col = Current.Column;

        switch (Current.Kind)
        {
            case TokenKind.NumberInt:
            {
                var val = long.Parse(Current.Text);
                Advance();
                return new PredicateAst.IntLiteral(val);
            }

            case TokenKind.NumberFloat:
            {
                var fval = float.Parse(Current.Text, System.Globalization.CultureInfo.InvariantCulture);
                var col2 = Current.Column;
                Advance();

                // playerNear second arg (radius) can be a whole float → convert to int
                if (functionName == "playerNear" && argIndex == 1)
                {
                    if (fval == MathF.Floor(fval))
                        return new PredicateAst.IntLiteral((long)fval);
                    _errors.Add(new ParseError("parse-error",
                        "playerNear radius must be a whole number; use an integer literal instead", col2));
                    return null;
                }

                // All other functions — floats are not supported
                _errors.Add(new ParseError("parse-error", "float literals are not supported in v1", col2));
                return null;
            }

            case TokenKind.String:
            {
                var val = Current.Text;
                Advance();
                return new PredicateAst.StringLiteral(val);
            }

            case TokenKind.LBrace:
                return ParsePositionLiteral();

            case TokenKind.ParameterRef:
            {
                var name = Current.Text;
                var paramCol = Current.Column;
                Advance();
                return ResolveParameterRef(name, paramCol);
            }

            case TokenKind.RParen:
                // Empty arg (trailing comma case already handled, this is the `,)` case)
                _errors.Add(new ParseError("parse-error", "expected argument", col));
                return null;

            default:
                _errors.Add(new ParseError("parse-error", $"unexpected token '{Current.Text}' in argument list", col));
                return null;
        }
    }

    // ------------------------------------------------------------------ //
    // Token helpers
    // ------------------------------------------------------------------ //

    private Token Current => _tokens[_pos];

    private Token Previous => _pos > 0 ? _tokens[_pos - 1] : _tokens[0];

    private Token Advance()
    {
        var t = _tokens[_pos];
        if (_pos < _tokens.Length - 1) _pos++;
        return t;
    }

    private void SkipTo(params TokenKind[] stopAt)
    {
        while (Current.Kind != TokenKind.EndOfInput && !stopAt.Contains(Current.Kind))
            Advance();
    }

}