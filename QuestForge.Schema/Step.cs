using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestForge.Schema;

// ---------------------------------------------------------------------------
// Step base class — all 20 step types inherit from this.
// Uses [JsonPolymorphic] with "type" as the discriminator.
// ---------------------------------------------------------------------------

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TravelStep),            "travel")]
[JsonDerivedType(typeof(TalkStep),              "talk")]
[JsonDerivedType(typeof(InteractObjectStep),    "interact-object")]
[JsonDerivedType(typeof(PickupItemStep),        "pickup-item")]
[JsonDerivedType(typeof(AcceptStep),            "accept")]
[JsonDerivedType(typeof(TurnInStep),            "turn-in")]
[JsonDerivedType(typeof(CombatStep),            "combat")]
[JsonDerivedType(typeof(DutyStep),              "duty")]
[JsonDerivedType(typeof(CutsceneStep),          "cutscene")]
[JsonDerivedType(typeof(SayChatMessageStep),    "say-chat-message")]
[JsonDerivedType(typeof(UseEmoteStep),          "use-emote")]
[JsonDerivedType(typeof(UseItemStep),           "use-item")]
[JsonDerivedType(typeof(UseActionStep),         "use-action")]
[JsonDerivedType(typeof(EquipGearForQuestStep), "equip-gear-for-quest")]
[JsonDerivedType(typeof(EquipBestGearStep),     "equip-best-gear")]
[JsonDerivedType(typeof(ChangeJobStep),         "change-job")]
[JsonDerivedType(typeof(MinigameStep),          "minigame")]
[JsonDerivedType(typeof(AwaitUserStep),         "await-user")]
[JsonDerivedType(typeof(BranchStep),            "branch")]
[JsonDerivedType(typeof(FragmentStep),          "fragment")]
[JsonDerivedType(typeof(AttunementStep),        "attune")]
[JsonDerivedType(typeof(HandOverItemStep),      "hand-over-item")]
[JsonDerivedType(typeof(WaitStep),              "wait")]
[JsonDerivedType(typeof(PurchaseItemStep),      "purchase-item")]
[JsonDerivedType(typeof(RegisterGearsetStep),   "register-gearset")]
[JsonDerivedType(typeof(OpenCoffersStep),       "open-coffers")]
public class Step
{
    public string Id { get; init; } = default!;
    public string? Zone { get; init; }
    public string? RequiredZone { get; init; }
    public string? ResumePointFragmentId { get; init; }
    public ExpectValue? Expect { get; init; }
    public ExpectValue? SkipIf { get; init; }
    public float? StopDistance { get; init; }
    public RecoverConfig? Recover { get; init; }
    public RetryConfig? Retry { get; init; }
    public Preconditions? Preconditions { get; init; }
    public string? Notes { get; init; }
}

// ---------------------------------------------------------------------------
// Concrete step types
// ---------------------------------------------------------------------------

public class TravelStep : Step
{
    public TravelDestination Destination { get; init; } = default!;
    public RouteHint? RouteHint { get; init; }
}

public class TalkStep : Step
{
    public NpcLocation? Target { get; init; }
    public NpcLocation[]? Targets { get; init; }
    public string? TargetOrder { get; init; }   // "sequential" | "any" | "nearest-first"
    public DialogueChoice[] DialogueChoices { get; init; } = [];
}

public sealed class InteractObjectStep : Step
{
    public uint InteractableId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Position3? Position { get; init; }
}

public sealed class PickupItemStep : Step
{
    public uint InteractableId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Position3? Position { get; init; }
}

public class AcceptStep : Step
{
    public NpcLocation Target { get; init; } = default!;
}

public class TurnInStep : Step
{
    public NpcLocation Target { get; init; } = default!;
    public DialogueChoice[] DialogueChoices { get; init; } = [];
}

public class CombatStep : Step
{
    public uint[] KillEnemyDataIds { get; init; } = [];
    public CombatSpawn Spawn { get; init; } = CombatSpawn.AutoOnEnterArea;
    public NpcLocation? Location { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<CombatSpawn>))]
public enum CombatSpawn
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("autoOnEnterArea")]
    AutoOnEnterArea,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("overworldEnemies")]
    OverworldEnemies
}

public sealed class DutyStep : Step
{
    public string Kind { get; init; } = default!;   // "regular" | "spd" | "duty"

    /// <summary>
    /// ContentFinderCondition row ID (Lumina). Required for kind "duty".
    /// The adapter derives territory type from this ID for AutoDuty's IPC.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? ContentFinderConditionId { get; init; }
}

public class CutsceneStep : Step
{
    public string Skip { get; init; } = "ifAllowed";   // "never" | "ifAllowed"
}

// SayChatMessageStep — synced with questforge/QuestForge.Schema/Step.cs
// OLD shape: { Channel, Message, Target: NpcLocation? } — REPLACED
public sealed class SayChatMessageStep : Step
{
    public string Message { get; init; } = default!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TargetNpcId { get; init; }
}

// UseEmoteStep — synced with questforge/QuestForge.Schema/Step.cs
// OLD shape: { EmoteId, Target: NpcLocation? } — REPLACED (added TargetNpcId, Motion)
public sealed class UseEmoteStep : Step
{
    public uint EmoteId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TargetNpcId { get; init; }
    public bool Motion { get; init; } = true;
}

// UseItemStep — synced with questforge/QuestForge.Schema/Step.cs
// OLD shape: { ItemId, Target: UseItemTarget? } — REPLACED (UseItemTarget DELETED)
// TODO: ItemKind enum must be added to SharedValueTypes.cs
public sealed class UseItemStep : Step
{
    public ItemKind Kind { get; init; }     // TODO: ItemKind enum
    public uint ItemId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TargetNpcId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Position3? TargetPosition { get; init; }
}

// UseActionStep — synced with questforge/QuestForge.Schema/Step.cs
// OLD shape: { ActionId, Target: ActionTarget, RepeatUntilExpect } — REPLACED
// TODO: ActionType enum must be added to SharedValueTypes.cs
public sealed class UseActionStep : Step
{
    public ActionType ActionType { get; init; }     // TODO: ActionType enum
    public uint ActionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TargetNpcId { get; init; }
}

public sealed class EquipGearForQuestStep : Step
{
    /// <summary>
    /// Item IDs to equip. The adapter determines the target slot from each item's
    /// EquipSlotCategory (Lumina lookup). For rings, the adapter picks the first
    /// available ring slot.
    /// </summary>
    public uint[] ItemIds { get; init; } = [];
}

public sealed class EquipBestGearStep : Step { }

public sealed class ChangeJobStep : Step
{
    /// <summary>
    /// ClassJob row ID from the game's ClassJob sheet.
    /// Example: 32 = Dark Knight, 19 = Paladin, 24 = White Mage.
    /// </summary>
    public uint JobId { get; init; }
}

public class MinigameStep : Step
{
    public string Kind { get; init; } = default!;   // "sniping" | "memory" | "aiming" | "rhythm" | "selection" | "other"
    public string Skip { get; init; } = "ifAllowed"; // "never" | "ifAllowed" | "always"
}

public class AwaitUserStep : Step
{
    public string Reason { get; init; } = default!;   // ≤200 chars
}

public class BranchStep : Step
{
    public BranchCase[] Branches { get; init; } = [];
}

public class FragmentStep : Step
{
    public string Ref { get; init; } = default!;
    public Dictionary<string, JsonElement>? Params { get; init; }
}

public class AttunementStep : Step
{
    /// <summary>The aetheryte or aethernet shard to attune to.</summary>
    public AetheryteId Target { get; init; }

    /// <summary>
    /// Optional world-space position of the aetheryte or shard.
    /// When present, the engine uses implied navigation (same as talk/accept/turn-in):
    /// if the player is beyond StopDistance, it emits Navigate first.
    /// When absent, the engine emits Interact directly — author a preceding TravelStep
    /// to ensure the player is close enough.
    /// </summary>
    public NpcLocation? Location { get; init; }
}

public class HandOverItemStep : Step
{
    public NpcLocation Target { get; init; } = default!;
    public uint[] Items { get; init; } = [];
}

public class WaitStep : Step
{
    /// <summary>Wall-clock seconds to wait before the step completes. Must be &gt; 0.</summary>
    public double Seconds { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseCurrency>))]
public enum PurchaseCurrency
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("gil")]
    Gil,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("gcSeals")]
    GcSeals
}

public class PurchaseItemStep : Step
{
    public NpcLocation Target { get; init; } = default!;
    public uint ItemId { get; init; }
    public int Quantity { get; init; } = 1;
    public PurchaseCurrency Currency { get; init; } = PurchaseCurrency.Gil;
    /// <summary>GC shop category tab index (0=Weapons, 1=Armor, 2=Materiel, 3=Materials); valid range 0..3.</summary>
    public int? GcCategory { get; init; }
    /// <summary>GC rank tier row within the selected category (0=lowest, 2=highest); valid range 0..2.</summary>
    public int? GcRankTier { get; init; }
}

public sealed class RegisterGearsetStep : Step { }

public sealed class OpenCoffersStep : Step { }