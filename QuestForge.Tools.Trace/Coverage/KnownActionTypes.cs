namespace QuestForge.Tools.Trace.Coverage;

public static class KnownActionTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "navigate", "interact", "interactobject", "handover", "purchase",
        "useaethernet", "teleport", "wait", "awaituser", "done",
        "engage", "equipgear", "equipbestgear", "changejob",
        "registergearset", "opencoffer", "useaction", "useemote",
        "useitem", "saychatmessage", "attune",
        "entersingleplayerduty", "enterduty",
    };
}
