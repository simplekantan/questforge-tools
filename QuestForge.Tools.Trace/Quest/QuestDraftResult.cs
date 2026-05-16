using QuestForge.Schema;

namespace QuestForge.Tools.Trace.Quest;

/// <summary>
/// The result of extracting a quest draft from a trace.
/// <see cref="Definition"/> is suitable for serialization with
/// <see cref="QuestForgeJsonContext.QuestFileOptions"/>.
/// <see cref="Todos"/> is an ordered list of items the author must fill in manually.
/// </summary>
public sealed record QuestDraftResult(
    QuestDefinition Definition,
    IReadOnlyList<string> Todos);
