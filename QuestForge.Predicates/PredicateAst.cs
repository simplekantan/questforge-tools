namespace QuestForge.Predicates;

public abstract record PredicateAst
{
    public sealed record DefaultLiteral() : PredicateAst;
    public sealed record IntLiteral(long Value) : PredicateAst;
    public sealed record StringLiteral(string Value) : PredicateAst;
    public sealed record PositionLiteral(float X, float Y, float Z) : PredicateAst;
    public sealed record ParameterRef(string Name) : PredicateAst;
    public sealed record FunctionCall(string Name, IReadOnlyList<PredicateAst> Args) : PredicateAst;
    public sealed record Comparison(PredicateAst Left, ComparisonOp Op, PredicateAst Right) : PredicateAst;
    public sealed record And(PredicateAst Left, PredicateAst Right) : PredicateAst;
    public sealed record Or(PredicateAst Left, PredicateAst Right) : PredicateAst;
    public sealed record Not(PredicateAst Inner) : PredicateAst;
}

public enum ComparisonOp { Eq, NotEq, Gt, Lt, GtEq, LtEq }