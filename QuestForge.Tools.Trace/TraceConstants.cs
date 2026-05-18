namespace QuestForge.Tools.Trace;

/// <summary>Shared string constants for trace action types and outcomes.</summary>
internal static class TraceConstants
{
    // Lowercase action types as emitted by DecisionEvent (canonical form).
    internal const string ActionNavigate    = "navigate";
    internal const string ActionInteract    = "interact";
    internal const string ActionWait        = "wait";
    internal const string ActionDone        = "done";
    internal const string ActionAwaitUser   = "awaituser";   // lowercased from "awaitUser"
    internal const string ActionAttune      = "attune";
    internal const string ActionHandover    = "handover";
    internal const string ActionUseAethernet = "useaethernet";

    internal static bool IsTerminalAction(string actionTypeLower) =>
        actionTypeLower is ActionDone or ActionAwaitUser;

    // Allowed terminalOutcome values in FixtureModel (preserved casing from RunEndEvent.Outcome).
    internal static readonly HashSet<string> AllowedTerminalOutcomes = ["done", "awaitUser"];
}
