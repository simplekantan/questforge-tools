# State Changes Plan: `qf-trace state-changes <trace.jsonl>`

**Status:** ready to implement
**Input docs:** issue #16 in questforge-tools
**Output:** `qf-trace state-changes <trace.jsonl>` prints a timeline of quest state transitions correlated with the decisions that triggered them
**Phase dependencies:** None; uses existing `TraceEventParser`, `CliArgsParser`, `CliArgs`, `OutputFormatters` infrastructure

---

## Dependency graph

Single repo (`questforge-tools`), no cross-repo dependencies:

```
QuestForge.Tools.Trace/Analysis/QuestStateChangeAnalyzer.cs     <-- new
QuestForge.Tools.Trace/Analysis/QuestStateChangeReport.cs        <-- new
QuestForge.Tools.Trace/Cli/CliSubcommand.cs                      <-- add StateChanges
QuestForge.Tools.Trace/Cli/CliArgsParser.cs                       <-- add "state-changes" case
QuestForge.Tools.Trace/Cli/OutputFormatters.cs                    <-- add FormatStateChanges
qf-trace/Program.cs                                              <-- add RunStateChanges + dispatch + help
QuestForge.Tools.Trace.Tests/Analysis/QuestStateChangeAnalyzerTests.cs <-- new
```

Build order: result types first, analyzer, CLI wiring, tests.

---

## Architectural decisions

### SC1 -- Use pre-parsed TraceEvent list, not raw lines

Unlike `TraceValidator` (which needs raw lines for line numbers and malformed-JSON detection), `state-changes` is an analysis tool that operates on well-formed traces. It uses `TraceEventParser.ReadFile` to get `IReadOnlyList<TraceEvent>`, then walks the typed events. This avoids duplicating the JSON parsing logic in `SnapshotState`.

```csharp
public sealed class QuestStateChangeAnalyzer
{
    public QuestStateChangeReport Analyze(IReadOnlyList<TraceEvent> events);
}
```

**Rejected alternative:** Raw-line parsing like `TraceValidator`. Unnecessary complexity -- the analyzer doesn't need line numbers or malformed-line detection, and duplicating `SnapshotState`'s observation parsing would be fragile.

**Testability:** Tests build `TraceEvent` lists directly using constructors; no file I/O needed.

### SC2 -- Quest ID extracted from RunStartEvent

The analyzer scans for the first `RunStartEvent` and reads `Data.QuestId`. This determines which quest-scoped observations (GetQuestSequence, GetQuestFlags, GetQuestVariables) to track. If no `RunStartEvent` is found, the report has `QuestId = null` and no changes (the quest-scoped observations require an argument match, and without a quest ID there is nothing to match against).

```csharp
// Inside Analyze:
var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
uint? questId = runStart?.Data.QuestId;
```

**What breaks if violated:** Without filtering by quest ID, observations for unrelated quests (e.g. prerequisite checks) would pollute the report.

### SC3 -- Inline observation parsing, not SnapshotState reuse

`SnapshotState` is designed for the extractor's incremental snapshot model with reset semantics, combat spans, purchase spans, and key-item tracking -- none of which `state-changes` needs. The analyzer does its own minimal parsing of three observation methods: `GetQuestSequence`, `GetQuestFlags`, `GetQuestVariables`. The parsing shapes (value-wrapped vs raw) are identical to `SnapshotState` lines 237-305 but the code is ~30 lines total vs importing all of `SnapshotState`.

```csharp
// Parsing GetQuestSequence value:
if (val.TryGetInt32(out var seq))
    currentSequence = seq;
// (SnapshotState line 297-298 uses the same shape)
```

**Rejected alternative:** Reusing `SnapshotState` and calling `Apply` on every observation. This would work but creates a coupling to the extractor's complex state machine. The analyzer's needs are trivial -- three last-seen values.

**What breaks if violated:** If `SnapshotState` adds new reset/lifecycle semantics, the analyzer would silently inherit behavior it doesn't want.

### SC4 -- Quest argument matching mirrors SnapshotState.QuestArgMatches

Quest-scoped observations carry an `argument` field with the quest ID. The analyzer must match `{"value": N}` (object-wrapped) and plain `N` (raw number) forms, exactly like `SnapshotState.QuestArgMatches` (lines 506-524). This is a ~10-line private method duplicated from `SnapshotState` rather than extracted to a shared utility, because the duplication is trivial and extracting it would create a public API surface that signals "this is a reusable utility" when it is really just a JSON shape detail.

```csharp
private static bool QuestArgMatches(ObservationEvent ev, uint questId)
{
    if (!ev.Data.Argument.HasValue) return false;
    var arg = ev.Data.Argument.Value;
    if (arg.ValueKind == JsonValueKind.Number)
    {
        try { return arg.GetUInt32() == questId; } catch { return false; }
    }
    if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var v))
    {
        try { return v.GetUInt32() == questId; } catch { return false; }
    }
    return false;
}
```

### SC5 -- Correlation uses the most-recent DecisionEvent

Each state change is correlated with the last `DecisionEvent` seen before the observation that produced the change. This is the "decision that likely caused it" heuristic. If no `DecisionEvent` has been seen yet, `AfterStepId` and `AfterActionType` are both `null`.

```csharp
DecisionEvent? lastDecision = null;
// ... in the event loop:
case DecisionEvent d:
    lastDecision = d;
    break;
case ObservationEvent obs:
    // ... check for state changes, correlate with lastDecision
```

**Rejected alternative:** Forward-scanning (find the decision after the change). The engine's observation polling happens after action dispatch, so the preceding decision is the causal one.

### SC6 -- Flags bit-diff as human-readable detail

When `GetQuestFlags` changes, the `Detail` string describes which bits changed:

```csharp
private static string? ComputeFlagsDiff(uint oldFlags, uint newFlags)
{
    var set = newFlags & ~oldFlags;
    var cleared = oldFlags & ~newFlags;
    var parts = new List<string>();
    if (set != 0) parts.Add(FormatBits(set, "set"));
    if (cleared != 0) parts.Add(FormatBits(cleared, "cleared"));
    return parts.Count > 0 ? string.Join("; ", parts) : null;
}

private static string FormatBits(uint mask, string verb)
{
    var bits = new List<int>();
    for (int i = 0; i < 32; i++)
        if ((mask & (1u << i)) != 0) bits.Add(i);
    var label = bits.Count == 1 ? "bit" : "bits";
    return $"{label} {string.Join(",", bits)} {verb}";
}
```

Output examples: `"bit 2 set"`, `"bits 2,4 set"`, `"bits 2,4 set; bit 1 cleared"`.

### SC7 -- Variables formatted as bracket arrays

Quest variables are `byte[6]` (or potentially shorter). They are formatted as `[v0,v1,v2,v3,v4,v5]` for both `PreviousValue` and `NewValue`. The `Detail` field is `null` for variables changes -- the array diff is self-explanatory.

```csharp
// "[0,0,0,0,0,0]"
private static string FormatVariables(byte[] vars)
    => "[" + string.Join(",", vars) + "]";
```

### SC8 -- First observation establishes baseline, never emits a change

The first time each of `GetQuestSequence`, `GetQuestFlags`, `GetQuestVariables` is seen for the target quest, the value is stored as the baseline. No `QuestStateChange` is emitted. Only subsequent observations that differ from the last-seen value produce a change record. This mirrors `SnapshotState`'s baseline behavior for quest variables (line 261: "First observation: establish the baseline only").

**What breaks if violated:** The initial read of sequence=0, flags=0 would appear as a "change from null" which is noise, not a meaningful state transition.

### SC9 -- Seq field on QuestStateChange is the TraceEvent.Seq

`QuestStateChange.Seq` is the `Seq` field from the `ObservationEvent` that triggered the change. This provides a stable position reference into the trace file. It is NOT the quest sequence value (which is `PreviousValue`/`NewValue` for `Kind == "sequence"`).

### SC10 -- No quest-data root needed

Like `validate` and `redact`, `state-changes` does not need `--quest-data`. The dispatch arm in `Program.cs` is placed before the `resolvedRoot` resolution block.

### SC11 -- CLI subcommand is "state-changes" (hyphenated)

Matches the existing `extract-fixture`, `validate-fixture`, `extract-quest` naming convention. The `CliSubcommand` enum variant is `StateChanges`.

### SC12 -- GetQuestFlags parsing: plain uint (not value-wrapped)

Looking at `SnapshotState` line 301-303, `GetQuestFlags` values are parsed as plain `uint` via `TryGetUInt32`. The real trace (with-attunement.trace.jsonl line 9) confirms: `"value":0` is a plain number at the top level. However, for robustness the analyzer handles both plain uint and `{"value": N}` wrapping, matching the defensive pattern used for other observation methods.

```csharp
// Parse flags value: plain uint or {"value": uint}
uint newFlags;
if (val.TryGetUInt32(out var f))
    newFlags = f;
else if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var fv) && fv.TryGetUInt32(out var f2))
    newFlags = f2;
else break;
```

---

## Task breakdown

### Task 1 -- Result types

**File:** `QuestForge.Tools.Trace/Analysis/QuestStateChangeReport.cs`

```csharp
namespace QuestForge.Tools.Trace.Analysis;

public sealed record QuestStateChangeReport(
    uint? QuestId,
    IReadOnlyList<QuestStateChange> Changes);

public sealed record QuestStateChange(
    int Seq,
    string Kind,
    string PreviousValue,
    string NewValue,
    string? Detail,
    string? AfterStepId,
    string? AfterActionType);
```

`Kind` is one of `"sequence"`, `"flags"`, `"variables"`.

### Task 2 -- QuestStateChangeAnalyzer

**File:** `QuestForge.Tools.Trace/Analysis/QuestStateChangeAnalyzer.cs`

```csharp
namespace QuestForge.Tools.Trace.Analysis;

public sealed class QuestStateChangeAnalyzer
{
    public QuestStateChangeReport Analyze(IReadOnlyList<TraceEvent> events)
    {
        var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
        uint? questId = runStart?.Data.QuestId;

        if (questId is null)
            return new QuestStateChangeReport(null, []);

        var changes = new List<QuestStateChange>();
        DecisionEvent? lastDecision = null;

        int? lastSequence = null;
        uint? lastFlags = null;
        byte[]? lastVariables = null;

        foreach (var ev in events)
        {
            switch (ev)
            {
                case DecisionEvent d:
                    lastDecision = d;
                    break;

                case ObservationEvent obs:
                    if (obs.Data.Method == "GetQuestSequence"
                        && QuestArgMatches(obs, questId.Value))
                    {
                        // Parse sequence value...
                        // If lastSequence is null, set baseline
                        // If changed, record change
                    }
                    // Similar for GetQuestFlags and GetQuestVariables
                    break;
            }
        }

        return new QuestStateChangeReport(questId, changes);
    }
}
```

Private helpers: `QuestArgMatches`, `ComputeFlagsDiff`, `FormatBits`, `FormatVariables`, `ParseSequenceValue`, `ParseFlagsValue`, `ParseVariablesValue`.

### Task 3 -- CLI wiring

**3a. `CliSubcommand.cs`** -- add `StateChanges` variant:
```csharp
StateChanges,
```

**3b. `CliArgsParser.cs`** -- add `"state-changes"` to the switch:
```csharp
"state-changes" => CliSubcommand.StateChanges,
```

The positional argument routes to `TracePath` (same field as `extract-fixture`).

**3c. `OutputFormatters.cs`** -- add `FormatStateChanges`:
```csharp
public static string FormatStateChanges(QuestStateChangeReport report)
{
    var sb = new StringBuilder();

    if (report.QuestId is null)
    {
        sb.Append("No run.start found; cannot determine quest ID.");
        return sb.ToString();
    }

    sb.Append($"Quest {report.QuestId} state changes:");

    if (report.Changes.Count == 0)
    {
        sb.Append("\n  (none)");
        return sb.ToString();
    }

    foreach (var c in report.Changes)
    {
        sb.Append($"\n  seq {c.Seq,-4}");
        sb.Append($" {c.Kind,-9}");
        sb.Append($" {c.PreviousValue}->{c.NewValue}");
        if (c.Detail is not null)
            sb.Append($"  ({c.Detail})");
        if (c.AfterStepId is not null)
            sb.Append($"  after: {c.AfterStepId} / {c.AfterActionType}");
    }

    return sb.ToString();
}
```

**3d. `Program.cs`** -- add `RunStateChanges` method and dispatch arm:
```csharp
// Dispatch (before resolvedRoot block, alongside validate and redact):
if (cliArgs.Subcommand == CliSubcommand.StateChanges)
    return RunStateChanges(cliArgs);

private static int RunStateChanges(CliArgs cliArgs)
{
    if (cliArgs.TracePath is null)
    {
        Console.Error.WriteLine("qf-trace: state-changes requires <trace.jsonl>");
        return 1;
    }
    if (!File.Exists(cliArgs.TracePath))
    {
        Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
        return 1;
    }

    var events = TraceEventParser.ReadFile(cliArgs.TracePath, Console.Error);
    var report = new QuestStateChangeAnalyzer().Analyze(events);
    Console.Out.WriteLine(OutputFormatters.FormatStateChanges(report));
    return 0;
}
```

**3e. `PrintHelp()`** -- add state-changes subcommand help text:
```csharp
Console.WriteLine("  state-changes <trace.jsonl>");
Console.WriteLine("    Show a timeline of quest state transitions correlated with decisions.");
Console.WriteLine();
```

### Task 4 -- Tests

**File:** `QuestForge.Tools.Trace.Tests/Analysis/QuestStateChangeAnalyzerTests.cs`

All tests construct `List<TraceEvent>` directly and call `Analyze`. See GWT specs below.

---

## Given-When-Then specifications

### T1 -- Happy path: sequence changes

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
DecisionEvent { StepId = "accept-quest", ActionType = "Interact" }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** Report has `QuestId == 65644`, one change: `Kind == "sequence"`, `PreviousValue == "0"`, `NewValue == "1"`, `Detail == null`, `AfterStepId == "accept-quest"`, `AfterActionType == "Interact"`.

### T2 -- Happy path: flags change with bit diff

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
DecisionEvent { StepId = "travel-to-camp", ActionType = "Navigate" }
ObservationEvent { Method = "GetQuestFlags", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestFlags", Argument = {"value":65644}, Value = 4 }
```
**When:** `Analyze(events)`
**Then:** One change: `Kind == "flags"`, `PreviousValue == "0"`, `NewValue == "4"`, `Detail == "bit 2 set"`, `AfterStepId == "travel-to-camp"`.

### T3 -- Happy path: variables change

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
DecisionEvent { StepId = "use-item", ActionType = "UseItem" }
ObservationEvent { Method = "GetQuestVariables", Argument = {"value":65644}, Value = [0,0,0,0,0,0] }
ObservationEvent { Method = "GetQuestVariables", Argument = {"value":65644}, Value = [1,0,0,0,0,0] }
```
**When:** `Analyze(events)`
**Then:** One change: `Kind == "variables"`, `PreviousValue == "[0,0,0,0,0,0]"`, `NewValue == "[1,0,0,0,0,0]"`, `Detail == null`, `AfterStepId == "use-item"`.

### T4 -- First observation is baseline, no change emitted

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
```
**When:** `Analyze(events)`
**Then:** Report has zero changes. The first observation sets the baseline but does not produce a change.

### T5 -- No RunStartEvent

**Given:** Events:
```
DecisionEvent { StepId = "s1", ActionType = "Navigate" }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** Report has `QuestId == null`, zero changes.

### T6 -- Quest ID filtering: observations for other quests ignored

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":99999}, Value = 5 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** One change (0->1 for quest 65644). The observation for quest 99999 is ignored.

### T7 -- Repeated same value does not emit duplicate change

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** One change (0->1). The repeated 0 does not produce a second change.

### T8 -- Multiple flags bits set and cleared

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestFlags", Argument = {"value":65644}, Value = 5 }
ObservationEvent { Method = "GetQuestFlags", Argument = {"value":65644}, Value = 18 }
```
**When:** `Analyze(events)`
**Then:** One change: `PreviousValue == "5"`, `NewValue == "18"`. 5 = bits 0,2. 18 = bits 1,4. Set = 18 & ~5 = 18 (bits 1,4). Cleared = 5 & ~18 = 5 (bits 0,2). `Detail == "bits 1,4 set; bits 0,2 cleared"`.

### T9 -- No decision before first change

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** One change with `AfterStepId == null`, `AfterActionType == null`.

### T10 -- Multiple decisions: each change correlates with most recent

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
DecisionEvent { StepId = "s1", ActionType = "Interact" }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
DecisionEvent { StepId = "s2", ActionType = "Navigate" }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 2 }
```
**When:** `Analyze(events)`
**Then:** Two changes: first (0->1) with `AfterStepId == "s1"`, second (1->2) with `AfterStepId == "s2"`.

### T11 -- Mixed state types: sequence + flags + variables

**Given:** Events with all three state types changing at different points (sequence 0->1, flags 0->4, variables [0,0,0,0,0,0]->[1,0,0,0,0,0]).
**When:** `Analyze(events)`
**Then:** Three changes in chronological order (the order they appear in the event list).

### T12 -- Seq field is TraceEvent.Seq, not quest sequence

**Given:** Events:
```
RunStartEvent { Seq = 0, QuestId = 65644 }
ObservationEvent { Seq = 5, Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Seq = 12, Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** The change has `Seq == 12` (the trace event seq), not 1 (the quest sequence value).

### T13 -- Value-wrapped GetQuestSequence: {"value": N} shape

**Given:** Events where GetQuestSequence value is `{"value": 0}` then `{"value": 1}` (object-wrapped).
**When:** `Analyze(events)`
**Then:** Correctly parses both forms. One change (0->1).

### T14 -- Plain-number quest argument (old trace format)

**Given:** Events where `Argument` is plain `65644` (not `{"value":65644}`).
**When:** `Analyze(events)`
**Then:** Correctly matches quest ID. Change is recorded.

### T15 -- Variables: value-wrapped shape

**Given:** Events where GetQuestVariables value is `{"value":[0,0,0,0,0,0]}` then `{"value":[1,0,0,0,0,0]}`.
**When:** `Analyze(events)`
**Then:** Correctly parses wrapped arrays. One change.

### T16 -- FormatStateChanges: no run.start

**Given:** `QuestStateChangeReport(null, [])`
**When:** `FormatStateChanges(report)`
**Then:** Output is `"No run.start found; cannot determine quest ID."`.

### T17 -- FormatStateChanges: no changes

**Given:** `QuestStateChangeReport(65644, [])`
**When:** `FormatStateChanges(report)`
**Then:** Output is `"Quest 65644 state changes:\n  (none)"`.

### T18 -- FormatStateChanges: full output

**Given:** Report with QuestId=65644 and one sequence change (seq=12, 0->1, after s1/Interact).
**When:** `FormatStateChanges(report)`
**Then:** Output contains `"Quest 65644 state changes:"` and a line with `"seq 12"`, `"sequence"`, `"0->1"`, `"after: s1 / Interact"`.

### T19 -- CliArgsParser: "state-changes" recognized

**Given:** `args = ["state-changes", "trace.jsonl"]`
**When:** `CliArgsParser.Parse(args)`
**Then:** `Subcommand == CliSubcommand.StateChanges`, `TracePath == "trace.jsonl"`.

### T20 -- Flags value-wrapped shape

**Given:** Events where GetQuestFlags value is `{"value": 4}` (object-wrapped), preceded by baseline `{"value": 0}`.
**When:** `Analyze(events)`
**Then:** Correctly parses wrapped uint. One change (0->4) with `Detail == "bit 2 set"`.

### T21 -- Failure-shaped observation value is ignored

**Given:** Events:
```
RunStartEvent { QuestId = 65644 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 0 }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = {"failure":"timeout"} }
ObservationEvent { Method = "GetQuestSequence", Argument = {"value":65644}, Value = 1 }
```
**When:** `Analyze(events)`
**Then:** One change (0->1). The failure-shaped observation does not update the baseline or produce a change.

### T22 -- Single bit set detail format

**Given:** Flags change from 0 to 1 (bit 0 set).
**When:** `ComputeFlagsDiff(0, 1)` (or observe via Analyze)
**Then:** Detail is `"bit 0 set"` (singular "bit", not "bits").

### T23 -- Multiple bits set detail format

**Given:** Flags change from 0 to 6 (bits 1,2 set).
**When:** `ComputeFlagsDiff(0, 6)` (or observe via Analyze)
**Then:** Detail is `"bits 1,2 set"` (plural "bits").

---

## Implementation order

### Phase A -- Types (est. 10 min)
1. Create `QuestForge.Tools.Trace/Analysis/QuestStateChangeReport.cs` (Task 1)
2. Create test file skeleton with all T1--T23 as `[Fact]` stubs

Done-before-next: types compile, tests compile (all red or skip).

### Phase B -- Analyzer core (est. 1 hour)
1. Create `QuestForge.Tools.Trace/Analysis/QuestStateChangeAnalyzer.cs` (Task 2)
2. Implement observation parsing for all three methods
3. Implement quest argument matching
4. Implement flags bit-diff computation
5. All T1--T15 and T20--T23 green

Done-before-next: `dotnet test` passes all analyzer unit tests.

### Phase C -- CLI wiring + formatter (est. 30 min)
1. Add `StateChanges` to `CliSubcommand`
2. Add `"state-changes"` case to `CliArgsParser`
3. Add `FormatStateChanges` to `OutputFormatters`
4. Add `RunStateChanges` to `Program.cs` + dispatch arm
5. Update `PrintHelp`
6. T16--T19 green

Done-before-next: `dotnet test` passes all tests.

---

## Done criteria

1. `qf-trace state-changes good-trace.jsonl` exits 0 and prints a timeline of quest state transitions
2. `qf-trace state-changes` (no file) exits 1 with usage message on stderr
3. `qf-trace state-changes nonexistent.jsonl` exits 1 with "not found" message on stderr
4. All unit tests in `QuestStateChangeAnalyzerTests` green
5. `dotnet test QuestForge.Tools.Trace.Tests` exits 0

---

## Exclusions

- **`--format json` output** -- text only in this PR; JSON format can be added later
- **`--quest` filter flag** -- traces contain only one quest's run; multi-quest filtering is not needed yet
- **IsQuestAccepted / IsQuestComplete changes** -- could be added later but are less interesting than sequence/flags/variables
- **Diff detail for variables** -- the array diff is self-explanatory; per-index annotation (e.g. "index 0: 0->1") is not included
- **Color/ANSI output** -- plain text only
- **quest-data root resolution** -- this subcommand does not need quest metadata

---

```
READY FOR TEST CREATION

Tester: Write failing tests from the GWT specs in Task 4.
- Happy paths: 5 scenarios (T1, T2, T3, T11, T18)
- Edge cases: 10 scenarios (T4, T6, T7, T8, T10, T12, T13, T14, T15, T20)
- Error cases: 8 scenarios (T5, T9, T16, T17, T19, T21, T22, T23)
- Expected total: ~23 tests in QuestForge.Tools.Trace.Tests
```
