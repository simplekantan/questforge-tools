namespace QuestForge.Predicates;

internal enum TokenKind
{
    Identifier, NumberInt, NumberFloat, String,
    LParen, RParen, LBrace, RBrace, Comma, Colon,
    Eq, NotEq, Gt, Lt, GtEq, LtEq,
    KeywordAnd, KeywordOr, KeywordNot, KeywordDefault,
    ParameterRef, EndOfInput, Error
}