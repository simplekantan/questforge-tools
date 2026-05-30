using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestForge.Schema;

// ---------------------------------------------------------------------------
// Shared value types — immutable records used across quest and fragment types.
// ---------------------------------------------------------------------------

// ActionType — synced with questforge/QuestForge.Schema/SharedValueTypes.cs
// Used by UseActionStep to discriminate the game-side ActionManager call.
// TODO: [JsonSerializable(typeof(ActionType))] must be registered in QuestForgeJsonContext
[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("action")]
    Action,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("generalAction")]
    GeneralAction,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("keyItem")]
    KeyItem
}

// ItemKind — synced with questforge/QuestForge.Schema/SharedValueTypes.cs (NEW)
// Discriminates game-side ActionManager call for UseItemStep:
//   KeyItem       → ActionManager.UseAction(ActionType.EventItem, itemId, ...)
//   InventoryItem → ActionManager.UseAction(ActionType.Item, itemId, ...)
// TODO: [JsonSerializable(typeof(ItemKind))] must be registered in QuestForgeJsonContext
[JsonConverter(typeof(JsonStringEnumConverter<ItemKind>))]
public enum ItemKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("keyItem")]
    KeyItem,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("inventoryItem")]
    InventoryItem
}

// Schema-side AetheryteId alias. Lives here (not in Adapters) to keep Schema as a leaf
// with no upward dependency.
public readonly record struct AetheryteId(uint Value);

public record Position3(float X, float Y, float Z);

public record NpcLocation(uint NpcId, int Zone, Position3 Position);

public record TravelDestination(int Zone, Position3? Position = null, uint? AetheryteId = null);

public record AethernetRouteHint(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] uint? From,
    uint To);

/// <summary>
/// Describes NPC-mediated zone travel (e.g. a Lift Attendant). Target.Zone is the SOURCE
/// zone where the NPC resides; TravelStep.Destination.Zone is the DESTINATION zone.
/// </summary>
public record NpcDialogueHint
{
    [JsonConstructor]
    public NpcDialogueHint(NpcLocation Target)
    {
        this.Target = Target;
    }

    public NpcLocation Target { get; init; }

    /// <summary>
    /// Ordered list of list-type dialogue choices to drive the NPC menu.
    /// Empty array when not supplied at construction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DialogueChoice[] DialogueChoices { get; init; } = [];

    /// <summary>
    /// Convenience constructor that also accepts dialogue choices.
    /// </summary>
    public NpcDialogueHint(NpcLocation target, DialogueChoice[]? dialogueChoices)
        : this(target)
    {
        DialogueChoices = dialogueChoices ?? [];
    }
}

public record RouteHint(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Aetheryte = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AethernetRouteHint? Aethernet = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NpcDialogueHint? NpcDialogue = null);

/// <summary>A single dialogue interaction. Type: "list" | "yesno" | "talk".</summary>
public record DialogueChoice(string Type, string? Prompt = null, string? Answer = null);

public record DutyTrigger(
    string Kind,        // "npc" | "object"
    int Zone,
    Position3 Position,
    uint? NpcId = null,
    uint? InteractableId = null);

public record BranchCase(string When, Step[] Steps = default!);

public record ChainNext(string When, uint? QuestId);

public record PrerequisiteRef(uint QuestId, string State);  // "complete" | "accepted"

public record FragmentParameter(string Name, string Type, bool Required = true);
// Type: "position" | "npcId" | "itemId" | "string"

public record RetryConfig(int? MaxAttempts = null, int? Timeout = null, string? Backoff = null);

public record Preconditions(int? MinGearCondition = null);

public record RewardOverride(string Strategy, uint? ItemId = null);

public record GearItem(string Slot, uint ItemId);
// Slot: "mainhand" | "offhand" | "head" | "body" | "hands" | "legs" | "feet" |
//       "earrings" | "necklace" | "bracelets" | "ringR" | "ringL" | "soul"

public record GearConstraints(int? MinItemLevel = null);

// UseItemTarget — DELETED (Decision UI1: replaced by flat TargetNpcId + TargetPosition on UseItemStep)
// ActionTarget  — DELETED (schema-drift sync: UseActionStep now carries ActionType + TargetNpcId directly)

/// <summary>Target for interact-object and pickup-item steps.</summary>
public record InteractableTarget(uint InteractableId, int Zone, Position3 Position);

// CombatTarget (was: nearestHostile / specificNpc / wave) — retired in part A.
// Combat step completion is now modelled via Step.Expect + CombatStep.KillEnemyDataIds + CombatSpawn.
// No schema-version bump — early dev, no existing authored quests used this type.