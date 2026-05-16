using System.Diagnostics;
using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Capabilities;

namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Enumerates committed fixture files and computes capability coverage gaps.
/// </summary>
public sealed class ListFixturesCommand
{
    // Cached per JsonSerializerOptions guidance — metadata is built once and reused.
    private static readonly JsonSerializerOptions FixtureReadOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _questDataRoot;

    public ListFixturesCommand(string questDataRoot)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Enumerate all fixture files under <c>fixtures/engine/*.json</c> relative to
    /// <see cref="_questDataRoot"/>.
    /// </summary>
    public IReadOnlyList<FixtureListEntry> Enumerate()
    {
        var dir = Path.Combine(_questDataRoot, "fixtures", "engine");
        if (!Directory.Exists(dir))
            return [];

        var results = new List<FixtureListEntry>();

        // Sorted for deterministic output across filesystems.
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var json = File.ReadAllText(file);
                var fixture = JsonSerializer.Deserialize<FixtureModel>(json, FixtureReadOptions);
                if (fixture == null) continue;

                var filename = Path.GetFileName(file);
                var fixtureFile = $"fixtures/engine/{filename}";
                var questFileExists = File.Exists(Path.Combine(_questDataRoot, fixture.QuestFile));

                results.Add(new FixtureListEntry(fixtureFile, fixture.Capabilities, questFileExists, fixture.QuestFile));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ListFixturesCommand: skipping {file}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Compute capability tags that appear in any quest in the data root but are not
    /// covered by any fixture in <paramref name="fixtures"/>.
    /// </summary>
    public IReadOnlyList<string> ComputeGaps(IReadOnlyList<FixtureListEntry> fixtures)
    {
        var questsDir = Path.Combine(_questDataRoot, "quests");
        var allQuestCaps = new HashSet<string>();

        if (Directory.Exists(questsDir))
        {
            foreach (var file in Directory.GetFiles(questsDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var quest = JsonSerializer.Deserialize<QuestDefinition>(json, QuestForgeJsonContext.QuestFileOptions);
                    if (quest == null) continue;

                    foreach (var cap in CapabilityInferrer.Infer(quest))
                        allQuestCaps.Add(cap);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ListFixturesCommand.ComputeGaps: skipping {file}: {ex.Message}");
                }
            }
        }

        var coveredCaps = fixtures.SelectMany(f => f.Capabilities).ToHashSet();

        return allQuestCaps
            .Except(coveredCaps)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
    }
}
