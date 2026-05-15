namespace QuestForge.Predicates;

internal readonly record struct Token(TokenKind Kind, string Text, int Column);