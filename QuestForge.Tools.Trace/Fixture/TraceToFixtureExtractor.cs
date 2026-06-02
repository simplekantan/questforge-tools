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
        // Existing five mappings (unchanged)
        (["step:talk", "step:travel"], "simple-linear-acceptance.json"),
        (["step:duty"], "with-dungeon.json"),
        (["step:branch"], "with-branching.json"),
        (["step:fragment"], "with-fragments.json"),
        (["step:spd"], "with-spd.json"),
        // New single-shape exact-match entries
        (["step:accept", "step:talk", "step:travel"], "with-accept.json"),
        (["step:talk", "step:travel", "step:turn-in"], "with-turn-in.json"),
        (["step:attune", "step:travel"], "with-attunement.json"),
        (["step:hand-over-item", "step:talk", "step:travel"], "with-hand-over-item.json"),
        (["step:interact-object", "step:travel"], "with-interact-object.json"),
        (["step:pickup-item", "step:travel"], "with-pickup-item.json"),
        // Catch-up: newer step shapes (PR #100, #104, #95 et al.)
        (["step:talk", "step:travel", "step:use-action"], "with-use-action.json"),
        (["step:talk", "step:travel", "step:use-emote"], "with-use-emote.json"),
        (["step:talk", "step:travel", "step:use-item"], "with-use-item.json"),
        (["step:say-chat-message", "step:talk", "step:travel"], "with-say-chat-message.json"),
        (["step:change-job", "step:talk", "step:travel"], "with-change-job.json"),
        (["step:equip-best-gear", "step:talk", "step:travel"], "with-equip-best-gear.json"),
        (["step:equip-gear-for-quest", "step:talk", "step:travel"], "with-equip-gear-for-quest.json"),
        (["step:talk", "step:teleport", "step:travel"], "with-teleport.json"),
        (["step:purchase-item", "step:talk", "step:travel"], "with-purchase-item.json"),
        (["step:register-gearset", "step:talk", "step:travel"], "with-register-gearset.json"),
        (["step:open-coffers", "step:talk", "step:travel"], "with-open-coffers.json"),
        (["step:dungeon-trial"], "with-dungeon-trial.json"),
    ];

    // Priority-ordered list of distinguishing capabilities for the multi-shape fallback (§3.7).
    // When no exact set matches, the highest-priority cap present in the fixture determines
    // the suggested filename.
    private static readonly (string Cap, string Filename)[] DistinguishingCapPriority =
    [
        ("step:dungeon-trial",  "with-dungeon-trial.json"),
        ("step:duty",           "with-dungeon.json"),
        ("step:spd",            "with-spd.json"),
        ("step:branch",         "with-branching.json"),
        ("step:fragment",       "with-fragments.json"),
        ("step:attune",         "with-attunement.json"),
        ("step:hand-over-item", "with-hand-over-item.json"),
        ("step:turn-in",        "with-turn-in.json"),
        ("step:accept",         "with-accept.json"),
        ("step:interact-object","with-interact-object.json"),
        ("step:pickup-item",    "with-pickup-item.json"),
        // Catch-up: newer step shapes. Action precedes emote (combat actions are more
        // distinguishing for fixture identity than emotes). Teleport and purchase-item
        // rank below since they're less "shape-defining" within a multi-shape quest.
        ("step:use-action",     "with-use-action.json"),
        ("step:use-emote",      "with-use-emote.json"),
        ("step:use-item",       "with-use-item.json"),
        ("step:say-chat-message", "with-say-chat-message.json"),
        ("step:equip-gear-for-quest", "with-equip-gear-for-quest.json"),
        ("step:equip-best-gear",      "with-equip-best-gear.json"),
        ("step:change-job",           "with-change-job.json"),
        ("step:register-gearset",     "with-register-gearset.json"),
        ("step:open-coffers",         "with-open-coffers.json"),
        ("step:teleport",       "with-teleport.json"),
        ("step:purchase-item",  "with-purchase-item.json"),
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
        var questId = runStart.Data.QuestId;

        // Step 2-4: build transitions from DecisionEvents
        var transitions = new List<TransitionEntry>();
        TransitionEntry? lastAppended = null;

        foreach (var ev in events)
        {
            if (ev is not DecisionEvent decision) continue;
            if (decision.RunId != runId) continue;

            var actionType = decision.Data.ActionType.ToLowerInvariant();

            // Skip terminal actions
            if (TraceConstants.IsTerminalAction(actionType)) continue;

            var entry = new TransitionEntry(decision.Data.StepId, actionType);

            // Deduplicate consecutive identical pairs
            if (lastAppended == null ||
                lastAppended.StepId != entry.StepId ||
                lastAppended.ActionType != entry.ActionType)
            {
                transitions.Add(entry);
                lastAppended = entry;
            }
        }

        // Step 5: find RunEndEvent — prefer a real engine terminal outcome ("done"/"awaitUser")
        // over the EngineHost cleanup marker ("ended") which may follow it.
        var runEnds = events.OfType<RunEndEvent>().Where(e => e.RunId == runId).ToList();
        string? terminalOutcome = runEnds.FirstOrDefault(e => e.Data.Outcome is "done" or "awaitUser")?.Data.Outcome
                                  ?? runEnds.LastOrDefault()?.Data.Outcome; // do not lowercase

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
    /// Returns only the events belonging to the first <see cref="RunStartEvent"/>'s run,
    /// preserving document order. Events with a different or absent RunId are dropped.
    /// Returns an empty list when <paramref name="events"/> contains no <see cref="RunStartEvent"/>.
    /// </summary>
    public static IReadOnlyList<TraceEvent> FilterToFixtureRun(IReadOnlyList<TraceEvent> events)
    {
        var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
        if (runStart == null)
            return [];

        var runId = runStart.RunId;
        return events.Where(e => GetRunId(e) == runId).ToList();
    }

    private static string? GetRunId(TraceEvent e) => e switch
    {
        RunStartEvent           r => r.RunId,
        RunEndEvent             r => r.RunId,
        ObservationEvent        r => r.RunId,
        DecisionEvent           r => r.RunId,
        ActionSubmittedEvent    r => r.RunId,
        ActionCompletedEvent    r => r.RunId,
        StepRecordedEvent       r => r.RunId,
        _                         => null,
    };

    /// <summary>
    /// Suggest a canonical filename for the given fixture based on the capability set it exercises.
    /// First tries an exact match against FilenameLookup. When no exact match exists, falls back
    /// to the highest-priority distinguishing capability in DistinguishingCapPriority (§3.7).
    /// Final fallback: <c>"simple-linear-acceptance.json"</c>.
    /// </summary>
    public string SuggestFilename(FixtureModel fixture)
    {
        // Extract only step: capabilities, sorted
        var stepCaps = fixture.Capabilities
            .Where(c => c.StartsWith("step:"))
            .OrderBy(c => c)
            .ToArray();

        // (1) Exact match
        foreach (var (caps, filename) in FilenameLookup)
        {
            if (stepCaps.SequenceEqual(caps))
                return filename;
        }

        // (2) Best distinguishing capability fallback
        var stepCapsSet = new HashSet<string>(stepCaps, StringComparer.Ordinal);
        foreach (var (cap, filename) in DistinguishingCapPriority)
        {
            if (stepCapsSet.Contains(cap))
                return filename;
        }

        return "simple-linear-acceptance.json";
    }
}
