namespace QuestForge.Tools.Trace;

/// <summary>Shared string constants for trace action types and outcomes.</summary>
internal static class TraceConstants
{
    // Lowercase action types as emitted by DecisionEvent (canonical form).
    // DecisionEvent.ActionType uses action.GetType().Name (TitleCase); the extractor
    // ToLowerInvariant()s it before comparing. These constants must match what would
    // be produced by .GetType().Name.ToLowerInvariant() for each EngineAction subtype.
    internal const string ActionNavigate     = "navigate";
    internal const string ActionInteract     = "interact";
    internal const string ActionWait         = "wait";
    internal const string ActionDone         = "done";
    internal const string ActionAwaitUser    = "awaituser";   // lowercased from "AwaitUser"
    internal const string ActionAttune       = "attune";      // documented; AttunementStep currently dispatches via Interact
    internal const string ActionHandover     = "handover";
    internal const string ActionUseAethernet = "useaethernet";
    internal const string ActionTeleport     = "teleport";
    internal const string ActionPurchase     = "purchase";
    internal const string ActionUseAction    = "useaction";
    internal const string ActionUseEmote         = "useemote";
    internal const string ActionUseItem          = "useitem";         // lowercased from "UseItem"
    internal const string ActionSayChatMessage   = "saychatmessage";  // lowercased from "SayChatMessage"
    internal const string ActionEngage           = "engage";

    internal static bool IsTerminalAction(string actionTypeLower) =>
        actionTypeLower is ActionDone or ActionAwaitUser;

    // Allowed terminalOutcome values in FixtureModel (preserved casing from RunEndEvent.Outcome).
    internal static readonly HashSet<string> AllowedTerminalOutcomes = ["done", "awaitUser"];
}
