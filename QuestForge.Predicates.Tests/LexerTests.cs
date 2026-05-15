using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class LexerTests
{
    // Helper to get all tokens except EndOfInput
    private static Token[] TokenizeNoEoi(string source)
    {
        var all = Lexer.Tokenize(source);
        return all.Where(t => t.Kind != TokenKind.EndOfInput).ToArray();
    }

    // Helper to assert a single token sequence matches kinds and texts
    private static void AssertTokens(Token[] tokens, params (TokenKind kind, string text)[] expected)
    {
        Assert.Equal(expected.Length, tokens.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].kind, tokens[i].Kind);
            Assert.Equal(expected[i].text, tokens[i].Text);
        }
    }

    // ------------------------------------------------------------------ //
    // Empty / whitespace
    // ------------------------------------------------------------------ //

    [Fact]
    public void EmptyInput_ProducesOnlyEndOfInput()
    {
        var tokens = Lexer.Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfInput, tokens[0].Kind);
    }

    [Fact]
    public void WhitespaceOnly_ProducesOnlyEndOfInput()
    {
        var tokens = Lexer.Tokenize("   \t  ");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfInput, tokens[0].Kind);
    }

    // ------------------------------------------------------------------ //
    // Whitespace skipping / function call
    // ------------------------------------------------------------------ //

    [Fact]
    public void WhitespaceSkipped_FunctionCallWithSpaces()
    {
        // "  questFlag( 65 ,  1 )  "
        var tokens = TokenizeNoEoi("  questFlag( 65 ,  1 )  ");
        AssertTokens(tokens,
            (TokenKind.Identifier, "questFlag"),
            (TokenKind.LParen,     "("),
            (TokenKind.NumberInt,  "65"),
            (TokenKind.Comma,      ","),
            (TokenKind.NumberInt,  "1"),
            (TokenKind.RParen,     ")"));
    }

    // ------------------------------------------------------------------ //
    // Keywords
    // ------------------------------------------------------------------ //

    [Fact]
    public void Keyword_And()
    {
        var tokens = TokenizeNoEoi("and");
        AssertTokens(tokens, (TokenKind.KeywordAnd, "and"));
    }

    [Fact]
    public void Keyword_Or()
    {
        var tokens = TokenizeNoEoi("or");
        AssertTokens(tokens, (TokenKind.KeywordOr, "or"));
    }

    [Fact]
    public void Keyword_Not()
    {
        var tokens = TokenizeNoEoi("not");
        AssertTokens(tokens, (TokenKind.KeywordNot, "not"));
    }

    [Fact]
    public void Keyword_Default()
    {
        var tokens = TokenizeNoEoi("default");
        AssertTokens(tokens, (TokenKind.KeywordDefault, "default"));
    }

    [Fact]
    public void KeywordPrefix_IsIdentifier()
    {
        // "andSomething" starts with keyword prefix but is identifier
        var tokens = TokenizeNoEoi("andSomething");
        AssertTokens(tokens, (TokenKind.Identifier, "andSomething"));
    }

    [Fact]
    public void KeywordPrefix_Or_IsIdentifier()
    {
        var tokens = TokenizeNoEoi("orElse");
        AssertTokens(tokens, (TokenKind.Identifier, "orElse"));
    }

    [Fact]
    public void KeywordPrefix_Not_IsIdentifier()
    {
        var tokens = TokenizeNoEoi("notReady");
        AssertTokens(tokens, (TokenKind.Identifier, "notReady"));
    }

    // ------------------------------------------------------------------ //
    // Identifiers
    // ------------------------------------------------------------------ //

    [Fact]
    public void Identifier_WithUnderscoreAndDigits()
    {
        var tokens = TokenizeNoEoi("quest_Flag2");
        AssertTokens(tokens, (TokenKind.Identifier, "quest_Flag2"));
    }

    // ------------------------------------------------------------------ //
    // Numbers
    // ------------------------------------------------------------------ //

    [Fact]
    public void NumberInt_Simple()
    {
        var tokens = TokenizeNoEoi("65");
        AssertTokens(tokens, (TokenKind.NumberInt, "65"));
    }

    [Fact]
    public void NumberFloat_Simple()
    {
        var tokens = TokenizeNoEoi("5.0");
        AssertTokens(tokens, (TokenKind.NumberFloat, "5.0"));
    }

    [Fact]
    public void NumberFloat_NegativePrefix_ProducesErrorThenInt()
    {
        // "-3" → Error("-") then NumberInt("3")
        var tokens = TokenizeNoEoi("-3");
        Assert.Equal(2, tokens.Length);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal("-", tokens[0].Text);
        Assert.Equal(TokenKind.NumberInt, tokens[1].Kind);
        Assert.Equal("3", tokens[1].Text);
    }

    // ------------------------------------------------------------------ //
    // String literals
    // ------------------------------------------------------------------ //

    [Fact]
    public void StringLiteral_Simple_StripsQuotes()
    {
        // "Gladiator" → String("Gladiator") without quotes
        var tokens = TokenizeNoEoi("\"Gladiator\"");
        AssertTokens(tokens, (TokenKind.String, "Gladiator"));
    }

    [Fact]
    public void StringLiteral_WithEscapedQuote()
    {
        // say \"hi\" → String(say "hi")
        var tokens = TokenizeNoEoi("\"say \\\"hi\\\"\"");
        AssertTokens(tokens, (TokenKind.String, "say \"hi\""));
    }

    [Fact]
    public void StringLiteral_Unterminated_ProducesError()
    {
        var tokens = TokenizeNoEoi("\"unterminated");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
    }

    // ------------------------------------------------------------------ //
    // ParameterRef
    // ------------------------------------------------------------------ //

    [Fact]
    public void ParameterRef_Simple()
    {
        var tokens = TokenizeNoEoi("${finalPosition}");
        AssertTokens(tokens, (TokenKind.ParameterRef, "finalPosition"));
    }

    [Fact]
    public void ParameterRef_ShortName()
    {
        var tokens = TokenizeNoEoi("${pos}");
        AssertTokens(tokens, (TokenKind.ParameterRef, "pos"));
    }

    // ------------------------------------------------------------------ //
    // Comparison operators
    // ------------------------------------------------------------------ //

    [Fact]
    public void Operator_Eq()
    {
        var tokens = TokenizeNoEoi("==");
        AssertTokens(tokens, (TokenKind.Eq, "=="));
    }

    [Fact]
    public void Operator_NotEq()
    {
        var tokens = TokenizeNoEoi("!=");
        AssertTokens(tokens, (TokenKind.NotEq, "!="));
    }

    [Fact]
    public void Operator_Gt()
    {
        var tokens = TokenizeNoEoi(">");
        AssertTokens(tokens, (TokenKind.Gt, ">"));
    }

    [Fact]
    public void Operator_Lt()
    {
        var tokens = TokenizeNoEoi("<");
        AssertTokens(tokens, (TokenKind.Lt, "<"));
    }

    [Fact]
    public void Operator_GtEq()
    {
        var tokens = TokenizeNoEoi(">=");
        AssertTokens(tokens, (TokenKind.GtEq, ">="));
    }

    [Fact]
    public void Operator_LtEq()
    {
        var tokens = TokenizeNoEoi("<=");
        AssertTokens(tokens, (TokenKind.LtEq, "<="));
    }

    [Fact]
    public void Operator_GtSpaceEq_ProducesGtThenError()
    {
        // "> =" → Gt then Error("=")
        var tokens = TokenizeNoEoi("> =");
        Assert.Equal(2, tokens.Length);
        Assert.Equal(TokenKind.Gt,    tokens[0].Kind);
        Assert.Equal(">",             tokens[0].Text);
        Assert.Equal(TokenKind.Error, tokens[1].Kind);
        Assert.Equal("=",             tokens[1].Text);
    }

    [Fact]
    public void SingleEquals_ProducesError()
    {
        var tokens = TokenizeNoEoi("=");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal("=", tokens[0].Text);
    }

    [Fact]
    public void ExclamationAlone_ProducesError()
    {
        var tokens = TokenizeNoEoi("!");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal("!", tokens[0].Text);
    }

    // ------------------------------------------------------------------ //
    // Punctuation
    // ------------------------------------------------------------------ //

    [Fact]
    public void Punctuation_LParen()
    {
        var tokens = TokenizeNoEoi("(");
        AssertTokens(tokens, (TokenKind.LParen, "("));
    }

    [Fact]
    public void Punctuation_RParen()
    {
        var tokens = TokenizeNoEoi(")");
        AssertTokens(tokens, (TokenKind.RParen, ")"));
    }

    [Fact]
    public void Punctuation_LBrace()
    {
        var tokens = TokenizeNoEoi("{");
        AssertTokens(tokens, (TokenKind.LBrace, "{"));
    }

    [Fact]
    public void Punctuation_RBrace()
    {
        var tokens = TokenizeNoEoi("}");
        AssertTokens(tokens, (TokenKind.RBrace, "}"));
    }

    [Fact]
    public void Punctuation_Comma()
    {
        var tokens = TokenizeNoEoi(",");
        AssertTokens(tokens, (TokenKind.Comma, ","));
    }

    [Fact]
    public void Punctuation_Colon()
    {
        var tokens = TokenizeNoEoi(":");
        AssertTokens(tokens, (TokenKind.Colon, ":"));
    }

    // ------------------------------------------------------------------ //
    // Column tracking
    // ------------------------------------------------------------------ //

    [Fact]
    public void Column_FirstToken_IsZero()
    {
        var tokens = Lexer.Tokenize("foo");
        Assert.Equal(0, tokens[0].Column);
    }

    [Fact]
    public void Column_AfterLeadingSpaces()
    {
        // "   foo" → Identifier at column 3
        var tokens = Lexer.Tokenize("   foo");
        Assert.Equal(3, tokens[0].Column);
    }

    [Fact]
    public void Column_SecondToken_TracksProperly()
    {
        // "foo bar" → Identifier("foo") at 0, Identifier("bar") at 4
        var tokens = Lexer.Tokenize("foo bar");
        Assert.Equal(0, tokens[0].Column);
        Assert.Equal(4, tokens[1].Column);
    }

    [Fact]
    public void Column_EndOfInput_IsAtEnd()
    {
        var tokens = Lexer.Tokenize("foo");
        var eoi = tokens.Last();
        Assert.Equal(TokenKind.EndOfInput, eoi.Kind);
        Assert.Equal(3, eoi.Column);
    }

    // ------------------------------------------------------------------ //
    // EndOfInput always present
    // ------------------------------------------------------------------ //

    [Fact]
    public void AlwaysEndsWithEndOfInput()
    {
        var tokens = Lexer.Tokenize("questFlag(1, 2)");
        Assert.Equal(TokenKind.EndOfInput, tokens.Last().Kind);
    }

    // ------------------------------------------------------------------ //
    // Unknown character
    // ------------------------------------------------------------------ //

    [Fact]
    public void UnknownChar_ProducesError()
    {
        var tokens = TokenizeNoEoi("@");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal("@", tokens[0].Text);
    }
}