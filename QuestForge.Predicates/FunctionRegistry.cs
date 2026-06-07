namespace QuestForge.Predicates;

using static PredicateType;
using static Arity;

public static class FunctionRegistry
{
    private static readonly FunctionSignature[] s_functions =
    [
        new("questSequence",             new Fixed(1),           [Int],           Int),
        new("questFlag",                 new Fixed(2),           [Int, Int],      Bool),
        new("questFlags",                new Fixed(1),           [Int],           Int),
        new("questFlagAny",              new VariadicMin(2),     [Int, Int],      Bool),
        new("questFlagAll",              new VariadicMin(2),     [Int, Int],      Bool),
        new("questFlagCount",            new VariadicMin(2),     [Int, Int],      Int),
        new("isQuestComplete",           new Fixed(1),           [Int],           Bool),
        new("isQuestAccepted",           new Fixed(1),           [Int],           Bool),
        new("isQuestAvailable",          new Fixed(1),           [Int],           Bool),
        new("playerZone",                new Fixed(0),           [],              Int),
        new("playerLevel",               new OptionalTail(0, 1), [String],        Int),
        new("playerHasItem",             new OptionalTail(1, 1), [Int, Int],      Bool),
        new("playerHasEquipped",         new OptionalTail(1, 1), [Int, String],   Bool),
        new("playerAverageItemLevel",    new Fixed(0),           [],              Int),
        new("playerNear",                new Fixed(2),           [Position, Int], Bool),
        new("playerStartingClass",       new Fixed(0),           [],              String),
        new("currentJob",                new Fixed(0),           [],              Int),
        new("playerJobId",               new Fixed(0),           [],              Int),
        new("isDiscipleOfWar",           new Fixed(0),           [],              Bool),
        new("isDiscipleOfMagic",         new Fixed(0),           [],              Bool),
        new("isPlayerJob",               new Fixed(1),           [Int],           Bool),
        new("inventoryFreeSlots",        new Fixed(0),           [],              Int),
        new("instanceKind",              new Fixed(0),           [],              String),
        new("playerInCombat",            new Fixed(0),           [],              Bool),
        new("playerDead",                new Fixed(0),           [],              Bool),
        new("interactableActive",        new Fixed(1),           [Int],           Bool),
        new("uiDialogueOpen",            new Fixed(0),           [],              Bool),
        new("gil",                       new Fixed(0),           [],              Int),
        new("playerLowestGearCondition", new Fixed(0),           [],              Int),
        new("gearsetExists",             new Fixed(1),           [String],        Bool),
        new("jobGearsetExists",          new Fixed(1),           [Int],           Bool),
        new("inNewGamePlus",             new Fixed(0),           [],              Bool),
        new("isAttuned",                 new Fixed(1),           [Int],           Bool),
        new("questVariable",             new Fixed(2),           [Int, Int],      Int),
        new("questVariableLow",          new Fixed(2),           [Int, Int],      Int),
        new("questVariableHigh",         new Fixed(2),           [Int, Int],      Int),
        new("inventoryHasCoffers",       new Fixed(0),           [],              Bool),
        new("isAetherCurrentAttuned",    new Fixed(1),           [Int],           Bool),
        new("npcExistsNearby",           new Fixed(1),           [Int],           Bool),
        new("objectExists",              new Fixed(1),           [Int],           Bool),
        new("objectExistsInRange",       new Fixed(2),           [Int, Int],      Bool),
        new("isSlotEquipped",            new Fixed(1),           [Int],           Bool),
    ];

    public static IReadOnlyDictionary<string, FunctionSignature> All { get; } =
        s_functions.ToDictionary(f => f.Name, StringComparer.Ordinal);

    public static bool TryGet(string name, out FunctionSignature sig) =>
        All.TryGetValue(name, out sig!);

    public static IReadOnlyList<string> SuggestSimilar(string name, int maxDistance = 2)
    {
        var results = new List<string>();
        foreach (var key in All.Keys)
            if (Levenshtein.Distance(name, key) <= maxDistance)
                results.Add(key);
        return results;
    }
}