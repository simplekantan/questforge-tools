using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

/// <summary>Minimal IFragmentParameterScope for tests.</summary>
internal sealed class TestScope : IFragmentParameterScope
{
    private readonly Dictionary<string, PredicateType> _params;

    public TestScope(params (string name, PredicateType type)[] parameters)
        => _params = parameters.ToDictionary(p => p.name, p => p.type);

    public bool TryGetParameterType(string name, out PredicateType type)
        => _params.TryGetValue(name, out type);
}