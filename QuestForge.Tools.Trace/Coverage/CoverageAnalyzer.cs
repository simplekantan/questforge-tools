using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using QuestForge.Predicates;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Fixture;

namespace QuestForge.Tools.Trace.Coverage;

public sealed class CoverageAnalyzer
{
    private static readonly JsonSerializerOptions ReadOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly IReadOnlySet<string> StepTypes = DiscoverStepTypes();
    private static readonly IReadOnlySet<string> PredicateNames = DiscoverPredicates();

    private static IReadOnlySet<string> DiscoverStepTypes()
    {
        var attrs = typeof(Step).GetCustomAttributes<JsonDerivedTypeAttribute>();
        return attrs
            .Select(a => (string)a.TypeDiscriminator!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> DiscoverPredicates()
        => FunctionRegistry.All.Keys.ToHashSet(StringComparer.Ordinal);

    public CoverageReport Analyze(string questDataRoot)
    {
        var dir = Path.Combine(questDataRoot, "fixtures", "engine");

        var allCapabilities = new List<string>();
        var allActionTypes = new List<string>();

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var fixture = JsonSerializer.Deserialize<FixtureModel>(json, ReadOptions);
                    if (fixture == null) continue;

                    allCapabilities.AddRange(fixture.Capabilities);
                    allActionTypes.AddRange(fixture.ExpectedTransitions.Select(t => t.ActionType));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CoverageAnalyzer: skipping {file}: {ex.Message}");
                }
            }
        }

        return Analyze(allCapabilities, allActionTypes);
    }

    public CoverageReport Analyze(
        IReadOnlyList<string> fixtureCapabilities,
        IReadOnlyList<string> fixtureActionTypes)
    {
        var coveredSteps = new HashSet<string>(StringComparer.Ordinal);
        var coveredPredicates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cap in fixtureCapabilities)
        {
            if (cap.StartsWith("step:", StringComparison.Ordinal))
            {
                var name = cap.Substring(5);
                if (StepTypes.Contains(name))
                    coveredSteps.Add(name);
            }
            else if (cap.StartsWith("predicate:", StringComparison.Ordinal))
            {
                var name = cap.Substring(10);
                if (PredicateNames.Contains(name))
                    coveredPredicates.Add(name);
            }
        }

        var coveredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actionType in fixtureActionTypes)
        {
            if (KnownActionTypes.All.Contains(actionType))
                coveredActions.Add(actionType.ToLowerInvariant());
        }

        return new CoverageReport(
            Steps: BuildSection(StepTypes, coveredSteps),
            Predicates: BuildSection(PredicateNames, coveredPredicates),
            ActionTypes: BuildSection(KnownActionTypes.All, coveredActions));
    }

    private static CoverageSection BuildSection(IReadOnlySet<string> total, HashSet<string> covered)
    {
        var coveredItems = total
            .Where(t => covered.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var uncoveredItems = total
            .Where(t => !covered.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        int coveredCount = coveredItems.Count;
        int totalCount = total.Count;
        double percentage = totalCount == 0
            ? 100.0
            : Math.Round(100.0 * coveredCount / totalCount, 1);

        return new CoverageSection(
            Covered: coveredCount,
            Total: totalCount,
            Percentage: percentage,
            CoveredItems: coveredItems,
            UncoveredItems: uncoveredItems);
    }
}
