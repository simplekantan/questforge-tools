namespace QuestForge.Tools.Trace.Quest;

public sealed class NullMetadataResolver : IQuestMetadataResolver
{
    public static readonly NullMetadataResolver Instance = new();
    private NullMetadataResolver() { }

    public QuestMetadata? ResolveQuest(uint questId) => null;
    public string? ResolveNpcName(uint npcId) => null;
    public string? ResolveZoneName(uint zoneId) => null;
    public string? ResolveJobAbbreviation(uint jobId) => null;
}
