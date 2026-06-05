# Coverage Report Plan: `qf-trace coverage --quest-data <dir>`

**Status:** ready to implement
**Input docs:** issue #55 in questforge-tools
**Output:** `qf-trace coverage --quest-data <dir>` prints a fixture transition coverage report showing what percentage of step types, predicates, and action types are covered by at least one fixture. `--min <percent>` exits non-zero when coverage is below threshold (CI gate).
**Phase dependencies:** None; uses existing `CliArgsParser`, `CliArgs`, `OutputFormatters`, `FixtureModel` infrastructure. Requires `QuestForge.Schema` (for `[JsonDerivedType]` reflection) and `QuestForge.Predicates` (for `FunctionRegistry.All`).

---

## Dependency graph

Single repo (`questforge-tools`), two new project references:

```
QuestForge.Tools.Trace/Coverage/CoverageAnalyzer.cs         <-- new
QuestForge.Tools.Trace/Coverage/CoverageReport.cs            <-- new
QuestForge.Tools.Trace/Coverage/KnownActionTypes.cs          <-- new (static list)
QuestForge.Tools.Trace/Cli/CliSubcommand.cs                  <-- add Coverage
QuestForge.Tools.Trace/Cli/CliArgsParser.cs                   <-- add "coverage" case + --min flag
QuestForge.Tools.Trace/Cli/CliArgs.cs                         <-- add MinCoverage? field
QuestForge.Tools.Trace/Cli/OutputFormatters.cs                <-- add FormatCoverageText/Json/Markdown
QuestForge.Tools.Trace/QuestForge.Tools.Trace.csproj          <-- add ProjectReference to Predicates
qf-trace/Program.cs                                          <-- add RunCoverage + dispatch + help
QuestForge.Tools.Trace.Tests/Coverage/CoverageAnalyzerTests.cs <-- new
QuestForge.Tools.Trace.Tests/Cli/CoverageCli Tests.cs         <-- new (CLI parsing + formatter tests)
```

Build order: result types first -> analyzer -> CLI wiring -> tests.

---

## Architectural decisions

### CR1 -- Step type totals derived from `[JsonDerivedType]` reflection, not a hardcoded list

The analyzer reflects over `typeof(Step).GetCustomAttributes<JsonDerivedTypeAttribute>()` to extract discriminator strings. This means adding a new step type to Schema automatically updates the coverage total -- no manual sync required.

`fragment` is NOT filtered out. It is a real step type with a `step:fragment` capability tag, and fixtures that exercise fragment composition should exist. The issue description mentioned filtering it, but `CapabilityInferrer` already emits `step:fragment` for `FragmentStep` instances, so a fixture with a fragment-bearing quest would cover it.

```csharp
private static IReadOnlySet<string> DiscoverStepTypes()
{
    var attrs = typeof(Step).GetCustomAttributes<JsonDerivedTypeAttribute>();
    return attrs.Select(a => (string)a.TypeDiscriminator!).ToHashSet(StringComparer.Ordinal);
}
```

**Rejected alternative:** Hardcoded list of step discriminator strings. Breaks silently when a new step type is added to Schema and nobody remembers to update the coverage tool.

**What breaks if violated:** A new step type would appear in fixtures' capabilities but not in the total set, causing >100% coverage or silent omission.

**Testability:** Unit tests can assert `DiscoverStepTypes()` count >= 27 and contains known entries.

### CR2 -- Predicate totals derived from `FunctionRegistry.All.Keys` at runtime

Requires adding a `ProjectReference` from `QuestForge.Tools.Trace` to `QuestForge.Predicates`. This is a one-line csproj change. The Predicates project has zero dependencies (no Dalamud, no Engine), so this is safe.

```csharp
private static IReadOnlySet<string> DiscoverPredicates()
    => FunctionRegistry.All.Keys.ToHashSet(StringComparer.Ordinal);
```

**Rejected alternative:** Parsing predicate names from a manifest file or hardcoded list. Same staleness problem as CR1.

**What breaks if violated:** Adding a new predicate function to `FunctionRegistry` without updating a separate list would silently report higher coverage than actual.

### CR3 -- Action type totals are a static list in `KnownActionTypes`, not reflection

The tools repo references `QuestForge.Engine` but `EngineAction` subclasses are sealed internal types not designed for external reflection. A static `HashSet<string>` in `KnownActionTypes` mirrors the `TraceConstants` catalog. This is acceptable because action types change rarely (requires engine changes), and `TraceConstants` already maintains the same list for other purposes.

```csharp
namespace QuestForge.Tools.Trace.Coverage;

internal static class KnownActionTypes
{
    internal static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        "navigate", "interact", "interactobject", "handover", "purchase",
        "useaethernet", "teleport", "wait", "awaituser", "done",
        "engage", "equipgear", "equipbestgear", "changejob",
        "registergearset", "opencoffer", "useaction", "useemote",
        "useitem", "saychatmessage",
        "entersingleplayerduty", "enterduty",
    };
}
```

The list includes `entersingleplayerduty` and `enterduty` which are in `TraceConstants` but were not in the issue description. The canonical source is `TraceConstants`; the coverage list must match it.

**Rejected alternative:** Reflecting over `EngineAction` subtypes. The engine's `EngineAction` is a discriminated union of nested record types -- reflection would require `typeof(EngineAction).GetNestedTypes()` and lowercasing `.Name`, which couples to internal naming conventions. The static list is explicit and easy to audit.

**What breaks if violated:** Adding a new `EngineAction` variant without updating `KnownActionTypes` causes the new action to be missing from coverage totals. A unit test (T2) guards against this by asserting `KnownActionTypes.All` is a superset of `TraceConstants` action constants.

### CR4 -- Action types extracted from `expectedTransitions[].actionType`, not from capabilities

Fixture capabilities use `step:` and `predicate:` prefixes but do NOT include action types. Action type coverage is derived by scanning `expectedTransitions[].actionType` across all fixtures and collecting unique values.

```csharp
var coveredActions = fixtures
    .SelectMany(f => f.ExpectedTransitions)
    .Select(t => t.ActionType)
    .ToHashSet(StringComparer.Ordinal);
```

**What breaks if violated:** Action type coverage would always be 0% if we only looked at capabilities.

### CR5 -- `CoverageReport` and `CoverageSection` are records, output formatting is in `OutputFormatters`

The analyzer returns pure data; formatting is the CLI layer's job. Three formatters: `FormatCoverageText`, `FormatCoverageJson`, `FormatCoverageMarkdown`.

```csharp
namespace QuestForge.Tools.Trace.Coverage;

public sealed record CoverageSection(
    int Covered,
    int Total,
    double Percentage,
    IReadOnlyList<string> CoveredItems,
    IReadOnlyList<string> UncoveredItems);

public sealed record CoverageReport(
    CoverageSection Steps,
    CoverageSection Predicates,
    CoverageSection ActionTypes)
{
    public double OverallPercentage
    {
        get
        {
            int totalItems = Steps.Total + Predicates.Total + ActionTypes.Total;
            if (totalItems == 0) return 100.0;
            int coveredItems = Steps.Covered + Predicates.Covered + ActionTypes.Covered;
            return Math.Round(100.0 * coveredItems / totalItems, 1);
        }
    }
}
```

`OverallPercentage` is a computed property, not a constructor parameter. This avoids the caller needing to compute it and avoids inconsistency between section data and the overall number.

**Rejected alternative:** Making `OverallPercentage` a constructor parameter. Creates a contract where the caller could pass an inconsistent value.

### CR6 -- `--min` is an optional integer flag parsed as `MinCoverage` on `CliArgs`

```csharp
// CliArgs addition:
public sealed record CliArgs(
    // ... existing fields ...
    int? MinCoverage = null);  // null = no threshold, exit 0

// CliArgsParser: "--min" added to ValueFlags, parsed as int
```

When `MinCoverage` is set and `report.OverallPercentage < MinCoverage`, exit code is 1 and a message is written to stderr. When coverage meets or exceeds the threshold, exit code is 0.

**What breaks if violated:** CI cannot gate on coverage without an exit-code mechanism.

### CR7 -- `CoverageAnalyzer.Analyze` takes the quest-data root string, not pre-parsed fixtures

The analyzer owns fixture scanning (reuses the same `FixtureModel` deserialization as `ListFixturesCommand`). This keeps the CLI handler thin: just construct the analyzer, call `Analyze`, format, print.

```csharp
public sealed class CoverageAnalyzer
{
    private static readonly JsonSerializerOptions ReadOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public CoverageReport Analyze(string questDataRoot);
}
```

For testability, the analyzer also accepts an overload taking pre-built data so tests can avoid file I/O:

```csharp
public CoverageReport Analyze(
    IReadOnlyList<string> fixtureCapabilities,
    IReadOnlyList<string> fixtureActionTypes);
```

The first overload calls the second after scanning files.

**Rejected alternative:** Having the CLI handler scan fixtures and pass them in. Duplicates the scanning logic that `ListFixturesCommand` already has and that we'd be re-implementing anyway.

### CR8 -- Markdown format uses GitHub-flavored tables for `$GITHUB_STEP_SUMMARY`

```markdown
## Fixture Coverage Report

| Category     | Covered | Total | Percentage |
|-------------|---------|-------|------------|
| Steps        | 5       | 27    | 18.5%      |
| Predicates   | 4       | 40    | 10.0%      |
| Action Types | 3       | 22    | 13.6%      |
| **Overall**  | **12**  | **89**| **13.5%**  |

### Uncovered Steps
- `attune`
- `branch`
...

### Uncovered Predicates
- `currentJob`
...

### Uncovered Action Types
- `changejob`
...
```

Uncovered sections are only emitted when there are uncovered items. If a section is 100% covered, its "Uncovered" subsection is omitted.

### CR9 -- `coverage` subcommand is dispatched after quest-data resolution, alongside `list-fixtures`

In `Program.cs`, the `coverage` subcommand follows the same quest-data resolution path as `list-fixtures` and `validate-fixture`. It requires `resolvedRoot` to be non-null.

```csharp
// In the switch expression:
CliSubcommand.Coverage => RunCoverage(cliArgs, resolvedRoot),
```

---

## File-by-file change list

### New files

1. **`QuestForge.Tools.Trace/Coverage/CoverageReport.cs`** -- `CoverageSection` and `CoverageReport` record definitions (CR5).

2. **`QuestForge.Tools.Trace/Coverage/KnownActionTypes.cs`** -- Static `HashSet<string>` of all known action type strings (CR3).

3. **`QuestForge.Tools.Trace/Coverage/CoverageAnalyzer.cs`** -- `Analyze(string questDataRoot)` and `Analyze(IReadOnlyList<string>, IReadOnlyList<string>)` overloads. Step discovery via `[JsonDerivedType]` reflection (CR1). Predicate discovery via `FunctionRegistry.All.Keys` (CR2). Fixture scanning with `FixtureModel` deserialization. Returns `CoverageReport`.

4. **`QuestForge.Tools.Trace.Tests/Coverage/CoverageAnalyzerTests.cs`** -- All test scenarios T1-T14.

### Modified files

5. **`QuestForge.Tools.Trace/QuestForge.Tools.Trace.csproj`** -- Add `<ProjectReference Include="..\QuestForge.Predicates\QuestForge.Predicates.csproj" />` (CR2).

6. **`QuestForge.Tools.Trace/Cli/CliSubcommand.cs`** -- Add `Coverage` enum value.

7. **`QuestForge.Tools.Trace/Cli/CliArgs.cs`** -- Add `int? MinCoverage = null` parameter to the record (CR6). Because `CliArgs` is a positional record, this is appended after `WithTrace` with a default value.

8. **`QuestForge.Tools.Trace/Cli/CliArgsParser.cs`** -- Add `"coverage"` to the subcommand switch. Add `"--min"` to `ValueFlags`. Parse `--min` as `int.TryParse`; set `parseError` on failure. Pass `MinCoverage` to the `CliArgs` constructor.

9. **`QuestForge.Tools.Trace/Cli/OutputFormatters.cs`** -- Add three static methods: `FormatCoverageText(CoverageReport)`, `FormatCoverageJson(CoverageReport)`, `FormatCoverageMarkdown(CoverageReport)` (CR5, CR8).

10. **`qf-trace/Program.cs`** -- Add `CliSubcommand.Coverage` to the switch expression (dispatching to `RunCoverage`). Add `RunCoverage(CliArgs, string?)` method. Add coverage help text to `PrintHelp()`.

---

## Given-When-Then test scenarios

### CoverageAnalyzer tests (`CoverageAnalyzerTests.cs`)

**T1 -- Step type discovery matches Schema**

Given: the `Step` class has 27 `[JsonDerivedType]` attributes
When: `CoverageAnalyzer` discovers step types via reflection
Then: the discovered set contains exactly 27 entries, including `"travel"`, `"talk"`, `"open-coffers"`, `"hand-over-item"`

**T2 -- KnownActionTypes is a superset of TraceConstants**

Given: `TraceConstants` defines action constants (`ActionNavigate` through `ActionEnterDuty`)
When: each `TraceConstants.Action*` constant value is checked against `KnownActionTypes.All`
Then: every constant value is present in `KnownActionTypes.All` (guards against adding a TraceConstant without updating KnownActionTypes)

**T3 -- Predicate discovery matches FunctionRegistry**

Given: `FunctionRegistry.All` has N entries
When: `CoverageAnalyzer` discovers predicates
Then: the discovered set has exactly N entries and includes `"isQuestComplete"`, `"playerZone"`, `"questVariable"`

**T4 -- Empty fixture directory yields 0% coverage**

Given: no fixture files exist (empty capabilities list, empty action types list)
When: `Analyze([], [])` is called
Then: `Steps.Covered == 0`, `Predicates.Covered == 0`, `ActionTypes.Covered == 0`; `Steps.Total == 27`, `Predicates.Total == FunctionRegistry.All.Count`, `ActionTypes.Total == KnownActionTypes.All.Count`; `OverallPercentage == 0.0`; `UncoveredItems` lists all known items in each section

**T5 -- Single fixture with step and predicate capabilities**

Given: capabilities `["step:travel", "step:talk", "predicate:playerZone", "predicate:isQuestComplete"]` and action types `["navigate", "interact"]`
When: `Analyze(capabilities, actionTypes)` is called
Then: `Steps.Covered == 2`, `Steps.UncoveredItems` contains 25 entries (all step types except `travel` and `talk`); `Predicates.Covered == 2`; `ActionTypes.Covered == 2`; `OverallPercentage` is `Round(100.0 * 6 / (27 + predicateCount + actionCount), 1)`

**T6 -- Duplicate capabilities across multiple fixtures are deduplicated**

Given: two fixtures both have `["step:travel", "predicate:playerZone"]` in capabilities and `["navigate"]` in action types
When: analyzed
Then: `Steps.Covered == 1` (travel), `Predicates.Covered == 1` (playerZone), `ActionTypes.Covered == 1` (navigate) -- not doubled

**T7 -- Unknown capability prefixes are ignored**

Given: capabilities include `["step:travel", "engine:branching", "bogus:xyz"]` and action types `["navigate"]`
When: analyzed
Then: `Steps.Covered == 1` (only `step:` prefix counts for steps); `engine:` and `bogus:` prefixes are ignored; no exception thrown

**T8 -- Unknown action types in transitions do not inflate coverage**

Given: action types include `["navigate", "unknownaction"]`
When: analyzed
Then: `ActionTypes.Covered == 1` (only `navigate` is in `KnownActionTypes`); `"unknownaction"` is silently ignored

**T9 -- 100% coverage when all items are covered**

Given: capabilities include every `step:<x>` for all 27 step types and every `predicate:<x>` for all predicates; action types include every entry from `KnownActionTypes.All`
When: analyzed
Then: `OverallPercentage == 100.0`; all `UncoveredItems` lists are empty

**T10 -- CoveredItems and UncoveredItems are sorted ordinally**

Given: capabilities `["step:travel", "step:accept", "step:talk"]` (unsorted)
When: analyzed
Then: `Steps.CoveredItems` is `["accept", "talk", "travel"]` (sorted, prefix stripped); `Steps.UncoveredItems` is also sorted

### Formatter tests

**T11 -- Text format lists uncovered items**

Given: a `CoverageReport` with `Steps.Covered=2, Total=5, UncoveredItems=["branch","combat","duty"]`
When: `FormatCoverageText` is called
Then: output contains `"Steps: 2/5 (40.0%)"`, followed by `"  Uncovered:"` and each uncovered item on its own line prefixed with `"    - "`

**T12 -- JSON format round-trips**

Given: a `CoverageReport` with known values
When: `FormatCoverageJson` is called and the result is parsed as `JsonDocument`
Then: `$.steps.covered == 2`, `$.steps.total == 5`, `$.steps.percentage == 40.0`, `$.steps.uncovered` is a JSON array of 3 strings; `$.overall.covered`, `$.overall.total`, `$.overall.percentage` are present

**T13 -- Markdown format produces valid GFM table**

Given: a `CoverageReport` with `Steps=(2,5)`, `Predicates=(1,3)`, `ActionTypes=(0,4)`
When: `FormatCoverageMarkdown` is called
Then: output contains `"| Steps"`, `"| Predicates"`, `"| Action Types"`, `"| **Overall**"` table rows; contains `"### Uncovered Steps"` section; does NOT contain `"### Uncovered Action Types"` section header if that section would be empty -- wait, 0/4 means all 4 are uncovered, so it DOES appear. Correction: the section is omitted only when uncovered count is 0.

### CLI integration tests

**T14 -- `--min` below threshold exits 1**

Given: `CliArgsParser.Parse(["coverage", "--quest-data", "/tmp", "--min", "50"])`
When: parsed
Then: `Subcommand == Coverage`, `QuestDataRoot == "/tmp"`, `MinCoverage == 50`

**T15 -- `--min` with non-integer value is a parse error**

Given: `CliArgsParser.Parse(["coverage", "--quest-data", "/tmp", "--min", "abc"])`
When: parsed
Then: `ParseError` is non-null and contains `"--min"`

**T16 -- `--min` without value is a parse error**

Given: `CliArgsParser.Parse(["coverage", "--quest-data", "/tmp", "--min"])`
When: parsed
Then: `ParseError` is non-null (flag requires a value)

**T17 -- `coverage` subcommand recognized**

Given: `CliArgsParser.Parse(["coverage", "--quest-data", "/tmp", "--format", "json"])`
When: parsed
Then: `Subcommand == Coverage`, `Format == "json"`, `QuestDataRoot == "/tmp"`

---

## Implementation order

### Phase A -- Result types and KnownActionTypes (30 min)

1. Create `QuestForge.Tools.Trace/Coverage/CoverageReport.cs` with `CoverageSection` and `CoverageReport` records.
2. Create `QuestForge.Tools.Trace/Coverage/KnownActionTypes.cs` with the static `HashSet<string>`.
3. Add `ProjectReference` to `QuestForge.Predicates` in `QuestForge.Tools.Trace.csproj`.

Done gate: project builds.

### Phase B -- CoverageAnalyzer (1 hour)

4. Create `QuestForge.Tools.Trace/Coverage/CoverageAnalyzer.cs` with both overloads.
5. Write tests T1-T10 in `QuestForge.Tools.Trace.Tests/Coverage/CoverageAnalyzerTests.cs`.

Done gate: all analyzer tests pass.

### Phase C -- CLI wiring (1 hour)

6. Add `Coverage` to `CliSubcommand`.
7. Add `int? MinCoverage` to `CliArgs` (with default `null`).
8. Add `"coverage"` to `CliArgsParser` switch and `"--min"` to `ValueFlags` with `int.TryParse` handling.
9. Add `FormatCoverageText`, `FormatCoverageJson`, `FormatCoverageMarkdown` to `OutputFormatters`.
10. Add `RunCoverage` to `Program.cs` and wire into the subcommand switch. Add help text.
11. Write tests T11-T17.

Done gate: all tests pass, `dotnet run --project qf-trace -- coverage --quest-data <path> --format text` produces output.

### Phase D -- Integration smoke (15 min)

12. Run against real `questforge-data` fixtures and verify output is sensible.
13. Run with `--format json` and `--format markdown`.
14. Run with `--min 5` (should pass) and `--min 99` (should fail with exit code 1).

Done gate: all three formats produce correct output; exit codes are correct.

---

## Done criteria

1. `qf-trace coverage --quest-data <dir>` prints a three-section coverage report to stdout in text format.
2. `--format json` produces valid JSON with `steps`, `predicates`, `actionTypes`, and `overall` objects.
3. `--format markdown` produces a GitHub-flavored markdown table suitable for `$GITHUB_STEP_SUMMARY`.
4. `--min <N>` exits 1 when overall coverage is below N%, exits 0 when at or above.
5. Step type totals are derived from `[JsonDerivedType]` reflection on `Step` -- adding a new step type to Schema automatically increases the total.
6. Predicate totals are derived from `FunctionRegistry.All.Keys` -- adding a new predicate function automatically increases the total.
7. Action type totals are maintained in `KnownActionTypes` -- a test (T2) ensures it is a superset of `TraceConstants`.
8. All 17 test scenarios pass in `QuestForge.Tools.Trace.Tests`.

---

## Exclusions

- This plan does NOT add a CI workflow to `questforge-data`. That is a follow-up task (add a step to the existing fixture CI workflow).
- This plan does NOT add coverage for `engine:*` capability tags (e.g. `engine:branching`, `engine:fragments`). Those are engine features, not step/predicate/action categories.
- This plan does NOT gate any existing CI on coverage thresholds. The `--min` flag is opt-in for future CI integration.
- This plan does NOT add action type strings to fixture `capabilities` arrays. Action types remain extracted from `expectedTransitions`.

---

## Ready for test creation

Tester: Write failing tests from the GWT specs in T1-T17.
- Happy paths: 6 scenarios (T1, T3, T5, T9, T12, T17)
- Edge cases: 6 scenarios (T4, T6, T7, T8, T10, T13)
- Error cases: 5 scenarios (T2, T11, T14, T15, T16)
- Expected total: ~17 tests in QuestForge.Tools.Trace.Tests
