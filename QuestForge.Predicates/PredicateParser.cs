namespace QuestForge.Predicates;

public static class PredicateParser
{
    public static ParseResult Parse(string source) => Parse(source, null);

    public static ParseResult Parse(string source, IFragmentParameterScope? scope)
    {
        var tokens = Lexer.Tokenize(source);
        return new Parser(tokens, source, scope).Parse();
    }
}