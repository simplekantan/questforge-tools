namespace QuestForge.Predicates;

internal sealed class Lexer
{
    private readonly string _src;
    private int _pos;
    private readonly List<Token> _tokens = new();

    private Lexer(string source)
    {
        _src = source;
        _pos = 0;
    }

    public static Token[] Tokenize(string source)
    {
        var lexer = new Lexer(source);
        lexer.Run();
        return lexer._tokens.ToArray();
    }

    private void Run()
    {
        while (_pos < _src.Length)
        {
            SkipWhitespace();
            if (_pos >= _src.Length)
                break;

            int col = _pos;
            char c = _src[_pos];

            // Identifiers and keywords
            if (char.IsLetter(c) || c == '_')
            {
                ReadIdentifierOrKeyword();
                continue;
            }

            // Numbers
            if (char.IsDigit(c))
            {
                ReadNumber(col);
                continue;
            }

            // String literals
            if (c == '"')
            {
                ReadString(col);
                continue;
            }

            // Parameter references ${name}
            if (c == '$')
            {
                ReadParameterRef(col);
                continue;
            }

            // Two-character operators
            if (c == '=' && Peek(1) == '=')
            {
                Emit(TokenKind.Eq, "==", col);
                _pos += 2;
                continue;
            }
            if (c == '!' && Peek(1) == '=')
            {
                Emit(TokenKind.NotEq, "!=", col);
                _pos += 2;
                continue;
            }
            if (c == '>' && Peek(1) == '=')
            {
                Emit(TokenKind.GtEq, ">=", col);
                _pos += 2;
                continue;
            }
            if (c == '<' && Peek(1) == '=')
            {
                Emit(TokenKind.LtEq, "<=", col);
                _pos += 2;
                continue;
            }

            // Single-character operators and punctuation
            switch (c)
            {
                case '>':
                    Emit(TokenKind.Gt, ">", col);
                    _pos++;
                    break;
                case '<':
                    Emit(TokenKind.Lt, "<", col);
                    _pos++;
                    break;
                case '(':
                    Emit(TokenKind.LParen, "(", col);
                    _pos++;
                    break;
                case ')':
                    Emit(TokenKind.RParen, ")", col);
                    _pos++;
                    break;
                case '{':
                    Emit(TokenKind.LBrace, "{", col);
                    _pos++;
                    break;
                case '}':
                    Emit(TokenKind.RBrace, "}", col);
                    _pos++;
                    break;
                case ',':
                    Emit(TokenKind.Comma, ",", col);
                    _pos++;
                    break;
                case ':':
                    Emit(TokenKind.Colon, ":", col);
                    _pos++;
                    break;
                case '=':
                    // Single = is an error
                    Emit(TokenKind.Error, "=", col);
                    _pos++;
                    break;
                case '!':
                    // Single ! is an error
                    Emit(TokenKind.Error, "!", col);
                    _pos++;
                    break;
                case '-':
                    // Unary minus not supported at lexer level
                    Emit(TokenKind.Error, "-", col);
                    _pos++;
                    break;
                default:
                    Emit(TokenKind.Error, c.ToString(), col);
                    _pos++;
                    break;
            }
        }

        _tokens.Add(new Token(TokenKind.EndOfInput, "", _pos));
    }

    private void ReadIdentifierOrKeyword()
    {
        int col = _pos;
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        string text = _src[start.._pos];
        var kind = text switch
        {
            "and"     => TokenKind.KeywordAnd,
            "or"      => TokenKind.KeywordOr,
            "not"     => TokenKind.KeywordNot,
            "default" => TokenKind.KeywordDefault,
            _         => TokenKind.Identifier
        };
        Emit(kind, text, col);
    }

    private void ReadNumber(int col)
    {
        int start = _pos;
        while (_pos < _src.Length && char.IsDigit(_src[_pos]))
            _pos++;

        // Check for decimal point
        if (_pos < _src.Length && _src[_pos] == '.' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1]))
        {
            _pos++; // consume '.'
            while (_pos < _src.Length && char.IsDigit(_src[_pos]))
                _pos++;
            Emit(TokenKind.NumberFloat, _src[start.._pos], col);
        }
        else
        {
            Emit(TokenKind.NumberInt, _src[start.._pos], col);
        }
    }

    private void ReadString(int col)
    {
        _pos++; // consume opening quote
        var sb = new System.Text.StringBuilder();
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '"')
            {
                _pos++; // consume closing quote
                Emit(TokenKind.String, sb.ToString(), col);
                return;
            }
            if (c == '\\' && _pos + 1 < _src.Length && _src[_pos + 1] == '"')
            {
                sb.Append('"');
                _pos += 2;
                continue;
            }
            sb.Append(c);
            _pos++;
        }
        // Unterminated string
        Emit(TokenKind.Error, sb.ToString(), col);
    }

    private void ReadParameterRef(int col)
    {
        _pos++; // consume '$'
        if (_pos < _src.Length && _src[_pos] == '{')
        {
            _pos++; // consume '{'
            int start = _pos;
            while (_pos < _src.Length && _src[_pos] != '}')
                _pos++;
            string name = _src[start.._pos];
            if (_pos < _src.Length)
                _pos++; // consume '}'
            Emit(TokenKind.ParameterRef, name, col);
        }
        else
        {
            Emit(TokenKind.Error, "$", col);
        }
    }

    private void SkipWhitespace()
    {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
            _pos++;
    }

    private char Peek(int offset)
    {
        int idx = _pos + offset;
        return idx < _src.Length ? _src[idx] : '\0';
    }

    private void Emit(TokenKind kind, string text, int col)
    {
        _tokens.Add(new Token(kind, text, col));
    }
}