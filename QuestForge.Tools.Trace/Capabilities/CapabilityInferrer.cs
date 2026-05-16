using QuestForge.Schema;

namespace QuestForge.Tools.Trace.Capabilities;

/// <summary>
/// Infers the capability tags exercised by a <see cref="QuestDefinition"/>.
/// Tags follow the pattern <c>step:&lt;discriminator&gt;</c>, <c>predicate:&lt;name&gt;</c>,
/// and <c>engine:&lt;feature&gt;</c>.
/// Returns a sorted, de-duplicated list.
/// </summary>
public static class CapabilityInferrer
{
    /// <summary>
    /// Walk every step in every sequence and produce a sorted, de-duplicated capability list.
    /// </summary>
    public static IReadOnlyList<string> Infer(QuestDefinition quest)
        => throw new NotImplementedException();
}
