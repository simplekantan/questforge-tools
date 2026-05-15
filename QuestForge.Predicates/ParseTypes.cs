namespace QuestForge.Predicates;

public enum PredicateType { Int, Bool, String, Position }

public abstract record Arity
{
    public sealed record Fixed(int Count) : Arity;
    public sealed record OptionalTail(int Required, int Optional) : Arity;
    public sealed record VariadicMin(int Minimum) : Arity;
}

public record FunctionSignature(
    string Name,
    Arity Arity,
    IReadOnlyList<PredicateType> ParameterTypes,
    PredicateType ReturnType);

public record ParseError(string Code, string Message, int Column, string? Suggestion = null);

public record ParseResult(PredicateAst? Ast, IReadOnlyList<ParseError> Errors)
{
    public bool IsSuccess => Errors.Count == 0;
}

public interface IFragmentParameterScope
{
    bool TryGetParameterType(string name, out PredicateType type);
}