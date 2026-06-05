using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using QuestForge.Predicates;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Coverage;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Coverage;

/// <summary>
/// RED-phase tests for <see cref="CoverageAnalyzer"/> and <see cref="CoverageReport"/>.
/// All tests will fail until Builder implements the production types in
/// QuestForge.Tools.Trace/Coverage/.
///
/// Scenarios T1-T13 from COVERAGE_REPORT_PLAN.md.
/// T14-T17 are CLI integration tests and are excluded from this file.
/// </summary>
public sealed class CoverageAnalyzerTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static CoverageReport Analyze(
        IReadOnlyList<string> capabilities,
        IReadOnlyList<string> actionTypes)
    {
        var analyzer = new CoverageAnalyzer();
        return analyzer.Analyze(capabilities, actionTypes);
    }

    private static string FormatReport(CoverageReport r) =>
        $"Steps={r.Steps.Covered}/{r.Steps.Total} " +
        $"Predicates={r.Predicates.Covered}/{r.Predicates.Total} " +
        $"Actions={r.ActionTypes.Covered}/{r.ActionTypes.Total} " +
        $"Overall={r.OverallPercentage}%";

    // =========================================================================
    // T1 -- Step type discovery matches Schema
    // =========================================================================

    [Fact]
    public void T1_StepTypeDiscovery_MatchesSchemaReflection()
    {
        /*
         * CONTRACT: Given the Step class has N [JsonDerivedType] attributes,
         *           When CoverageAnalyzer discovers step types via reflection,
         *           Then the discovered set has exactly N entries and includes
         *                known discriminators: "travel", "talk", "open-coffers",
         *                "hand-over-item".
         *
         * BUILDER GUIDANCE: Use typeof(Step).GetCustomAttributes<JsonDerivedTypeAttribute>()
         *                   to extract discriminator strings. The count is validated
         *                   dynamically against what Schema actually declares.
         */

        // Arrange -- reflect on Step to get the expected count
        int expectedCount = typeof(Step)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Count();

        // A sanity floor: we know there are at least 26 step types today
        Assert.True(expectedCount >= 26,
            $"Expected at least 26 [JsonDerivedType] attributes on Step, found {expectedCount}");

        // Act -- analyze with no fixtures; Steps.Total reveals the discovered count
        var report = Analyze([], []);

        // Assert
        Assert.Equal(expectedCount, report.Steps.Total);
        Assert.Contains("travel", report.Steps.UncoveredItems);
        Assert.Contains("talk", report.Steps.UncoveredItems);
        Assert.Contains("open-coffers", report.Steps.UncoveredItems);
        Assert.Contains("hand-over-item", report.Steps.UncoveredItems);
    }

    // =========================================================================
    // T2 -- KnownActionTypes is a superset of TraceConstants
    // =========================================================================

    [Fact]
    public void T2_KnownActionTypes_SupersetOfTraceConstants()
    {
        /*
         * CONTRACT: Given TraceConstants defines Action* string constants,
         *           When each constant value is checked against KnownActionTypes.All,
         *           Then every constant value is present in KnownActionTypes.All.
         *
         * BUILDER GUIDANCE: KnownActionTypes is internal. Add
         *                   [assembly: InternalsVisibleTo("QuestForge.Tools.Trace.Tests")]
         *                   to the QuestForge.Tools.Trace project (csproj or AssemblyInfo).
         *                   This test uses reflection to extract TraceConstants.Action*
         *                   field values and checks each against KnownActionTypes.All.
         */

        // Arrange -- reflect on TraceConstants to get all Action* constant values
        var traceConstantsType = typeof(TraceConstants);
        var actionFields = traceConstantsType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.Name.StartsWith("Action") && f.IsLiteral)
            .ToList();

        Assert.True(actionFields.Count >= 20,
            $"Expected at least 20 Action* constants in TraceConstants, found {actionFields.Count}");

        // Act & Assert -- every TraceConstants.Action* value must be in KnownActionTypes.All
        var knownActions = KnownActionTypes.All;
        var missing = new List<string>();

        foreach (var field in actionFields)
        {
            var value = (string)field.GetValue(null)!;
            if (!knownActions.Contains(value))
                missing.Add($"{field.Name} = \"{value}\"");
        }

        Assert.True(missing.Count == 0,
            $"KnownActionTypes.All is missing TraceConstants entries: {string.Join(", ", missing)}");
    }

    // =========================================================================
    // T3 -- Predicate discovery matches FunctionRegistry
    // =========================================================================

    [Fact]
    public void T3_PredicateDiscovery_MatchesFunctionRegistry()
    {
        /*
         * CONTRACT: Given FunctionRegistry.All has N entries,
         *           When CoverageAnalyzer discovers predicates,
         *           Then the discovered set has exactly N entries and includes
         *                "isQuestComplete", "playerZone", "questVariable".
         *
         * BUILDER GUIDANCE: Use FunctionRegistry.All.Keys to discover predicates.
         */

        // Arrange
        int expectedCount = FunctionRegistry.All.Count;
        Assert.True(expectedCount >= 40,
            $"Expected at least 40 predicates in FunctionRegistry, found {expectedCount}");

        // Act -- analyze with no fixtures; Predicates.Total reveals the discovered count
        var report = Analyze([], []);

        // Assert
        Assert.Equal(expectedCount, report.Predicates.Total);
        Assert.Contains("isQuestComplete", report.Predicates.UncoveredItems);
        Assert.Contains("playerZone", report.Predicates.UncoveredItems);
        Assert.Contains("questVariable", report.Predicates.UncoveredItems);
    }

    // =========================================================================
    // T4 -- Empty fixtures yield 0% coverage
    // =========================================================================

    [Fact]
    public void T4_EmptyFixtures_ZeroCoverage()
    {
        /*
         * CONTRACT: Given no fixture files exist (empty capabilities, empty action types),
         *           When Analyze([], []) is called,
         *           Then Steps.Covered == 0, Predicates.Covered == 0,
         *                ActionTypes.Covered == 0; totals match discovered counts;
         *                OverallPercentage == 0.0; UncoveredItems lists all known items.
         *
         * BUILDER GUIDANCE: The unit-test overload takes pre-built lists so tests
         *                   avoid file I/O. Empty inputs mean nothing is covered.
         */

        // Act
        var report = Analyze([], []);

        // Assert -- all covered counts are zero
        Assert.Equal(0, report.Steps.Covered);
        Assert.Equal(0, report.Predicates.Covered);
        Assert.Equal(0, report.ActionTypes.Covered);

        // Totals should match the discovered counts
        int expectedStepCount = typeof(Step)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Count();
        Assert.Equal(expectedStepCount, report.Steps.Total);
        Assert.Equal(FunctionRegistry.All.Count, report.Predicates.Total);
        Assert.True(report.ActionTypes.Total >= 20,
            $"Expected ActionTypes.Total >= 20, got {report.ActionTypes.Total}");

        // Overall percentage should be 0
        Assert.Equal(0.0, report.OverallPercentage);

        // UncoveredItems should contain all items
        Assert.Equal(report.Steps.Total, report.Steps.UncoveredItems.Count);
        Assert.Equal(report.Predicates.Total, report.Predicates.UncoveredItems.Count);
        Assert.Equal(report.ActionTypes.Total, report.ActionTypes.UncoveredItems.Count);

        // CoveredItems should be empty
        Assert.Empty(report.Steps.CoveredItems);
        Assert.Empty(report.Predicates.CoveredItems);
        Assert.Empty(report.ActionTypes.CoveredItems);
    }

    // =========================================================================
    // T5 -- Single fixture with step and predicate capabilities
    // =========================================================================

    [Fact]
    public void T5_SingleFixture_StepAndPredicateCoverage()
    {
        /*
         * CONTRACT: Given capabilities ["step:travel","step:talk","predicate:playerZone",
         *                               "predicate:isQuestComplete"]
         *                and action types ["navigate","interact"],
         *           When Analyze is called,
         *           Then Steps.Covered == 2, Predicates.Covered == 2,
         *                ActionTypes.Covered == 2; UncoveredItems excludes the covered ones;
         *                OverallPercentage reflects 6 / totalItems.
         *
         * BUILDER GUIDANCE: Parse "step:" prefix to extract step discriminator,
         *                   "predicate:" prefix for predicate names.
         */

        // Arrange
        var capabilities = new List<string>
        {
            "step:travel", "step:talk",
            "predicate:playerZone", "predicate:isQuestComplete"
        };
        var actionTypes = new List<string> { "navigate", "interact" };

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert -- covered counts
        Assert.Equal(2, report.Steps.Covered);
        Assert.Equal(2, report.Predicates.Covered);
        Assert.Equal(2, report.ActionTypes.Covered);

        // Covered items
        Assert.Contains("travel", report.Steps.CoveredItems);
        Assert.Contains("talk", report.Steps.CoveredItems);
        Assert.Contains("playerZone", report.Predicates.CoveredItems);
        Assert.Contains("isQuestComplete", report.Predicates.CoveredItems);
        Assert.Contains("navigate", report.ActionTypes.CoveredItems);
        Assert.Contains("interact", report.ActionTypes.CoveredItems);

        // Uncovered items should NOT contain the covered ones
        Assert.DoesNotContain("travel", report.Steps.UncoveredItems);
        Assert.DoesNotContain("talk", report.Steps.UncoveredItems);

        // Uncovered count
        Assert.Equal(report.Steps.Total - 2, report.Steps.UncoveredItems.Count);
        Assert.Equal(report.Predicates.Total - 2, report.Predicates.UncoveredItems.Count);
        Assert.Equal(report.ActionTypes.Total - 2, report.ActionTypes.UncoveredItems.Count);

        // Overall percentage: 6 covered out of total
        int totalItems = report.Steps.Total + report.Predicates.Total + report.ActionTypes.Total;
        double expected = Math.Round(100.0 * 6 / totalItems, 1);
        Assert.Equal(expected, report.OverallPercentage);
    }

    // =========================================================================
    // T6 -- Duplicate capabilities across fixtures are deduplicated
    // =========================================================================

    [Fact]
    public void T6_DuplicateCapabilities_Deduplicated()
    {
        /*
         * CONTRACT: Given two fixtures both have ["step:travel","predicate:playerZone"]
         *                in capabilities and ["navigate"] in action types,
         *           When analyzed,
         *           Then Steps.Covered == 1, Predicates.Covered == 1,
         *                ActionTypes.Covered == 1 -- not doubled.
         *
         * BUILDER GUIDANCE: Use HashSet-based union for deduplication.
         */

        // Arrange -- simulate two fixtures by combining their capabilities
        var capabilities = new List<string>
        {
            "step:travel", "predicate:playerZone",
            "step:travel", "predicate:playerZone"  // duplicates from second fixture
        };
        var actionTypes = new List<string> { "navigate", "navigate" };

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert
        Assert.Equal(1, report.Steps.Covered);
        Assert.Equal(1, report.Predicates.Covered);
        Assert.Equal(1, report.ActionTypes.Covered);
    }

    // =========================================================================
    // T7 -- Unknown capability prefixes are ignored
    // =========================================================================

    [Fact]
    public void T7_UnknownCapabilityPrefixes_Ignored()
    {
        /*
         * CONTRACT: Given capabilities include ["step:travel","engine:branching","bogus:xyz"]
         *                and action types ["navigate"],
         *           When analyzed,
         *           Then Steps.Covered == 1 (only "step:" prefix counts for steps);
         *                "engine:" and "bogus:" prefixes are silently ignored;
         *                no exception is thrown.
         *
         * BUILDER GUIDANCE: Only parse "step:" and "predicate:" prefixes from capabilities.
         *                   All other prefixes are silently dropped.
         */

        // Arrange
        var capabilities = new List<string>
        {
            "step:travel", "engine:branching", "bogus:xyz"
        };
        var actionTypes = new List<string> { "navigate" };

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert
        Assert.Equal(1, report.Steps.Covered);
        Assert.Equal(0, report.Predicates.Covered);
        Assert.Equal(1, report.ActionTypes.Covered);
    }

    // =========================================================================
    // T8 -- Unknown action types do not inflate coverage
    // =========================================================================

    [Fact]
    public void T8_UnknownActionTypes_Ignored()
    {
        /*
         * CONTRACT: Given action types include ["navigate","unknownaction"],
         *           When analyzed,
         *           Then ActionTypes.Covered == 1 (only "navigate" is in KnownActionTypes);
         *                "unknownaction" is silently ignored.
         *
         * BUILDER GUIDANCE: Intersect fixture action types with KnownActionTypes.All;
         *                   unknown action types are dropped silently.
         */

        // Arrange
        var capabilities = new List<string>();
        var actionTypes = new List<string> { "navigate", "unknownaction" };

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert
        Assert.Equal(1, report.ActionTypes.Covered);
        Assert.DoesNotContain("unknownaction", report.ActionTypes.CoveredItems);
        Assert.DoesNotContain("unknownaction", report.ActionTypes.UncoveredItems);
    }

    // =========================================================================
    // T9 -- 100% coverage when all items are covered
    // =========================================================================

    [Fact]
    public void T9_AllItemsCovered_100Percent()
    {
        /*
         * CONTRACT: Given capabilities include every "step:<x>" for all step types
         *                and every "predicate:<x>" for all predicates;
         *                action types include every entry from KnownActionTypes.All,
         *           When analyzed,
         *           Then OverallPercentage == 100.0; all UncoveredItems lists are empty.
         *
         * BUILDER GUIDANCE: This is the "green field" test -- everything is covered.
         */

        // Arrange -- build complete capability and action type lists from reflection
        var stepDiscriminators = typeof(Step)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (string)a.TypeDiscriminator!)
            .ToList();

        var predicateNames = FunctionRegistry.All.Keys.ToList();

        var capabilities = stepDiscriminators.Select(s => $"step:{s}")
            .Concat(predicateNames.Select(p => $"predicate:{p}"))
            .ToList();

        var actionTypes = KnownActionTypes.All.ToList();

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert
        Assert.Equal(100.0, report.OverallPercentage);
        Assert.Empty(report.Steps.UncoveredItems);
        Assert.Empty(report.Predicates.UncoveredItems);
        Assert.Empty(report.ActionTypes.UncoveredItems);

        // All section percentages are 100%
        Assert.Equal(100.0, report.Steps.Percentage);
        Assert.Equal(100.0, report.Predicates.Percentage);
        Assert.Equal(100.0, report.ActionTypes.Percentage);
    }

    // =========================================================================
    // T10 -- CoveredItems and UncoveredItems are sorted ordinally
    // =========================================================================

    [Fact]
    public void T10_ItemLists_SortedOrdinally()
    {
        /*
         * CONTRACT: Given capabilities ["step:travel","step:accept","step:talk"] (unsorted),
         *           When analyzed,
         *           Then Steps.CoveredItems == ["accept","talk","travel"] (sorted, prefix stripped);
         *                Steps.UncoveredItems is also sorted ordinally.
         *
         * BUILDER GUIDANCE: Sort both CoveredItems and UncoveredItems using
         *                   StringComparer.Ordinal before returning.
         */

        // Arrange
        var capabilities = new List<string> { "step:travel", "step:accept", "step:talk" };
        var actionTypes = new List<string>();

        // Act
        var report = Analyze(capabilities, actionTypes);

        // Assert -- covered items are sorted
        var coveredList = report.Steps.CoveredItems.ToList();
        Assert.Equal(3, coveredList.Count);
        Assert.Equal("accept", coveredList[0]);
        Assert.Equal("talk", coveredList[1]);
        Assert.Equal("travel", coveredList[2]);

        // Uncovered items are also sorted
        var uncoveredList = report.Steps.UncoveredItems.ToList();
        var sortedUncovered = uncoveredList.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sortedUncovered, uncoveredList);
    }

    // =========================================================================
    // T11 -- Text format lists uncovered items
    // =========================================================================

    [Fact]
    public void T11_TextFormat_ListsUncoveredItems()
    {
        /*
         * CONTRACT: Given a CoverageReport with Steps.Covered=2, Total=5,
         *                UncoveredItems=["branch","combat","duty"],
         *           When FormatCoverageText is called,
         *           Then output contains "Steps: 2/5 (40.0%)",
         *                followed by "  Uncovered:" and each uncovered item on its
         *                own line prefixed with "    - ".
         *
         * BUILDER GUIDANCE: Add FormatCoverageText to OutputFormatters.
         *                   Format: "Section: covered/total (pct%)\n  Uncovered:\n    - item"
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(
                Covered: 2, Total: 5, Percentage: 40.0,
                CoveredItems: ["accept", "talk"],
                UncoveredItems: ["branch", "combat", "duty"]),
            Predicates: new CoverageSection(
                Covered: 1, Total: 3, Percentage: 33.3,
                CoveredItems: ["playerZone"],
                UncoveredItems: ["isQuestComplete", "questVariable"]),
            ActionTypes: new CoverageSection(
                Covered: 0, Total: 2, Percentage: 0.0,
                CoveredItems: [],
                UncoveredItems: ["navigate", "interact"]));

        // Act
        string output = OutputFormatters.FormatCoverageText(report);

        // Assert
        Assert.Contains("Steps: 2/5 (40.0%)", output);
        Assert.Contains("Uncovered:", output);
        Assert.Contains("    - branch", output);
        Assert.Contains("    - combat", output);
        Assert.Contains("    - duty", output);
        Assert.Contains("Predicates: 1/3 (33.3%)", output);
        Assert.Contains("Action Types: 0/2 (0.0%)", output);
    }

    // =========================================================================
    // T12 -- JSON format round-trips
    // =========================================================================

    [Fact]
    public void T12_JsonFormat_RoundTrips()
    {
        /*
         * CONTRACT: Given a CoverageReport with known values,
         *           When FormatCoverageJson is called and the result is parsed as JsonDocument,
         *           Then $.steps.covered == 2, $.steps.total == 5, $.steps.percentage == 40.0,
         *                $.steps.uncovered is a JSON array of 3 strings;
         *                $.overall.covered, $.overall.total, $.overall.percentage are present.
         *
         * BUILDER GUIDANCE: Add FormatCoverageJson to OutputFormatters.
         *                   Use camelCase property naming. Include an "overall" object
         *                   with aggregated covered/total/percentage.
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(
                Covered: 2, Total: 5, Percentage: 40.0,
                CoveredItems: ["accept", "talk"],
                UncoveredItems: ["branch", "combat", "duty"]),
            Predicates: new CoverageSection(
                Covered: 1, Total: 3, Percentage: 33.3,
                CoveredItems: ["playerZone"],
                UncoveredItems: ["isQuestComplete", "questVariable"]),
            ActionTypes: new CoverageSection(
                Covered: 0, Total: 2, Percentage: 0.0,
                CoveredItems: [],
                UncoveredItems: ["navigate", "interact"]));

        // Act
        string json = OutputFormatters.FormatCoverageJson(report);

        // Assert -- must be valid JSON
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Steps section
        Assert.Equal(2, root.GetProperty("steps").GetProperty("covered").GetInt32());
        Assert.Equal(5, root.GetProperty("steps").GetProperty("total").GetInt32());
        Assert.Equal(40.0, root.GetProperty("steps").GetProperty("percentage").GetDouble());
        Assert.Equal(3, root.GetProperty("steps").GetProperty("uncovered").GetArrayLength());

        // Predicates section
        Assert.Equal(1, root.GetProperty("predicates").GetProperty("covered").GetInt32());

        // Action types section
        Assert.Equal(0, root.GetProperty("actionTypes").GetProperty("covered").GetInt32());

        // Overall section
        var overall = root.GetProperty("overall");
        Assert.True(overall.TryGetProperty("covered", out _), "overall.covered missing");
        Assert.True(overall.TryGetProperty("total", out _), "overall.total missing");
        Assert.True(overall.TryGetProperty("percentage", out _), "overall.percentage missing");
        Assert.Equal(3, overall.GetProperty("covered").GetInt32());  // 2+1+0
        Assert.Equal(10, overall.GetProperty("total").GetInt32());   // 5+3+2
    }

    // =========================================================================
    // T13 -- Markdown format produces valid GFM table
    // =========================================================================

    [Fact]
    public void T13_MarkdownFormat_ProducesGfmTable()
    {
        /*
         * CONTRACT: Given a CoverageReport with Steps=(2,5), Predicates=(1,3),
         *                ActionTypes=(0,4),
         *           When FormatCoverageMarkdown is called,
         *           Then output contains "| Steps", "| Predicates", "| Action Types",
         *                "| **Overall**" table rows; contains "### Uncovered Steps" section;
         *                contains "### Uncovered Predicates" section;
         *                contains "### Uncovered Action Types" section (0/4 = all uncovered).
         *
         * BUILDER GUIDANCE: Add FormatCoverageMarkdown to OutputFormatters.
         *                   Use GFM table format per CR8 in the plan.
         *                   Uncovered sections are omitted only when uncovered count is 0.
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(
                Covered: 2, Total: 5, Percentage: 40.0,
                CoveredItems: ["accept", "talk"],
                UncoveredItems: ["branch", "combat", "duty"]),
            Predicates: new CoverageSection(
                Covered: 1, Total: 3, Percentage: 33.3,
                CoveredItems: ["playerZone"],
                UncoveredItems: ["isQuestComplete", "questVariable"]),
            ActionTypes: new CoverageSection(
                Covered: 0, Total: 4, Percentage: 0.0,
                CoveredItems: [],
                UncoveredItems: ["engage", "interact", "navigate", "teleport"]));

        // Act
        string markdown = OutputFormatters.FormatCoverageMarkdown(report);

        // Assert -- GFM table markers
        Assert.Contains("| Steps", markdown);
        Assert.Contains("| Predicates", markdown);
        Assert.Contains("| Action Types", markdown);
        Assert.Contains("| **Overall**", markdown);

        // Table header separator
        Assert.Contains("|---", markdown);

        // Uncovered sections -- all three have uncovered items, so all appear
        Assert.Contains("### Uncovered Steps", markdown);
        Assert.Contains("### Uncovered Predicates", markdown);
        Assert.Contains("### Uncovered Action Types", markdown);

        // Uncovered items as markdown list items
        Assert.Contains("- `branch`", markdown);
        Assert.Contains("- `isQuestComplete`", markdown);
        Assert.Contains("- `navigate`", markdown);
    }

    [Fact]
    public void T13b_MarkdownFormat_OmitsUncoveredSection_WhenFullyCovered()
    {
        /*
         * CONTRACT: Given a CoverageReport where Steps is 100% covered
         *                (UncoveredItems is empty),
         *           When FormatCoverageMarkdown is called,
         *           Then output does NOT contain "### Uncovered Steps".
         *
         * BUILDER GUIDANCE: Only emit "### Uncovered <Section>" when
         *                   that section's UncoveredItems is non-empty.
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(
                Covered: 2, Total: 2, Percentage: 100.0,
                CoveredItems: ["talk", "travel"],
                UncoveredItems: []),
            Predicates: new CoverageSection(
                Covered: 0, Total: 1, Percentage: 0.0,
                CoveredItems: [],
                UncoveredItems: ["playerZone"]),
            ActionTypes: new CoverageSection(
                Covered: 0, Total: 1, Percentage: 0.0,
                CoveredItems: [],
                UncoveredItems: ["navigate"]));

        // Act
        string markdown = OutputFormatters.FormatCoverageMarkdown(report);

        // Assert
        Assert.DoesNotContain("### Uncovered Steps", markdown);
        Assert.Contains("### Uncovered Predicates", markdown);
        Assert.Contains("### Uncovered Action Types", markdown);
    }

    // =========================================================================
    // CoverageReport.OverallPercentage computed property
    // =========================================================================

    [Fact]
    public void OverallPercentage_ComputedCorrectly()
    {
        /*
         * CONTRACT: Given a CoverageReport with Steps=(2,5), Predicates=(1,3),
         *                ActionTypes=(0,2),
         *           When OverallPercentage is accessed,
         *           Then it returns Math.Round(100.0 * 3 / 10, 1) == 30.0.
         *
         * BUILDER GUIDANCE: OverallPercentage is a computed property on CoverageReport,
         *                   not a constructor parameter.
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(2, 5, 40.0, ["a", "b"], ["c", "d", "e"]),
            Predicates: new CoverageSection(1, 3, 33.3, ["x"], ["y", "z"]),
            ActionTypes: new CoverageSection(0, 2, 0.0, [], ["m", "n"]));

        // Act
        double overall = report.OverallPercentage;

        // Assert
        Assert.Equal(30.0, overall);
    }

    [Fact]
    public void OverallPercentage_ZeroTotal_ReturnsHundred()
    {
        /*
         * CONTRACT: Given a CoverageReport where all totals are 0,
         *           When OverallPercentage is accessed,
         *           Then it returns 100.0 (vacuous truth: nothing to cover).
         *
         * BUILDER GUIDANCE: Guard against division by zero.
         *                   Per CR5 in the plan, 0 total => 100.0%.
         *                   (Note: the task description says 0.0 but the plan's CR5
         *                    implementation says 100.0. Follow CR5.)
         */

        // Arrange
        var report = new CoverageReport(
            Steps: new CoverageSection(0, 0, 0.0, [], []),
            Predicates: new CoverageSection(0, 0, 0.0, [], []),
            ActionTypes: new CoverageSection(0, 0, 0.0, [], []));

        // Act & Assert
        Assert.Equal(100.0, report.OverallPercentage);
    }
}
