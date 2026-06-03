# Lumina Quest Metadata Resolution Plan

**Status:** spec complete, ready for test creation
**Scope:** `--sqpack` CLI flag for `qf-trace extract-quest`; resolves quest name, expansion, category, requirements from FFXIV game data via the Lumina NuGet package. Covers issues #4 (name resolution) and #15 (requirements inference).
**Input docs:** `QuestForge.Schema/QuestDefinition.cs`, `TraceToQuestExtractor.cs`, `CliArgsParser.cs`, `Program.cs`
**Output:** `extract-quest` populates `name`, `expansion`, `category`, `requirements` fields when `--sqpack` is provided or auto-detected. TODOs for those fields are removed. When no sqpack is available, behavior is unchanged (all TODOs remain).
**Dependencies:** None -- this is additive to the existing `extract-quest` subcommand.

---

## Dependency graph

```
1. QuestForge.Tools.Trace (library)
   +-- New: IQuestMetadataResolver interface
   +-- New: LuminaMetadataResolver (Lumina NuGet dependency)
   +-- New: NullMetadataResolver
   +-- Modified: TraceToQuestExtractor (accepts optional resolver)
   +-- Modified: CliArgs (new SqpackPath field)
   +-- Modified: CliArgsParser (new --sqpack flag)

2. qf-trace (CLI)
   +-- Modified: Program.cs (wire sqpack path, create resolver, pass to extractor)

3. QuestForge.Tools.Trace.Tests
   +-- New: LuminaResolutionTests.cs (unit tests against NullMetadataResolver + mock)
   +-- New: CliArgsTests additions (--sqpack flag parsing)
   +-- Modified: TraceToQuestExtractorTests.cs (verify resolver integration)
```

**Build order:** Interface + NullResolver first, then extractor integration, then LuminaMetadataResolver, then CLI wiring.

---

## Architectural decisions

### LR1 -- IQuestMetadataResolver is an interface, not a concrete class

The extractor must be testable without game data installed. The interface allows injection of a mock/null resolver in tests and the real Lumina resolver at the CLI level.

**Rejected:** Making the extractor call Lumina directly. This would make every extractor test require sqpack data on disk.

```csharp
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
    string Expansion,           // "arr", "heavensward", "stormblood", "shadowbringers", "endwalker", "dawntrail"
    string Category,            // "msq", "class", "job", "role", "blue-urgent", "blue", "side"
    int? MinLevel,
    string? RequiredJob,        // job abbreviation (e.g. "GLA", "PLD") or null if any job
    uint[] PrerequisiteQuestIds // raw quest IDs from PreviousQuest0..4, zeros filtered out
);
```

**Testability:** Tests construct a `FakeMetadataResolver` returning canned `QuestMetadata`. No file system, no Lumina.

**What breaks if violated:** Extractor unit tests would require ~40GB of FFXIV game data to run, making CI impossible.

### LR2 -- NullMetadataResolver returns null for everything

This is the null-object pattern. When no sqpack path is available, the extractor uses `NullMetadataResolver` and the output is identical to today's behavior.

```csharp
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
```

**Rejected:** Making the resolver parameter nullable on the extractor and sprinkling `if (resolver != null)` everywhere. The null-object is cleaner.

### LR3 -- LuminaMetadataResolver takes a sqpack directory path, not IDataManager

The tools repo does not reference Dalamud. We use the standalone `Lumina` NuGet directly, constructing `Lumina.GameData` from a sqpack path.

```csharp
namespace QuestForge.Tools.Trace.Quest;

public sealed class LuminaMetadataResolver : IQuestMetadataResolver, IDisposable
{
    private readonly Lumina.GameData _gameData;

    public LuminaMetadataResolver(string sqpackPath)
    {
        _gameData = new Lumina.GameData(sqpackPath, new Lumina.LuminaOptions
        {
            PanicOnSheetChecksumMismatch = false,
        });
    }

    public QuestMetadata? ResolveQuest(uint questId) { ... }
    public string? ResolveNpcName(uint npcId) { ... }
    public string? ResolveZoneName(uint zoneId) { ... }
    public string? ResolveJobAbbreviation(uint jobId) { ... }

    public void Dispose() { /* GameData is IDisposable */ }
}
```

**Rejected:** Referencing `Dalamud.Plugin.Services.IDataManager`. The tools repo is Dalamud-free.

**What breaks if violated:** The tools project would gain a Dalamud dependency, breaking the three-repo architecture.

### LR4 -- Expansion mapping uses the Quest sheet's ExVersion column

The Quest sheet has an `ExVersion` reference column. We resolve the row ID to our canonical expansion strings.

```csharp
private static readonly Dictionary<uint, string> ExVersionToExpansion = new()
{
    [0] = "arr",
    [1] = "heavensward",
    [2] = "stormblood",
    [3] = "shadowbringers",
    [4] = "endwalker",
    [5] = "dawntrail",
};
```

**Rejected:** Deriving expansion from quest ID ranges. This is fragile across patches and requires maintaining a mapping table that could drift.

### LR5 -- Category mapping uses JournalGenre -> JournalCategory chain

This mirrors the approach already used in `LuminaQuestDataProvider.GetDebugInfo` in the main plugin repo. The JournalCategory row ID maps to our category strings.

```csharp
private static string MapCategory(uint journalCategoryId) => journalCategoryId switch
{
    // MSQ categories
    1 => "msq",       // Main Scenario (A Realm Reborn)
    2 => "msq",       // Main Scenario (Heavensward) -- 7th Umbral Era etc.
    3 => "msq",       // Main Scenario (post-patches)
    4 => "msq",       // Main Scenario (Stormblood)
    5 => "msq",       // Main Scenario (Shadowbringers)
    6 => "msq",       // Main Scenario (Endwalker)
    7 => "msq",       // Main Scenario (Dawntrail)

    // Class/Job/Role
    9 => "class",     // Class quests
    10 => "job",      // Job quests
    11 => "role",     // Role quests

    // Feature unlock
    // JournalCategory for "feature unlock" quests = blue icon quests
    // We need to distinguish blue-urgent from blue. Blue-urgent are critical
    // unlocks (e.g. retainers, glamour). For now, map all feature unlocks
    // as "blue" and let the author override "blue-urgent" manually.
    // TODO: research which JournalCategory IDs correspond to critical unlocks

    _ => "side",      // everything else
};
```

**Important caveat:** The exact JournalCategory-to-our-category mapping needs empirical verification against real game data. The builder should log the first 20 quest lookups to stderr when `--sqpack` is used, to help validate the mapping during manual testing. The mapping above is a starting point; the builder should adjust based on what the real sheets contain.

**Rejected:** Using `Quest.Type` column -- Lumina's Quest sheet does not have a simple `Type` enum that maps cleanly to our categories. The JournalGenre chain is the established pattern in the codebase.

### LR6 -- RequiredJob resolution uses ClassJobCategory + ClassJob sheets

The Quest sheet has `ClassJobCategory0` which references a `ClassJobCategory` row. If the category allows only a single job, we resolve its abbreviation from the `ClassJob` sheet. If it allows multiple jobs, we leave `RequiredJob` null (the quest is not job-locked).

```csharp
public string? ResolveJobAbbreviation(uint classJobCategoryId)
{
    // ClassJobCategory has boolean columns for each job.
    // If exactly one is true, resolve its abbreviation from ClassJob sheet.
    // If multiple or zero are true, return null.
}
```

This is intentionally conservative. Multi-job quests (e.g. "any tank") are not representable in our schema's `RequiredJob` (which is a single abbreviation), so we leave it null and let the author fill it in.

**Rejected:** Returning the ClassJobCategory name string. Our schema uses job abbreviations ("GLA", "PLD"), not category names ("Gladiator", "Paladin").

### LR7 -- Extractor constructor gains an optional IQuestMetadataResolver parameter

```csharp
public sealed class TraceToQuestExtractor
{
    private readonly StepInferenceEngine _inference;
    private readonly IQuestMetadataResolver _resolver;

    public TraceToQuestExtractor(
        StepInferenceEngine? inference = null,
        IQuestMetadataResolver? resolver = null)
    {
        _inference = inference ?? new StepInferenceEngine();
        _resolver = resolver ?? NullMetadataResolver.Instance;
    }
}
```

**What breaks if violated:** All existing callers and tests that construct `TraceToQuestExtractor()` with zero or one argument would break. The optional parameter preserves backward compatibility.

### LR8 -- Resolved metadata replaces TODO strings and removes corresponding TODO items

When `_resolver.ResolveQuest(questId)` returns non-null, the extractor:
1. Sets `Name` to `metadata.Name` (instead of `"TODO"`)
2. Sets `Expansion` to `metadata.Expansion` (instead of `"TODO"`)
3. Sets `Category` to `metadata.Category` (instead of `"TODO"`)
4. Sets `Requirements.MinLevel` to `metadata.MinLevel`
5. Sets `Requirements.RequiredJob` to `metadata.RequiredJob` (if non-null)
6. Sets `Requirements.Prereqs` to `metadata.PrerequisiteQuestIds.Select(id => new PrerequisiteRef(id, "complete")).ToArray()`
7. Removes the following TODO items from the list: "name (Lumina lookup required)", "expansion", "category", "requirements (level, job, prereqs)"
8. Keeps "lastVerifiedPatch" TODO (cannot be resolved from game data)

When `ResolveQuest` returns null, all five TODOs remain and fields stay as `"TODO"` -- identical to current behavior.

### LR9 -- SqpackPath is a new field on CliArgs

```csharp
public sealed record CliArgs(
    CliSubcommand Subcommand,
    string? TracePath,
    string? FixturePath,
    string? QuestDataRoot,
    string? OutputPath,
    string? SqpackPath,        // NEW
    bool Stdout,
    bool FailOnWarning,
    string Format,
    string? UnknownToken,
    string? ParseError,
    bool WithTrace = true);
```

`--sqpack` is a value-consuming flag added to `ValueFlags`.

### LR10 -- Auto-detection of sqpack path when --sqpack is not provided

When `--sqpack` is omitted, the CLI probes standard FFXIV install locations:
1. `C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack`
2. `C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack`

If found, log to stderr: `qf-trace: using sqpack: <path>`. If not found, use `NullMetadataResolver` silently.

```csharp
internal static class SqpackPathResolver
{
    public static string? Resolve()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack",
            @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack",
        ];

        foreach (var path in candidates)
            if (Directory.Exists(path))
                return path;

        return null;
    }
}
```

**Rejected:** Always requiring `--sqpack`. Auto-detection is a quality-of-life feature for contributors who have FFXIV installed locally.

**Rejected:** Also probing Linux paths. The tools currently target Windows only (Dalamud is Windows-only). Linux paths can be added later if needed.

### LR11 -- Lumina NuGet dependency scoped to QuestForge.Tools.Trace only

Add `Lumina` (MIT) to `QuestForge.Tools.Trace.csproj`. The `Lumina` package brings in `Lumina.Excel` transitively.

```xml
<ItemGroup>
  <PackageReference Include="Lumina" Version="4.*" />
</ItemGroup>
```

Pin to major version 4 (current stable as of 2026). The `LuminaMetadataResolver` is the only consumer.

### LR12 -- LuminaMetadataResolver catches row-not-found exceptions per lookup

Lumina throws when accessing a row that does not exist. Each method wraps in try/catch and returns null on failure rather than crashing the entire extraction.

```csharp
public QuestMetadata? ResolveQuest(uint questId)
{
    try
    {
        var quest = _gameData.GetExcelSheet<Quest>()!.GetRow(questId);
        // ... resolve all fields ...
        return new QuestMetadata(name, expansion, category, minLevel, requiredJob, prereqs);
    }
    catch
    {
        return null;
    }
}
```

**Rationale:** Quest IDs from traces may reference custom/modded content, removed quests, or have been corrupted. Graceful fallback to NullResolver behavior is better than crashing.

### LR13 -- NPC name and zone name resolution are cosmetic and go into TODO comments

`ResolveNpcName` and `ResolveZoneName` are available on the interface but are NOT used to populate schema fields in this phase. They exist for future use (e.g. adding `// NPC: Minfilia` comments to the draft output, or populating a `notes` field). The current implementation in `TraceToQuestExtractor` only calls `ResolveQuest`.

**Rationale:** The schema does not have a field for NPC display names or zone display names. Adding them would be a schema change out of scope.

---

## Task breakdown

### Task 1 -- Interface and NullResolver

**File:** `QuestForge.Tools.Trace/Quest/IQuestMetadataResolver.cs` (new)

```csharp
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
```

**File:** `QuestForge.Tools.Trace/Quest/NullMetadataResolver.cs` (new)

```csharp
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
```

### Task 2 -- Extractor integration with resolver

**File:** `QuestForge.Tools.Trace/Quest/TraceToQuestExtractor.cs` (modified)

Changes:
1. Add `private readonly IQuestMetadataResolver _resolver;` field
2. Add `IQuestMetadataResolver? resolver = null` parameter to constructor
3. In both `Extract` code paths (observation-based at line 516-554 and StepRecorded-based at line 623-645), after assembling the TODOs list and before constructing `QuestDefinition`:
   - Call `_resolver.ResolveQuest(runStart.Data.QuestId)`
   - If non-null, populate fields from metadata and remove resolved TODOs
   - If null, keep existing behavior

Extract the shared resolution logic into a private method:

```csharp
private static (QuestDefinition def, List<string> todos) ApplyMetadata(
    QuestDefinition baseDef,
    List<string> todos,
    IQuestMetadataResolver resolver)
{
    var meta = resolver.ResolveQuest(baseDef.Id);
    if (meta is null)
        return (baseDef, todos);

    var resolved = baseDef with
    {
        Name = meta.Name,
        Expansion = meta.Expansion,
        Category = meta.Category,
        Requirements = new Requirements
        {
            MinLevel = meta.MinLevel,
            RequiredJob = meta.RequiredJob,
            Prereqs = meta.PrerequisiteQuestIds
                .Select(id => new PrerequisiteRef(id, "complete"))
                .ToArray(),
        },
    };

    var filtered = todos
        .Where(t => t is not "name (Lumina lookup required)"
                    and not "expansion"
                    and not "category"
                    and not "requirements (level, job, prereqs)")
        .ToList();

    return (resolved, filtered);
}
```

### Task 3 -- CLI changes

**File:** `QuestForge.Tools.Trace/Cli/CliArgs.cs` (modified)

Add `string? SqpackPath` parameter between `OutputPath` and `Stdout`.

**File:** `QuestForge.Tools.Trace/Cli/CliArgsParser.cs` (modified)

1. Add `"--sqpack"` to `ValueFlags`
2. Add `case "--sqpack": sqpackPath = value; break;` in the value-flag switch
3. Declare `string? sqpackPath = null;` alongside other locals
4. Pass `SqpackPath: sqpackPath` in the return constructor call
5. Update `Default` method to include `SqpackPath: null`

**File:** `QuestForge.Tools.Trace/Cli/SqpackPathResolver.cs` (new)

Auto-detection of standard FFXIV install paths (see LR10).

**File:** `qf-trace/Program.cs` (modified)

In `RunExtractQuest`:
```csharp
private static int RunExtractQuest(CliArgs cliArgs, string? resolvedRoot)
{
    // ... existing trace path validation ...

    // Resolve sqpack path
    IQuestMetadataResolver resolver;
    var sqpackPath = cliArgs.SqpackPath ?? SqpackPathResolver.Resolve();
    if (sqpackPath is not null && Directory.Exists(sqpackPath))
    {
        Console.Error.WriteLine($"qf-trace: using sqpack: {sqpackPath}");
        resolver = new LuminaMetadataResolver(sqpackPath);
    }
    else
    {
        resolver = NullMetadataResolver.Instance;
    }

    var events = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
    var result = new TraceToQuestExtractor(resolver: resolver).Extract(events);

    // ... rest unchanged ...

    // Dispose resolver if needed
    if (resolver is IDisposable d) d.Dispose();
}
```

Update `PrintHelp` to add:
```
  extract-quest <trace.jsonl> [--sqpack <path>] [--quest-data <dir>] [--out <path>]
    Convert a .jsonl trace into a QuestDefinition draft.
    --sqpack: path to FFXIV sqpack directory (auto-detected if omitted).
```

### Task 4 -- LuminaMetadataResolver

**File:** `QuestForge.Tools.Trace/Quest/LuminaMetadataResolver.cs` (new)

Full implementation. Key methods:

```csharp
public sealed class LuminaMetadataResolver : IQuestMetadataResolver, IDisposable
{
    private readonly Lumina.GameData _gameData;

    public LuminaMetadataResolver(string sqpackPath)
    {
        _gameData = new Lumina.GameData(sqpackPath, new Lumina.LuminaOptions
        {
            PanicOnSheetChecksumMismatch = false,
        });
    }

    public QuestMetadata? ResolveQuest(uint questId)
    {
        try
        {
            var questSheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.Quest>()!;
            var row = questSheet.GetRow(questId);

            var name = row.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return null;  // unnamed quest = likely invalid

            var expansion = ResolveExpansion(row.ExVersion.RowId);
            var category = ResolveCategory(row);
            var minLevel = row.ClassJobLevel[0];
            var requiredJob = ResolveSingleJob(row.ClassJobCategory0.RowId);

            var prereqs = new List<uint>();
            foreach (var p in row.PreviousQuest)
                if (p.RowId != 0) prereqs.Add(p.RowId);

            return new QuestMetadata(
                Name: name,
                Expansion: expansion,
                Category: category,
                MinLevel: minLevel > 0 ? minLevel : null,
                RequiredJob: requiredJob,
                PrerequisiteQuestIds: prereqs.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveExpansion(uint exVersionRowId) => exVersionRowId switch
    {
        0 => "arr",
        1 => "heavensward",
        2 => "stormblood",
        3 => "shadowbringers",
        4 => "endwalker",
        5 => "dawntrail",
        _ => "arr",  // unknown future expansion, default to arr
    };

    private string ResolveCategory(Lumina.Excel.Sheets.Quest row)
    {
        try
        {
            var genreId = row.JournalGenre.RowId;
            if (genreId == 0) return "side";

            var genreSheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.JournalGenre>()!;
            var genre = genreSheet.GetRow(genreId);
            var catId = genre.JournalCategory.RowId;

            return MapCategory(catId);
        }
        catch
        {
            return "side";
        }
    }

    // See LR5 for mapping rationale
    private static string MapCategory(uint journalCategoryId) => journalCategoryId switch
    {
        1 or 2 or 3 or 4 or 5 or 6 or 7 => "msq",
        9 => "class",
        10 => "job",
        11 => "role",
        _ => "side",
    };

    private string? ResolveSingleJob(uint classJobCategoryRowId)
    {
        if (classJobCategoryRowId == 0) return null;
        try
        {
            var catSheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.ClassJobCategory>()!;
            var cat = catSheet.GetRow(classJobCategoryRowId);
            var jobSheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!;

            uint? singleJobId = null;
            // ClassJobCategory has boolean columns for each ClassJob.
            // Iterate ClassJob sheet and check which ones are enabled in this category.
            foreach (var job in jobSheet)
            {
                if (job.RowId == 0) continue;
                if (!JobCategoryContainsJob(cat, job.RowId)) continue;

                if (singleJobId is not null)
                    return null;  // multiple jobs allowed, not single-job locked
                singleJobId = job.RowId;
            }

            if (singleJobId is null) return null;
            var resolvedJob = jobSheet.GetRow(singleJobId.Value);
            return resolvedJob.Abbreviation.ToString().ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }

    // Note: the builder will need to determine the exact Lumina API for checking
    // whether a ClassJobCategory row contains a specific ClassJob. This may involve
    // checking named boolean properties on the ClassJobCategory row (e.g. cat.GLA, cat.PLD)
    // or using a helper similar to JobCategoryHelper.IsJobInCategory from the main repo.
    // The exact API depends on the Lumina version; the builder should reference the
    // Lumina source or use reflection if needed.
    private static bool JobCategoryContainsJob(
        Lumina.Excel.Sheets.ClassJobCategory cat, uint jobId)
    {
        // Implementation depends on Lumina API surface.
        // The ClassJobCategory sheet has boolean columns named after each job.
        // The builder should check the Lumina.Excel.Sheets.ClassJobCategory type
        // for the correct property access pattern.
        throw new NotImplementedException("Builder: implement per Lumina API");
    }

    public string? ResolveNpcName(uint npcId)
    {
        try
        {
            var sheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>()!;
            var row = sheet.GetRow(npcId);
            var name = row.Singular.ToString();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    public string? ResolveZoneName(uint zoneId)
    {
        try
        {
            var sheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()!;
            var row = sheet.GetRow(zoneId);
            var name = row.PlaceName.Value.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    public string? ResolveJobAbbreviation(uint jobId)
    {
        try
        {
            var sheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()!;
            var row = sheet.GetRow(jobId);
            return row.Abbreviation.ToString().ToUpperInvariant();
        }
        catch { return null; }
    }

    public void Dispose()
    {
        // Lumina.GameData may or may not implement IDisposable depending on version.
        // If it does, dispose it. If not, this is a no-op.
        if (_gameData is IDisposable d) d.Dispose();
    }
}
```

### Task 5 -- NuGet dependency

**File:** `QuestForge.Tools.Trace/QuestForge.Tools.Trace.csproj` (modified)

```xml
<ItemGroup>
  <PackageReference Include="Lumina" Version="4.*" />
</ItemGroup>
```

---

## Given-When-Then specifications

### Resolver interface + NullResolver

#### T1 -- NullResolver returns null for quest

Given: `NullMetadataResolver.Instance`
When: `ResolveQuest(66130)` is called
Then: returns `null`

#### T2 -- NullResolver returns null for NPC name

Given: `NullMetadataResolver.Instance`
When: `ResolveNpcName(1003987)` is called
Then: returns `null`

#### T3 -- NullResolver returns null for zone name

Given: `NullMetadataResolver.Instance`
When: `ResolveZoneName(182)` is called
Then: returns `null`

#### T4 -- NullResolver returns null for job abbreviation

Given: `NullMetadataResolver.Instance`
When: `ResolveJobAbbreviation(1)` is called
Then: returns `null`

### Extractor with NullResolver (backward compatibility)

#### T5 -- Extract with NullResolver produces same TODOs as before

Given: a trace with `RunStart(questId: 66130)` and a navigate+done decision pair (same trace shape as existing `TraceToQuestExtractorTests.Extract_NavigateAction_ProducesTravelStep`)
When: `new TraceToQuestExtractor(resolver: NullMetadataResolver.Instance).Extract(events)` is called
Then: `draft.Definition.Name == "TODO"` and `draft.Definition.Expansion == "TODO"` and `draft.Definition.Category == "TODO"`
And: `draft.Todos` contains "name (Lumina lookup required)", "expansion", "category", "requirements (level, job, prereqs)", "lastVerifiedPatch"

#### T6 -- Extract with default constructor produces same TODOs (no regression)

Given: same trace as T5
When: `new TraceToQuestExtractor().Extract(events)` is called
Then: same assertions as T5 -- proves the optional parameter defaults to NullResolver

### Extractor with FakeMetadataResolver

#### T7 -- Extract resolves name, expansion, category from metadata

Given: a trace with `RunStart(questId: 66130)` and a navigate+done decision pair
And: a fake resolver that returns `QuestMetadata("Coming to Ul'dah", "arr", "msq", 1, null, [])` for quest 66130
When: `new TraceToQuestExtractor(resolver: fakeResolver).Extract(events)` is called
Then: `draft.Definition.Name == "Coming to Ul'dah"`
And: `draft.Definition.Expansion == "arr"`
And: `draft.Definition.Category == "msq"`
And: `draft.Definition.Requirements!.MinLevel == 1`
And: `draft.Definition.Requirements!.RequiredJob` is null
And: `draft.Definition.Requirements!.Prereqs` is empty

#### T8 -- Resolved metadata removes TODOs for name, expansion, category, requirements

Given: same setup as T7
When: Extract is called
Then: `draft.Todos` does NOT contain "name (Lumina lookup required)"
And: `draft.Todos` does NOT contain "expansion"
And: `draft.Todos` does NOT contain "category"
And: `draft.Todos` does NOT contain "requirements (level, job, prereqs)"
And: `draft.Todos` DOES contain "lastVerifiedPatch" (still a manual TODO)

#### T9 -- Prerequisites are mapped to PrerequisiteRef array

Given: a trace with `RunStart(questId: 66130)`
And: a fake resolver returns `QuestMetadata("Test", "arr", "msq", 1, null, [66100, 66110])` for quest 66130
When: Extract is called
Then: `draft.Definition.Requirements!.Prereqs.Length == 2`
And: `draft.Definition.Requirements!.Prereqs[0] == new PrerequisiteRef(66100, "complete")`
And: `draft.Definition.Requirements!.Prereqs[1] == new PrerequisiteRef(66110, "complete")`

#### T10 -- RequiredJob is populated when metadata provides it

Given: a trace with `RunStart(questId: 65000)`
And: a fake resolver returns `QuestMetadata("Some Job Quest", "arr", "job", 30, "GLA", [])` for quest 65000
When: Extract is called
Then: `draft.Definition.Requirements!.RequiredJob == "GLA"`

#### T11 -- Resolver returning null for unknown quest ID falls back to TODOs

Given: a trace with `RunStart(questId: 99999)`
And: a fake resolver returns `null` for quest 99999
When: Extract is called
Then: `draft.Definition.Name == "TODO"` (all TODOs present, identical to NullResolver)

#### T12 -- StepRecorded fast path also applies metadata

Given: a trace with `RunStart(questId: 66130)` and `StepRecordedEvent` entries (authoring trace path)
And: a fake resolver returns `QuestMetadata("Coming to Ul'dah", "arr", "msq", 1, null, [])` for quest 66130
When: Extract is called
Then: `draft.Definition.Name == "Coming to Ul'dah"` (metadata applied on fast path too)
And: `draft.Todos` does NOT contain "name (Lumina lookup required)"

### CLI arg parsing

#### T13 -- --sqpack flag is parsed

Given: args `["extract-quest", "run.jsonl", "--sqpack", "/path/to/sqpack"]`
When: `CliArgsParser.Parse(args)` is called
Then: `result.Subcommand == ExtractQuest`
And: `result.TracePath == "run.jsonl"`
And: `result.SqpackPath == "/path/to/sqpack"`
And: `result.ParseError` is null

#### T14 -- --sqpack without value is a parse error

Given: args `["extract-quest", "run.jsonl", "--sqpack"]`
When: `CliArgsParser.Parse(args)` is called
Then: `result.ParseError` contains "requires a value"

#### T15 -- --sqpack absent means SqpackPath is null

Given: args `["extract-quest", "run.jsonl"]`
When: `CliArgsParser.Parse(args)` is called
Then: `result.SqpackPath` is null

#### T16 -- Existing flags still work alongside --sqpack

Given: args `["extract-quest", "run.jsonl", "--sqpack", "/path", "--out", "draft.json"]`
When: `CliArgsParser.Parse(args)` is called
Then: `result.SqpackPath == "/path"`
And: `result.OutputPath == "draft.json"`
And: `result.ParseError` is null

### SqpackPathResolver

#### T17 -- Resolve returns null when no standard paths exist

Given: running on a machine without FFXIV installed (neither standard path exists)
When: `SqpackPathResolver.Resolve()` is called
Then: returns `null`

Note: This test cannot be deterministic across machines. In CI (Linux), neither path exists, so it always returns null. On a developer machine with FFXIV installed, it may return a path. The test should verify the return type is `string?` and that no exception is thrown. For a deterministic unit test, extract the candidate paths as a parameter.

### LuminaMetadataResolver (integration -- requires game data)

These tests require real FFXIV game data and should be marked with `[Trait("Category", "Integration")]` and skipped in CI.

#### T18 -- ResolveQuest for known ARR MSQ quest

Given: `LuminaMetadataResolver` constructed with a valid sqpack path
When: `ResolveQuest(66130)` is called (Coming to Ul'dah)
Then: `result.Name` is "Coming to Ul'dah" (or the English localized name)
And: `result.Expansion == "arr"`
And: `result.Category == "msq"`
And: `result.MinLevel` is 1

#### T19 -- ResolveQuest for nonexistent quest ID

Given: `LuminaMetadataResolver` constructed with a valid sqpack path
When: `ResolveQuest(0)` is called
Then: returns `null`

#### T20 -- ResolveNpcName for known NPC

Given: `LuminaMetadataResolver` constructed with a valid sqpack path
When: `ResolveNpcName(1003987)` is called (Wymond)
Then: returns a non-null, non-empty string

#### T21 -- ResolveZoneName for known zone

Given: `LuminaMetadataResolver` constructed with a valid sqpack path
When: `ResolveZoneName(182)` is called (Ul'dah - Steps of Nald)
Then: returns a non-null, non-empty string

#### T22 -- Constructor with invalid sqpack path throws

Given: a path that does not exist (e.g. `"C:\nonexistent\sqpack"`)
When: `new LuminaMetadataResolver(path)` is called
Then: throws an exception (Lumina cannot initialize without valid data files)

---

## Implementation order

### Phase A -- Interface + NullResolver + FakeResolver

1. Create `IQuestMetadataResolver.cs` and `QuestMetadata` record
2. Create `NullMetadataResolver.cs`
3. Create `FakeMetadataResolver` in test project (dictionary-backed, returns canned data)
4. Write and pass tests T1--T4

Duration estimate: 1 hour.

### Phase B -- Extractor integration

1. Add `_resolver` field and constructor parameter to `TraceToQuestExtractor`
2. Extract shared `ApplyMetadata` method
3. Wire into both extraction paths (observation-based and StepRecorded-based)
4. Write and pass tests T5--T12

Duration estimate: 2--3 hours.
Done-before-next: T5 and T6 green (backward compatibility proven before adding new behavior).

### Phase C -- CLI changes

1. Add `SqpackPath` to `CliArgs`
2. Add `--sqpack` to `CliArgsParser`
3. Create `SqpackPathResolver`
4. Wire `RunExtractQuest` in Program.cs
5. Update help text
6. Write and pass tests T13--T17

Duration estimate: 1--2 hours.
Done-before-next: T13--T16 green (CLI parsing correct before wiring Lumina).

### Phase D -- LuminaMetadataResolver

1. Add Lumina NuGet to csproj
2. Implement `LuminaMetadataResolver`
3. Determine `ClassJobCategory` boolean column access pattern from Lumina API
4. Write integration tests T18--T22 (marked as integration, skipped in CI)
5. Manual test: run `qf-trace extract-quest trace.jsonl --sqpack <path>` and verify output

Duration estimate: 3--4 hours.
Done-before-next: `dotnet build` succeeds. Integration tests pass locally.

### Phase E -- Category mapping validation

1. Run `LuminaMetadataResolver` against 10--20 known quest IDs covering MSQ, class, job, role, blue, side
2. Verify the JournalCategory mapping produces correct category strings
3. Adjust `MapCategory` if any IDs are wrong
4. Document final validated mapping in code comments

Duration estimate: 1--2 hours (manual verification).

---

## Done criteria

1. `dotnet build` succeeds for `QuestForge.Tools.Trace` with the Lumina dependency
2. `dotnet test QuestForge.Tools.Trace.Tests` passes -- all existing tests unbroken, new tests green
3. `qf-trace extract-quest trace.jsonl` (no `--sqpack`) produces identical output to before on a machine without FFXIV
4. `qf-trace extract-quest trace.jsonl --sqpack <valid-path>` produces a draft with resolved `name`, `expansion`, `category`, and `requirements` fields
5. The TODO list in the output no longer contains "name (Lumina lookup required)", "expansion", "category", "requirements (level, job, prereqs)" when sqpack is available
6. The TODO list still contains "lastVerifiedPatch" regardless of sqpack availability
7. `qf-trace extract-quest trace.jsonl --sqpack /nonexistent` gracefully falls back to NullResolver behavior (all TODOs present, no crash)
8. `--sqpack` appears in `qf-trace --help` output
9. Integration tests for LuminaMetadataResolver are marked `[Trait("Category", "Integration")]` and pass locally with game data

---

## What this plan does NOT include

- **NPC name / zone name injection into draft output.** The resolver interface supports these lookups but the extractor does not use them yet. Future work could add `// NPC: Minfilia` comments or populate `notes`.
- **Blue-urgent vs blue distinction.** The category mapping maps all feature-unlock quests as "blue". Authors must manually override to "blue-urgent" for critical unlocks. Automating this would require maintaining a curated list of critical unlock quest IDs.
- **Linux/Mac sqpack path auto-detection.** Only Windows standard paths are probed.
- **Lumina version pinning.** The csproj uses `4.*` wildcard. If Lumina 5 introduces breaking changes, we pin then.
- **ClassJobCategory multi-job representation.** When a quest allows multiple jobs, `RequiredJob` is left null. The schema's `RequiredJob` field is a single string; representing multi-job requirements would be a schema change.
- **Chain field population.** The `Chain` field on `QuestDefinition` is not populated from `PreviousQuest` data. Chain is a separate concern (forward/backward links between quest files) unrelated to requirements.
- **Caching of Lumina data.** `LuminaMetadataResolver` creates a fresh `GameData` instance per CLI invocation. Since `extract-quest` only calls `ResolveQuest` once (for the single quest ID in the trace), caching is unnecessary.

---

READY FOR TEST CREATION

Tester: Write failing tests from the GWT specs in T1--T22.
- Happy paths: 8 scenarios (T1--T4, T5, T7, T13, T15)
- Edge cases: 6 scenarios (T6, T10, T11, T12, T16, T17)
- Error cases: 2 scenarios (T14, T22)
- Integration (require game data): 4 scenarios (T18--T21)
- Expected total: ~20 tests in QuestForge.Tools.Trace.Tests
