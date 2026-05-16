using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Capabilities;

namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Converts a list of parsed <see cref="TraceEvent"/>s into a <see cref="FixtureModel"/>
/// suitable for committing as a regression fixture.
/// </summary>
public sealed class TraceToFixtureExtractor
{
    private readonly string? _questDataRoot;

    // Static lookup: sorted set of step-type capabilities → canonical filename
    private static readonly (string[] StepCaps, string Filename)[] FilenameLookup =
    [
        (["step:talk", "step:travel"], "simple-linear-acceptance.json"),
        (["step:duty"], "with-dungeon.json"),
        (["step:branch"], "with-branching.json"),
        (["step:fragment"], "with-fragments.json"),
        (["step:spd"], "with-spd.json"),
    ];

    public TraceToFixtureExtractor(string? questDataRoot = null)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Extract a fixture from the supplied event list.
    /// Returns <see cref="Result{T}.Failure"/> with code <c>"no-run-start"</c> when the
    /// list contains no <see cref="RunStartEvent"/>.
    /// </summary>
    public Result<FixtureModel> Extract(IReadOnlyList<TraceEvent> events)
    {
        // Step 1: find RunStartEvent
        var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
        if (runStart == null)
            return Result.Fail<FixtureModel>("no-run-start", "trace contains no run.start event");

        var runId = runStart.RunId;
        var questId = runStart.QuestId;

        // Step 2-4: build transitions from DecisionEvents
        var transitions = new List<TransitionEntry>();
        TransitionEntry? lastAppended = null;

        foreach (var ev in events)
        {
            if (ev is not DecisionEvent decision) continue;
            if (decision.RunId != runId) continue;

            var actionType = decision.ActionType.ToLowerInvariant();

            // Skip terminal actions
            if (actionType == "done" || actionType == "awaituser") continue;

            var entry = new TransitionEntry(decision.StepId, actionType);

            // Deduplicate consecutive identical pairs
            if (lastAppended == null ||
                lastAppended.StepId != entry.StepId ||
                lastAppended.ActionType != entry.ActionType)
            {
                transitions.Add(entry);
                lastAppended = entry;
            }
        }

        // Step 5: find last RunEndEvent
        var runEnd = events.OfType<RunEndEvent>().LastOrDefault(e => e.RunId == runId);
        string? terminalOutcome = runEnd?.Outcome; // as-is, do not lowercase

        // Step 6: resolve questFile
        string questFile;
        IReadOnlyList<string> capabilities = [];
        string description = "TODO: add description";

        if (_questDataRoot == null)
        {
            questFile = $"quests/UNKNOWN/{questId}.json";
        }
        else
        {
            questFile = ResolveQuestFile(_questDataRoot, questId);

            // Step 7: compute capabilities if quest file found
            var fullPath = Path.Combine(_questDataRoot, questFile);
            if (File.Exists(fullPath))
            {
                try
                {
                    var json = File.ReadAllText(fullPath);
                    var quest = JsonSerializer.Deserialize<QuestDefinition>(json, QuestForgeJsonContext.QuestFileOptions);
                    if (quest != null)
                        capabilities = CapabilityInferrer.Infer(quest);
                }
                catch
                {
                    // If quest file is unreadable, capabilities stay empty
                }
            }
        }

        var fixture = new FixtureModel(
            SchemaVersion: "1.0.0",
            Description: description,
            InitialState: "fresh",
            Capabilities: capabilities,
            QuestFile: questFile,
            ExpectedTransitions: transitions,
            TerminalOutcome: terminalOutcome);

        return Result.Ok(fixture);
    }

    private static string ResolveQuestFile(string questDataRoot, uint questId)
    {
        var questsDir = Path.Combine(questDataRoot, "quests");
        if (!Directory.Exists(questsDir))
            return $"quests/UNKNOWN/{questId}.json";

        // Search for {questId}-*.json recursively
        var pattern = $"{questId}-*.json";
        var matches = Directory.GetFiles(questsDir, pattern, SearchOption.AllDirectories);

        if (matches.Length > 0)
        {
            var match = matches[0];
            // Make relative to questDataRoot with forward slashes
            var relative = Path.GetRelativePath(questDataRoot, match)
                              .Replace(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        // Try exact {questId}.json
        var exact = Directory.GetFiles(questsDir, $"{questId}.json", SearchOption.AllDirectories);
        if (exact.Length > 0)
        {
            var relative = Path.GetRelativePath(questDataRoot, exact[0])
                              .Replace(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        return $"quests/UNKNOWN/{questId}.json";
    }

    /// <summary>
    /// Suggest a canonical filename for the given fixture based on the capability set it exercises.
    /// Falls back to <c>"simple-linear-acceptance.json"</c> when no mapping is found.
    /// </summary>
    public string SuggestFilename(FixtureModel fixture)
    {
        // Extract only step: capabilities, sorted
        var stepCaps = fixture.Capabilities
            .Where(c => c.StartsWith("step:"))
            .OrderBy(c => c)
            .ToArray();

        foreach (var (caps, filename) in FilenameLookup)
        {
            if (stepCaps.SequenceEqual(caps))
                return filename;
        }

        return "simple-linear-acceptance.json";
    }
}
