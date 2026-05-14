using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

public interface IFragmentRegistry
{
    bool TryGetFragment(string fragmentRef, out FragmentDefinition? fragment);
    IReadOnlyCollection<string> AllFragmentRefs { get; }
}

/// <summary>In-memory implementation for unit tests.</summary>
public sealed class InMemoryFragmentRegistry : IFragmentRegistry
{
    private readonly Dictionary<string, FragmentDefinition> _map;

    public InMemoryFragmentRegistry(IEnumerable<FragmentDefinition>? fragments = null)
    {
        _map = (fragments ?? []).ToDictionary(f => f.FragmentId);
    }

    public bool TryGetFragment(string fragmentRef, out FragmentDefinition? fragment)
        => _map.TryGetValue(fragmentRef, out fragment);

    public IReadOnlyCollection<string> AllFragmentRefs => _map.Keys;
}

/// <summary>Empty registry — no fragments exist. Use in tests that don't exercise fragments.</summary>
public sealed class EmptyFragmentRegistry : IFragmentRegistry
{
    public static readonly EmptyFragmentRegistry Instance = new();
    public bool TryGetFragment(string fragmentRef, out FragmentDefinition? fragment) { fragment = null; return false; }
    public IReadOnlyCollection<string> AllFragmentRefs => [];
}