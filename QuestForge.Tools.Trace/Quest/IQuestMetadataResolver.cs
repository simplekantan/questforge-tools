namespace QuestForge.Tools.Trace.Quest;

public interface IQuestMetadataResolver
{
    QuestMetadata? ResolveQuest(uint questId);
    string? ResolveNpcName(uint npcId);
    string? ResolveZoneName(uint zoneId);
    string? ResolveJobAbbreviation(uint jobId);
}

public sealed record QuestMetadata(
    string Name,
    string Expansion,
    string Category,
    int? MinLevel,
    string? RequiredJob,
    uint[] PrerequisiteQuestIds);
