# Trace Validate Plan: `qf-trace validate <trace.jsonl>`

**Status:** ready to implement
**Input docs:** TRACE_FORMAT.md SS9.3, issue #14 in questforge-tools
**Output:** `qf-trace validate <trace.jsonl>` exits 0 on clean trace, 1 on errors, 2 on warnings-only with `--fail-on-warning`
**Phase dependencies:** None; uses existing `CliArgsParser`, `CliArgs`, `OutputFormatters` infrastructure

---

## Dependency graph

Single repo (`questforge-tools`), no cross-repo dependencies:

```
QuestForge.Tools.Trace/Validation/TraceValidator.cs       <-- new
QuestForge.Tools.Trace/Validation/TraceValidationResult.cs <-- new
QuestForge.Tools.Trace/Cli/CliSubcommand.cs               <-- add ValidateTrace
QuestForge.Tools.Trace/Cli/CliArgsParser.cs                <-- add "validate" case
QuestForge.Tools.Trace/Cli/OutputFormatters.cs             <-- add FormatTraceIssues
qf-trace/Program.cs                                       <-- add RunValidateTrace + dispatch + help
QuestForge.Tools.Trace.Tests/Validation/TraceValidatorTests.cs <-- new
```

Build order: types first, validator, CLI wiring, tests.

---

## Architectural decisions

### TV1 — Raw-line parsing, not TraceEventParser

`TraceValidator` reads raw `string[]` lines (or a file path), NOT pre-parsed `TraceEvent` objects. Reason: `TraceEventParser.ReadFile` silently skips malformed lines and discards line numbers. The validator must:
- Report which line number is malformed
- Detect missing `v`, `seq`, `runId`, `type` fields even on lines that fail full deserialization
- Report all issues, not just the first

```csharp
public sealed class TraceValidator
{
    public TraceValidationResult Validate(string filePath);
    public TraceValidationResult Validate(IReadOnlyList<string> lines);
}
```

The file-path overload calls `File.ReadAllLines` then delegates to the lines overload. This keeps the core logic pure and testable without disk I/O.

**Rejected alternative:** Wrapping `TraceEventParser` with error collection. This would require invasive changes to `TraceEventParser` and still wouldn't give us line numbers or access to raw JSON for field-presence checks.

**Testability:** All tests use the `Validate(IReadOnlyList<string> lines)` overload. No file system needed.

### TV2 — Use JsonDocument for per-line field extraction

Each non-blank line is parsed with `JsonDocument.Parse`. If that throws `JsonException`, emit error `TV-E001`. If it succeeds, extract `v`, `seq`, `runId`, `type` from the `JsonElement` root. This avoids deserializing into typed `TraceEvent` objects (which would fail on unknown types or missing discriminators) while still validating structural fields.

```csharp
// Inside the validation loop:
using var doc = JsonDocument.Parse(line);
var root = doc.RootElement;
// root.TryGetProperty("v", out var vProp) etc.
```

**Why not STJ deserialization:** The validator must work on traces with unknown event types (future `v` values, custom extensions). `JsonDocument` is type-agnostic.

**What breaks if violated:** Using typed deserialization would miss errors on lines with unknown `type` discriminators (STJ throws, line gets skipped, no error reported).

### TV3 — Separate result type from FixtureValidationResult

`TraceValidationResult` and `TraceValidationIssue` are new types in `QuestForge.Tools.Trace/Validation/`, NOT reusing `FixtureValidationResult`. Reasons:
- `TraceValidationIssue` carries `int? LineNumber` (fixture issues don't have line numbers)
- Different error code namespace (`TV-E*` / `TV-W*` vs `fixture/*`)
- Keeps the types cohesive

```csharp
public sealed record TraceValidationResult(
    IReadOnlyList<TraceValidationIssue> Errors,
    IReadOnlyList<TraceValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
}

public sealed record TraceValidationIssue(
    string Code,
    string Message,
    int? LineNumber = null);
```

**Rejected alternative:** A shared `ValidationIssue<T>` generic. Over-abstraction for two concrete cases with different shapes.

### TV4 — Error codes use TV-E/TV-W prefix

Error codes: `TV-E001` through `TV-E007`. Warning codes: `TV-W001` through `TV-W003`. The `TV-` prefix distinguishes trace validation from fixture validation (`fixture/`) and quest validation (`structural/`).

| Code | Severity | Check |
|------|----------|-------|
| `TV-E001` | Error | Malformed JSON on line N |
| `TV-E002` | Error | Missing or non-integer `v` field |
| `TV-E003` | Error | `v` value is not 1 |
| `TV-E004` | Error | `seq` not strictly monotonic from 0 |
| `TV-E005` | Error | `runId` inconsistent across events |
| `TV-E006` | Error | `run.start` is not seq 0, or seq 0 is not `run.start` |
| `TV-E007` | Error | `run.end` is present but not the last event |
| `TV-E008` | Error | `type` field missing |
| `TV-W001` | Warning | Non-monotonic `ts` (ts decreased) |
| `TV-W002` | Warning | Missing `run.end` (trace may be from aborted run) |
| `TV-W003` | Warning | Empty trace (zero lines / zero non-blank lines) |

### TV5 — Single-pass validation with running state

The validator makes a single pass over the lines. Running state:

```csharp
int expectedSeq = 0;
string? runId = null;         // set from first event; compared on all subsequent
long lastTs = long.MinValue;  // for monotonic ts check
bool sawRunStart = false;
bool sawRunEnd = false;
int runEndLine = -1;          // line number of run.end if seen
int totalEvents = 0;          // count of successfully parsed events (for run.end-is-last check)
```

After the loop, post-checks:
- If `sawRunEnd && runEndLine != totalEvents - 1` -> TV-E007 (run.end not last)
- If `!sawRunEnd && totalEvents > 0` -> TV-W002 (missing run.end)
- If `totalEvents == 0` -> TV-W003 (empty trace)

**Rejected alternative:** Two-pass (scan for run.end first, then validate). Unnecessary complexity; the single-pass post-check handles it.

### TV6 — CLI subcommand is "validate" (not "validate-trace")

The subcommand is `validate` because the tool is already `qf-trace` -- `qf-trace validate <file.jsonl>` reads naturally. The positional argument routes to `TracePath` in `CliArgs` (same field used by `extract-fixture`).

The `CliSubcommand` enum gets a new `ValidateTrace` variant (not `Validate`, to avoid ambiguity with the existing `ValidateFixture`).

### TV7 — Blank lines are silently skipped, not errors

Blank lines (empty or whitespace-only) are silently skipped. They do not increment `expectedSeq`, do not count as events, and do not generate errors or warnings. This matches the behavior in `TraceEventParser.ReadText` and is consistent with JSONL conventions (trailing newlines, blank lines between events are harmless).

### TV8 — After TV-E001 (malformed JSON), skip all field checks for that line

If a line fails `JsonDocument.Parse`, only `TV-E001` is emitted for that line. The `seq`, `runId`, `v`, `type`, and `ts` checks are skipped (no data to check). The `expectedSeq` is NOT incremented -- we don't know what seq the line was supposed to have, so we can't advance the counter. This means subsequent valid lines will also fail TV-E004 if the malformed line was supposed to carry the next seq value. This is correct behavior: the trace is genuinely broken.

**What breaks if violated:** Incrementing `expectedSeq` on a malformed line would cause false negatives -- a gap created by the malformed line would be silently papered over.

### TV9 — `v` field validation: check presence, integer type, then value

Three-stage check on the `v` field:
1. `root.TryGetProperty("v", out var vProp)` -- if false, emit TV-E002 ("missing `v` field")
2. `vProp.ValueKind != JsonValueKind.Number` or `!vProp.TryGetInt32(out var vVal)` -- emit TV-E002 ("non-integer `v` field")
3. `vVal != 1` -- emit TV-E003 ($"`v` is {vVal}, expected 1")

Only one of TV-E002 or TV-E003 is emitted per line. TV-E002 suppresses TV-E003.

### TV10 — `seq` validation tolerates malformed-line gaps

`expectedSeq` only advances when a valid line has a correct `seq` value. Specifically:
- If `seq == expectedSeq`, advance `expectedSeq` to `seq + 1`
- If `seq != expectedSeq`, emit TV-E004 and set `expectedSeq = seq + 1` (resync to the actual value to avoid cascading errors on every subsequent line)

This resync prevents a single gap from generating N errors on all following lines.

### TV11 — OutputFormatters.FormatTraceIssues mirrors FormatIssues

Add a `FormatTraceIssues(TraceValidationResult)` method to `OutputFormatters` that produces text output matching the fixture validator style but with line numbers:

```
ERROR    [TV-E001]  line 3: malformed JSON: Unexpected character ...
ERROR    [TV-E004]  line 5: seq is 3, expected 2
WARNING  [TV-W001]  line 7: ts decreased from 1500 to 1200

2 error(s), 1 warning(s). Validation failed.
```

Issues without a line number (TV-W002, TV-W003) omit the "line N:" prefix.

---

## Task breakdown

### Task 1 -- Result types

**File:** `QuestForge.Tools.Trace/Validation/TraceValidationResult.cs`

```csharp
namespace QuestForge.Tools.Trace.Validation;

public sealed record TraceValidationResult(
    IReadOnlyList<TraceValidationIssue> Errors,
    IReadOnlyList<TraceValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
}

public sealed record TraceValidationIssue(
    string Code,
    string Message,
    int? LineNumber = null);
```

### Task 2 -- TraceValidator

**File:** `QuestForge.Tools.Trace/Validation/TraceValidator.cs`

```csharp
namespace QuestForge.Tools.Trace.Validation;

public sealed class TraceValidator
{
    public TraceValidationResult Validate(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        return Validate(lines);
    }

    public TraceValidationResult Validate(IReadOnlyList<string> lines)
    {
        // Single-pass implementation per TV5
    }
}
```

Core loop pseudo-structure:
```csharp
var errors = new List<TraceValidationIssue>();
var warnings = new List<TraceValidationIssue>();

int expectedSeq = 0;
string? firstRunId = null;
long lastTs = long.MinValue;
bool sawRunStart = false;
bool sawRunEnd = false;
int lastEventIndex = -1;   // 0-based index among successfully parsed events
int runEndEventIndex = -1;
int totalEvents = 0;

for (int i = 0; i < lines.Count; i++)
{
    int lineNum = i + 1; // 1-based
    var line = lines[i];

    if (string.IsNullOrWhiteSpace(line)) continue;

    // Parse JSON
    JsonDocument doc;
    try { doc = JsonDocument.Parse(line); }
    catch (JsonException ex)
    {
        errors.Add(new("TV-E001", $"malformed JSON: {ex.Message}", lineNum));
        continue; // TV8: skip all field checks
    }
    using (doc)
    {
        var root = doc.RootElement;

        // Check type (TV-E008)
        // Check v (TV-E002, TV-E003)
        // Check seq (TV-E004)
        // Check runId (TV-E005)
        // Check run.start / run.end (TV-E006)
        // Check ts (TV-W001)

        totalEvents++;
    }
}

// Post-loop checks: TV-E007, TV-W002, TV-W003
```

### Task 3 -- CLI wiring

**3a. `CliSubcommand.cs`** -- add `ValidateTrace` variant:
```csharp
public enum CliSubcommand
{
    None,
    Help,
    Unknown,
    ExtractFixture,
    ValidateFixture,
    ListFixtures,
    ExtractQuest,
    ValidateTrace,   // <-- new
}
```

**3b. `CliArgsParser.cs`** -- add `"validate"` to the switch:
```csharp
"validate" => CliSubcommand.ValidateTrace,
```

The positional argument routes to `TracePath` (same as `extract-fixture`).

**3c. `Program.cs`** -- add dispatch arm and `RunValidateTrace` method:
```csharp
CliSubcommand.ValidateTrace => RunValidateTrace(cliArgs),
```

```csharp
private static int RunValidateTrace(CliArgs cliArgs)
{
    if (cliArgs.TracePath is null)
    {
        Console.Error.WriteLine("qf-trace: validate requires <trace.jsonl>");
        return 1;
    }
    if (!File.Exists(cliArgs.TracePath))
    {
        Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
        return 1;
    }

    var validator = new TraceValidator();
    var result = validator.Validate(cliArgs.TracePath);
    Console.Out.Write(OutputFormatters.FormatTraceIssues(result));

    if (result.Errors.Count > 0) return 1;
    if (result.Warnings.Count > 0 && cliArgs.FailOnWarning) return 2;
    return 0;
}
```

`RunValidateTrace` does NOT require `resolvedRoot` (no quest-data dependency). Move the dispatch arm above the `resolvedRoot` resolution block, or pass it through without requiring it.

**3d. `OutputFormatters.cs`** -- add `FormatTraceIssues`:
```csharp
public static string FormatTraceIssues(TraceValidationResult result)
{
    var sb = new StringBuilder();

    foreach (var error in result.Errors)
    {
        var loc = error.LineNumber.HasValue ? $"line {error.LineNumber}: " : "";
        sb.Append($"ERROR    [{error.Code}]  {loc}{error.Message}\n");
    }
    foreach (var warning in result.Warnings)
    {
        var loc = warning.LineNumber.HasValue ? $"line {warning.LineNumber}: " : "";
        sb.Append($"WARNING  [{warning.Code}]  {loc}{warning.Message}\n");
    }

    if (result.Errors.Count > 0 || result.Warnings.Count > 0)
        sb.Append('\n');

    var passed = result.IsValid ? "Validation passed." : "Validation failed.";
    sb.Append($"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s). {passed}");
    return sb.ToString();
}
```

**3e. `PrintHelp()`** -- add validate subcommand help text:
```csharp
Console.WriteLine("  validate <trace.jsonl> [--fail-on-warning]");
Console.WriteLine("    Validate structural integrity of a JSONL trace file.");
Console.WriteLine();
```

### Task 4 -- Tests

**File:** `QuestForge.Tools.Trace.Tests/Validation/TraceValidatorTests.cs`

All tests use `TraceValidator.Validate(IReadOnlyList<string> lines)`. Tests build raw JSONL strings to exercise exact edge cases. See GWT specs below.

---

## Validation rule table

| Code | Severity | Rule | Suppressed when |
|------|----------|------|-----------------|
| TV-E001 | Error | Line is not valid JSON | -- |
| TV-E002 | Error | `v` field missing or not an integer | TV-E001 on same line |
| TV-E003 | Error | `v` value is not 1 | TV-E002 on same line |
| TV-E004 | Error | `seq` not strictly monotonic from 0 (gap, repeat, or non-integer) | TV-E001 on same line |
| TV-E005 | Error | `runId` differs from first event's `runId` | TV-E001 on same line |
| TV-E006 | Error | `run.start` is not at seq 0, or seq 0 is not type `run.start` | TV-E001 on same line |
| TV-E007 | Error | `run.end` exists but is not the last event | post-loop check |
| TV-E008 | Error | `type` field missing | TV-E001 on same line |
| TV-W001 | Warning | `ts` decreased from previous event | TV-E001 on same line |
| TV-W002 | Warning | No `run.end` event in a non-empty trace | post-loop check |
| TV-W003 | Warning | Empty trace (no non-blank lines / no parseable events) | -- |

---

## Given-When-Then specifications

### T1 -- Happy path: valid 3-event trace

**Given:** Three JSONL lines:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130}}
{"v":1,"seq":1,"ts":100,"type":"decision","runId":"aabbccdd","data":{"stepId":"s1","actionType":"navigate"}}
{"v":1,"seq":2,"ts":200,"type":"run.end","runId":"aabbccdd","data":{"outcome":"done"}}
```
**When:** `Validate(lines)`
**Then:** `result.IsClean == true`, zero errors, zero warnings.

### T2 -- TV-E001: malformed JSON

**Given:** Lines: `[validRunStart, "not json {{{", validRunEnd]`
**When:** `Validate(lines)`
**Then:** Exactly one error with code `TV-E001`, `LineNumber == 2`. Also expect TV-E004 on line 3 (seq 2 but expected 1 due to malformed line not advancing counter -- per TV8/TV10).

### T3 -- TV-E002: missing v field

**Given:** Line: `{"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** Error `TV-E002` on line 1.

### T4 -- TV-E002: v is a string not integer

**Given:** Line: `{"v":"one","seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** Error `TV-E002` on line 1.

### T5 -- TV-E003: v is 2

**Given:** Line: `{"v":2,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** Error `TV-E003` on line 1. Message mentions "expected 1".

### T6 -- TV-E004: seq gap

**Given:** Three lines with seq 0, 1, 3 (gap at 3).
**When:** `Validate(lines)`
**Then:** Exactly one `TV-E004` error on the line with seq 3. Message mentions "expected 2".

### T7 -- TV-E004: seq repeat

**Given:** Three lines with seq 0, 1, 1 (repeat).
**When:** `Validate(lines)`
**Then:** Exactly one `TV-E004` error. The validator resyncs per TV10, so no cascade.

### T8 -- TV-E004: seq starts at 1 instead of 0

**Given:** Single line with seq 1.
**When:** `Validate(lines)`
**Then:** `TV-E004` (expected 0, got 1). Also `TV-E006` (seq 0 is not `run.start`).

### T9 -- TV-E005: inconsistent runId

**Given:** Two lines: first with `runId:"aaaa0000"`, second with `runId:"bbbb1111"`.
**When:** `Validate(lines)`
**Then:** Exactly one `TV-E005` error on line 2.

### T10 -- TV-E006: run.start not at seq 0

**Given:** Two lines: seq 0 with type `decision`, seq 1 with type `run.start`.
**When:** `Validate(lines)`
**Then:** `TV-E006` on line 1 (seq 0 is not `run.start`). Also `TV-E006` on line 2 (`run.start` found at seq != 0).

### T11 -- TV-E006: no run.start at all

**Given:** Two lines: seq 0 with type `decision`, seq 1 with type `run.end`.
**When:** `Validate(lines)`
**Then:** `TV-E006` on line 1 (seq 0 is not `run.start`). Also `TV-W002` is NOT emitted (there IS a `run.end`).

### T12 -- TV-E007: run.end not last event

**Given:** Three lines: seq 0 `run.start`, seq 1 `run.end`, seq 2 `decision`.
**When:** `Validate(lines)`
**Then:** `TV-E007` (run.end at event index 1 but last event is index 2).

### T13 -- TV-E008: missing type field

**Given:** Line: `{"v":1,"seq":0,"ts":0,"runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** `TV-E008` on line 1. Also `TV-E006` (seq 0 is not `run.start` since type is unknown).

### T14 -- TV-W001: non-monotonic ts

**Given:** Three lines with ts values 0, 200, 100.
**When:** `Validate(lines)`
**Then:** Exactly one `TV-W001` warning on line 3. Message mentions "decreased from 200 to 100".

### T15 -- TV-W002: missing run.end

**Given:** Two lines: `run.start` at seq 0, `decision` at seq 1. No `run.end`.
**When:** `Validate(lines)`
**Then:** Exactly one `TV-W002` warning (no line number).

### T16 -- TV-W003: empty trace

**Given:** Empty list `[]`.
**When:** `Validate(lines)`
**Then:** Exactly one `TV-W003` warning. Zero errors.

### T17 -- TV-W003: only blank lines

**Given:** `["", "  ", "\t"]`
**When:** `Validate(lines)`
**Then:** Exactly one `TV-W003` warning. Zero errors.

### T18 -- Blank lines skipped silently

**Given:** Valid run.start (seq 0) on line 1, blank line on line 2, valid decision (seq 1) on line 3.
**When:** `Validate(lines)`
**Then:** No errors from the blank line. `seq` still validates correctly (0 then 1). `TV-W002` warning (no run.end).

### T19 -- Multiple errors on different lines

**Given:** Four lines: valid run.start, malformed JSON, line with wrong runId, valid decision with correct seq.
**When:** `Validate(lines)`
**Then:** `TV-E001` on malformed line, `TV-E005` on wrong-runId line. Both reported (validator doesn't bail on first error).

### T20 -- TV-E004 resync prevents cascade

**Given:** Lines with seq 0, 2, 3, 4 (gap at line 2, then correct progression).
**When:** `Validate(lines)`
**Then:** Exactly one `TV-E004` on the line with seq 2. Lines with seq 3 and 4 produce no seq errors (resync per TV10).

### T21 -- run.end as only event is valid for run.end-is-last

**Given:** One line: `run.start` at seq 0 followed by one line `run.end` at seq 1.
**When:** `Validate(lines)`
**Then:** Zero errors. `run.end` is the last event (TV-E007 not triggered).

### T22 -- TV-E004: seq is not a number

**Given:** Line: `{"v":1,"seq":"zero","ts":0,"type":"run.start","runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** `TV-E004` on line 1 (seq is not an integer).

### T23 -- TV-E004: seq field missing

**Given:** Line: `{"v":1,"ts":0,"type":"run.start","runId":"aabbccdd","data":{}}`
**When:** `Validate(lines)`
**Then:** `TV-E004` on line 1 (seq missing).

### T24 -- CLI exit code 0 for clean trace

**Given:** A temp file containing the valid 3-event trace from T1.
**When:** `qf-trace validate <tempfile>`
**Then:** Exit code 0. stdout contains "0 error(s), 0 warning(s). Validation passed."

### T25 -- CLI exit code 1 for errors

**Given:** A temp file containing a trace with malformed JSON.
**When:** `qf-trace validate <tempfile>`
**Then:** Exit code 1. stdout contains "Validation failed."

### T26 -- CLI exit code 2 for warnings with --fail-on-warning

**Given:** A temp file containing a valid trace missing `run.end`.
**When:** `qf-trace validate <tempfile> --fail-on-warning`
**Then:** Exit code 2. stdout contains "Validation passed." (warnings are non-fatal without --fail-on-warning, but exit code is 2 when flag is set).

### T27 -- CLI missing positional argument

**Given:** `qf-trace validate` (no file argument).
**When:** Run CLI.
**Then:** Exit code 1. stderr contains "requires <trace.jsonl>".

---

## Implementation order

### Phase A -- Types (est. 15 min)
1. Create `QuestForge.Tools.Trace/Validation/TraceValidationResult.cs` (Task 1)
2. Create `QuestForge.Tools.Trace/Validation/TraceValidatorTests.cs` test file skeleton with all T1--T23 tests as `[Fact]` stubs

Done-before-next: types compile, tests compile (all red).

### Phase B -- Validator core (est. 1--2 hours)
1. Create `QuestForge.Tools.Trace/Validation/TraceValidator.cs` (Task 2)
2. Implement single-pass validation loop
3. Implement post-loop checks
4. All T1--T23 green

Done-before-next: `dotnet test` passes all 23+ unit tests.

### Phase C -- CLI wiring (est. 30 min)
1. Add `ValidateTrace` to `CliSubcommand`
2. Add `"validate"` case to `CliArgsParser`
3. Add `FormatTraceIssues` to `OutputFormatters`
4. Add `RunValidateTrace` to `Program.cs` + dispatch arm
5. Update `PrintHelp`
6. Write CLI integration tests T24--T27

Done-before-next: `dotnet test` passes all tests including CLI tests.

---

## Done criteria

1. `qf-trace validate good-trace.jsonl` exits 0 and prints "0 error(s), 0 warning(s). Validation passed."
2. `qf-trace validate malformed-trace.jsonl` exits 1 and prints error lines with `[TV-E001]` codes and line numbers
3. `qf-trace validate aborted-trace.jsonl` exits 0 (warnings only) and prints `[TV-W002]`; exits 2 with `--fail-on-warning`
4. `qf-trace validate` (no file) exits 1 with usage message on stderr
5. All unit tests in `TraceValidatorTests` green
6. All CLI integration tests green
7. `dotnet test QuestForge.Tools.Trace.Tests` exits 0

---

## Exclusions

- **`engineConfig` deserialization check** -- deferred; requires `EngineDecisionConfig` schema to be stable
- **Engine-seed integrity check** -- deferred; requires running the engine against observations
- **Last-line newline check** -- deferred; minor, and `File.ReadAllLines` strips trailing newlines anyway
- **`rejected` alternatives check** -- deferred; requires access to current engine decision code
- **JSON output format for `--format json`** -- not in this PR; text output only. JSON format can be added later if needed for CI integration
- **Multi-part trace support** -- not in this PR; `part.start` events are not validated
- **Per-event `data` payload validation** -- only top-level fields (`v`, `seq`, `ts`, `type`, `runId`) are checked; `data` contents are opaque to this validator
