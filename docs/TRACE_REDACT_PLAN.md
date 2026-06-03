# Trace Redact Plan: `qf-trace redact <input> [<output>]`

**Status:** ready to implement
**Input docs:** TRACE_FORMAT.md SS7, issue #3 in questforge-tools
**Output:** `qf-trace redact <input> [<output>]` strips `wallClockUtc`, scans for excluded-field keys, writes redacted trace + report to stderr
**Phase dependencies:** None; uses existing `CliArgsParser`, `CliArgs`, `OutputFormatters` infrastructure

---

## Dependency graph

Single repo (`questforge-tools`), no cross-repo dependencies:

```
QuestForge.Tools.Trace/Redaction/TraceRedactor.cs          <-- new
QuestForge.Tools.Trace/Redaction/RedactionReport.cs        <-- new
QuestForge.Tools.Trace/Cli/CliSubcommand.cs                <-- add Redact
QuestForge.Tools.Trace/Cli/CliArgsParser.cs                <-- add "redact" case + second positional
QuestForge.Tools.Trace/Cli/OutputFormatters.cs             <-- add FormatRedactionReport
qf-trace/Program.cs                                       <-- add RunRedact + dispatch + help
QuestForge.Tools.Trace.Tests/Redaction/TraceRedactorTests.cs <-- new
```

Build order: result types first, redactor core, CLI wiring, tests.

---

## Architectural decisions

### TR1 -- Raw-line processing with JsonNode, not TraceEventParser

`TraceRedactor` processes raw `string[]` lines (or streams), NOT pre-parsed `TraceEvent` objects. Reasons:

1. **Byte-for-byte stability** -- `TraceEventParser` deserializes into typed objects and re-serializes, which may reorder properties, change whitespace, or drop unknown fields. Raw-line processing preserves everything except the targeted modification.
2. **wallClockUtc stripping requires surgical modification** -- only `run.start` events need modification; all other lines pass through byte-for-byte unchanged.
3. **Excluded-field scanning needs raw JSON property names** -- not all property names survive typed deserialization (unknown fields are dropped by STJ).

For `run.start` events, use `System.Text.Json.Nodes.JsonNode.Parse` to modify `data.wallClockUtc` to `null`, then re-serialize with `JsonSerializerOptions { WriteIndented = false }`. For all other lines, emit the original string unchanged.

```csharp
public sealed class TraceRedactor
{
    public RedactionReport Redact(IReadOnlyList<string> lines, TextWriter output);
    public RedactionReport RedactFile(string inputPath, TextWriter output);
}
```

The file-path overload calls `File.ReadAllLines` then delegates to the lines overload. This keeps the core logic pure and testable without disk I/O.

**Rejected alternative:** String manipulation (regex/string replace) on `wallClockUtc`. Fragile -- the value could contain escaped characters, and the field ordering is not guaranteed. `JsonNode` is the correct level of abstraction for "modify one field, preserve everything else."

**What breaks if violated:** Using `TraceEventParser` would silently drop unknown event types (future trace versions), change property ordering, and break the byte-for-byte stability contract. Using regex would break on edge cases like `wallClockUtc` appearing in a nested string value.

**Testability:** All tests use the `Redact(IReadOnlyList<string>, TextWriter)` overload. No file system needed.

### TR2 -- JsonNode re-serialization options for stability

When re-serializing a modified `run.start` line, use:

```csharp
private static readonly JsonSerializerOptions ReserializeOptions = new()
{
    WriteIndented = false,
};
```

`JsonNode.ToJsonString(ReserializeOptions)` produces compact JSON with no indentation. This is the same format the recorder emits (JSONL = one compact JSON object per line). Property order is preserved by `JsonNode` (it uses `JsonObject` which maintains insertion order).

**What breaks if violated:** Using `WriteIndented = true` would produce multi-line output, breaking the JSONL format. Using different options across calls would break byte-for-byte stability (same input must always produce same output).

**Testability:** Tests assert exact string equality on redacted output lines.

### TR3 -- Excluded-field scanning is property-name based, recursive

The excluded-field list is a static `HashSet<string>` of JSON property names that should never appear in a well-formed trace:

```csharp
internal static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.Ordinal)
{
    "characterName",
    "worldName",
    "serverId",
    "contentId",
    "accountId",
    "friendList",
    "fcName",
    "partyMembers",
    "retainerName",
    "chatContent",
    "chatMessage",
};
```

For each non-blank line, parse with `JsonDocument` and walk all property names recursively. If any property name matches the excluded set, record it in the report. The line is still emitted to the output unchanged -- the scan is a warning, not a filter. The recording proxy already prevents these fields from appearing; this is a safety-net verification.

```csharp
private static void ScanForExcludedKeys(
    JsonElement element,
    int lineNumber,
    List<ExcludedFieldHit> hits)
```

**Rejected alternative:** Scanning for substring matches in the raw line text. This would produce false positives when excluded words appear inside string values (e.g., a quest step ID containing "chatMessage"). Property-name-level scanning is precise.

**What breaks if violated:** Substring scanning would flag legitimate data. Not scanning recursively would miss nested excluded fields.

**Testability:** Tests construct lines with excluded keys at various nesting depths and verify they appear in the report.

### TR4 -- Idempotency via null check

Before modifying `wallClockUtc`, check if it is already `null`. If so, skip the modification. The output line is the original string, unchanged. This guarantees that redacting an already-redacted trace produces byte-for-byte identical output.

```csharp
// Inside run.start handling:
var dataNode = root["data"];
if (dataNode is JsonObject dataObj && dataObj.ContainsKey("wallClockUtc"))
{
    var wallClock = dataObj["wallClockUtc"];
    if (wallClock is null || wallClock.GetValueKind() == JsonValueKind.Null)
    {
        // Already redacted -- pass through unchanged
        output.WriteLine(line);
    }
    else
    {
        dataObj["wallClockUtc"] = null;
        output.WriteLine(root.ToJsonString(ReserializeOptions));
    }
}
else
{
    // No wallClockUtc field at all -- pass through unchanged
    output.WriteLine(line);
}
```

**What breaks if violated:** Unnecessary re-serialization would change property ordering or whitespace even when no modification is needed, breaking idempotency.

**Testability:** Test that `Redact(Redact(input)) == Redact(input)` byte-for-byte.

### TR5 -- RedactionReport as a simple data object

```csharp
namespace QuestForge.Tools.Trace.Redaction;

public sealed record RedactionReport(
    int TotalLines,
    int WallClockStripped,
    int AlreadyRedacted,
    IReadOnlyList<ExcludedFieldHit> ExcludedFieldHits);

public sealed record ExcludedFieldHit(
    string PropertyName,
    int LineNumber);
```

The report is returned from `Redact()` and formatted to stderr by the CLI. It contains:
- `TotalLines` -- number of non-blank lines processed
- `WallClockStripped` -- number of lines where `wallClockUtc` was set to `null` (should be 0 or 1)
- `AlreadyRedacted` -- number of `run.start` lines where `wallClockUtc` was already `null`
- `ExcludedFieldHits` -- list of (propertyName, lineNumber) pairs for any excluded keys found

**Rejected alternative:** A `TraceValidationResult`-style errors/warnings split. The redaction report is informational, not pass/fail. The CLI decides exit codes based on the report contents.

### TR6 -- CLI subcommand is "redact"

`qf-trace redact <input> [<output>]` where:
- `<input>` is the trace file to redact (required, positional)
- `<output>` is the output file (optional, positional). If omitted, redacted output goes to stdout.
- The redaction report always goes to stderr.

This requires extending `CliArgsParser` to support TWO positional arguments for the `redact` subcommand. The first positional routes to `TracePath`, the second to `OutputPath`.

```csharp
// In CliArgsParser, the positional handling becomes:
if (!positionalSeen)
{
    positionalSeen = true;
    if (subcommand == CliSubcommand.ValidateFixture)
        fixturePath = token;
    else
        tracePath = token;
}
else if (subcommand == CliSubcommand.Redact && outputPath is null)
{
    outputPath = token;
}
else
{
    parseError = $"unexpected positional argument: {token}";
    break;
}
```

The `CliSubcommand` enum gets a new `Redact` variant.

**Rejected alternative:** Using `--out <path>` for the output file. The spec says `qf-trace redact <input> [<output>]` with positional args, and this is more natural for a transform command (like `cp src dst`).

**What breaks if violated:** Using `--out` would diverge from the documented CLI signature in TRACE_FORMAT.md SS7.3.

### TR7 -- Exit codes

| Exit code | Condition |
|-----------|-----------|
| 0 | Redaction succeeded, no excluded fields found |
| 1 | Usage error (missing input, file not found) |
| 2 | Redaction succeeded but excluded fields were found (safety-net warning) |

Exit code 2 signals "the trace was redacted but contains suspicious keys that the recording proxy should have excluded." This lets CI flag traces that may have been hand-crafted or recorded with a buggy proxy.

**Rejected alternative:** Exit code 0 for all successful redactions. This would hide the excluded-field warnings in CI pipelines that only check exit codes.

### TR8 -- Output uses LF line endings, not CRLF

TRACE_FORMAT.md SS2 specifies `\n` line terminators. The redactor must emit `\n` regardless of platform. Use `TextWriter` configured for LF, or write `\n` explicitly.

```csharp
// When writing to a file:
using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false))
{
    NewLine = "\n"
};
```

When writing to stdout, set `Console.Out.NewLine = "\n"` before writing.

**What breaks if violated:** Windows default `\r\n` would produce traces that differ from the recorder output, breaking byte-for-byte comparisons and potentially confusing JSONL parsers that include `\r` in the last field value.

### TR9 -- run.start detection uses type field, not seq

A line is a `run.start` event if and only if its `type` property equals `"run.start"`. Do NOT detect by `seq == 0` alone -- a trace could have a malformed first line or missing type field. The `type` check is explicit and precise.

```csharp
// Inside the processing loop:
using var doc = JsonDocument.Parse(line);
var root = doc.RootElement;
if (root.TryGetProperty("type", out var typeProp) &&
    typeProp.GetString() == "run.start")
{
    // This is a run.start event -- apply wallClockUtc stripping
}
```

**Rejected alternative:** Checking `seq == 0`. This would miss `run.start` events at wrong positions (which are malformed but should still be redacted) and would incorrectly flag non-run.start events at seq 0.

### TR10 -- Non-JSON lines pass through unchanged

Lines that fail `JsonDocument.Parse` are emitted to the output unchanged. They are NOT stripped. Rationale: the redactor's job is to remove PII, not to fix malformed traces. A malformed line cannot contain structured PII fields (it's not valid JSON), and stripping it would change the trace's event count, potentially hiding bugs.

The redaction report does NOT flag malformed lines -- that is the validator's job. The redactor and validator are separate concerns.

**Rejected alternative:** Stripping malformed lines. This would silently alter the trace structure. The user should run `qf-trace validate` separately to check trace integrity.

---

## Task breakdown

### Task 1 -- Result types

**File:** `QuestForge.Tools.Trace/Redaction/RedactionReport.cs`

```csharp
namespace QuestForge.Tools.Trace.Redaction;

public sealed record RedactionReport(
    int TotalLines,
    int WallClockStripped,
    int AlreadyRedacted,
    IReadOnlyList<ExcludedFieldHit> ExcludedFieldHits);

public sealed record ExcludedFieldHit(
    string PropertyName,
    int LineNumber);
```

### Task 2 -- TraceRedactor

**File:** `QuestForge.Tools.Trace/Redaction/TraceRedactor.cs`

```csharp
namespace QuestForge.Tools.Trace.Redaction;

public sealed class TraceRedactor
{
    internal static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.Ordinal)
    {
        "characterName", "worldName", "serverId", "contentId", "accountId",
        "friendList", "fcName", "partyMembers", "retainerName",
        "chatContent", "chatMessage",
    };

    private static readonly JsonSerializerOptions ReserializeOptions = new()
    {
        WriteIndented = false,
    };

    public RedactionReport RedactFile(string inputPath, TextWriter output)
    {
        var lines = File.ReadAllLines(inputPath);
        return Redact(lines, output);
    }

    public RedactionReport Redact(IReadOnlyList<string> lines, TextWriter output)
    {
        int totalLines = 0;
        int wallClockStripped = 0;
        int alreadyRedacted = 0;
        var excludedHits = new List<ExcludedFieldHit>();

        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                output.Write(line);
                output.Write('\n');
                continue;
            }

            totalLines++;
            int lineNumber = index + 1;

            // Try to parse as JSON for scanning and potential modification
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException)
            {
                // Non-JSON line: pass through unchanged (TR10)
                output.Write(line);
                output.Write('\n');
                continue;
            }

            using (doc)
            {
                // Scan for excluded keys (TR3)
                ScanForExcludedKeys(doc.RootElement, lineNumber, excludedHits);

                // Check if this is a run.start event (TR9)
                bool isRunStart = doc.RootElement.TryGetProperty("type", out var typeProp)
                    && typeProp.GetString() == "run.start";

                if (!isRunStart)
                {
                    output.Write(line);
                    output.Write('\n');
                    continue;
                }
            }

            // run.start event: strip wallClockUtc using JsonNode (TR4)
            var node = JsonNode.Parse(line);
            if (node is JsonObject root
                && root["data"] is JsonObject dataObj
                && dataObj.ContainsKey("wallClockUtc"))
            {
                var wallClock = dataObj["wallClockUtc"];
                if (wallClock is null || wallClock.GetValueKind() == JsonValueKind.Null)
                {
                    alreadyRedacted++;
                    output.Write(line);
                    output.Write('\n');
                }
                else
                {
                    wallClockStripped++;
                    dataObj["wallClockUtc"] = null;
                    output.Write(root.ToJsonString(ReserializeOptions));
                    output.Write('\n');
                }
            }
            else
            {
                // run.start without wallClockUtc field -- pass through
                output.Write(line);
                output.Write('\n');
            }
        }

        return new RedactionReport(totalLines, wallClockStripped, alreadyRedacted, excludedHits);
    }

    private static void ScanForExcludedKeys(
        JsonElement element,
        int lineNumber,
        List<ExcludedFieldHit> hits)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (ExcludedPropertyNames.Contains(prop.Name))
                    hits.Add(new ExcludedFieldHit(prop.Name, lineNumber));
                ScanForExcludedKeys(prop.Value, lineNumber, hits);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ScanForExcludedKeys(item, lineNumber, hits);
        }
    }
}
```

### Task 3 -- CLI wiring

**3a. `CliSubcommand.cs`** -- add `Redact` variant:
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
    ValidateTrace,
    Redact,          // <-- new
}
```

**3b. `CliArgsParser.cs`** -- add `"redact"` to the switch and handle second positional:
```csharp
"redact" => CliSubcommand.Redact,
```

In the positional-argument handling, allow a second positional for `Redact`:
```csharp
else if (subcommand == CliSubcommand.Redact && outputPath is null)
{
    outputPath = token;
}
```

**3c. `Program.cs`** -- add dispatch and `RunRedact`:
```csharp
// Dispatch before resolvedRoot (redact doesn't need quest-data):
if (cliArgs.Subcommand == CliSubcommand.Redact)
    return RunRedact(cliArgs);
```

```csharp
private static int RunRedact(CliArgs cliArgs)
{
    if (cliArgs.TracePath is null)
    {
        Console.Error.WriteLine("qf-trace: redact requires <input>");
        return 1;
    }
    if (!File.Exists(cliArgs.TracePath))
    {
        Console.Error.WriteLine($"qf-trace: trace file not found: {cliArgs.TracePath}");
        return 1;
    }

    var redactor = new TraceRedactor();
    RedactionReport report;

    if (cliArgs.OutputPath is not null)
    {
        using var writer = new StreamWriter(cliArgs.OutputPath, append: false,
            new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        report = redactor.RedactFile(cliArgs.TracePath, writer);
    }
    else
    {
        Console.Out.NewLine = "\n";
        report = redactor.RedactFile(cliArgs.TracePath, Console.Out);
    }

    Console.Error.Write(OutputFormatters.FormatRedactionReport(report));
    return report.ExcludedFieldHits.Count > 0 ? 2 : 0;
}
```

**3d. `OutputFormatters.cs`** -- add `FormatRedactionReport`:
```csharp
public static string FormatRedactionReport(RedactionReport report)
{
    var sb = new StringBuilder();
    sb.Append($"Redaction complete: {report.TotalLines} lines processed");
    if (report.WallClockStripped > 0)
        sb.Append($", {report.WallClockStripped} wallClockUtc stripped");
    if (report.AlreadyRedacted > 0)
        sb.Append($", {report.AlreadyRedacted} already redacted");
    sb.Append(".\n");

    if (report.ExcludedFieldHits.Count > 0)
    {
        sb.Append('\n');
        sb.Append("WARNING: excluded fields found (recording proxy should have prevented these):\n");
        foreach (var hit in report.ExcludedFieldHits)
            sb.Append($"  line {hit.LineNumber}: \"{hit.PropertyName}\"\n");
    }

    return sb.ToString();
}
```

**3e. `PrintHelp()`** -- add redact subcommand help text:
```csharp
Console.WriteLine("  redact <input> [<output>]");
Console.WriteLine("    Strip wallClockUtc and verify no excluded PII fields.");
Console.WriteLine("    If <output> is omitted, write to stdout. Report goes to stderr.");
Console.WriteLine();
```

### Task 4 -- Tests

**File:** `QuestForge.Tools.Trace.Tests/Redaction/TraceRedactorTests.cs`

All tests use `TraceRedactor.Redact(IReadOnlyList<string>, TextWriter)` with a `StringWriter` to capture output. See GWT specs below.

---

## Given-When-Then specifications

### T1 -- Happy path: strip wallClockUtc from run.start

**Given:** Two JSONL lines:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130,"wallClockUtc":"2026-05-12T14:22:01.234Z"}}
{"v":1,"seq":1,"ts":100,"type":"run.end","runId":"aabbccdd","data":{"outcome":"done"}}
```
**When:** `Redact(lines, writer)`
**Then:**
- First output line contains `"wallClockUtc":null` (not the original timestamp)
- Second output line is byte-for-byte identical to input
- Report: `TotalLines == 2`, `WallClockStripped == 1`, `AlreadyRedacted == 0`, `ExcludedFieldHits.Count == 0`

### T2 -- Idempotency: already-redacted trace passes through unchanged

**Given:** Two JSONL lines where `wallClockUtc` is already `null`:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130,"wallClockUtc":null}}
{"v":1,"seq":1,"ts":100,"type":"run.end","runId":"aabbccdd","data":{"outcome":"done"}}
```
**When:** `Redact(lines, writer)`
**Then:**
- Output is byte-for-byte identical to input
- Report: `WallClockStripped == 0`, `AlreadyRedacted == 1`

### T3 -- Idempotency: redacting twice produces same output

**Given:** A trace with a non-null `wallClockUtc`.
**When:** `Redact(input)` produces `output1`. `Redact(output1)` produces `output2`.
**Then:** `output2` is byte-for-byte identical to `output1`.

### T4 -- Non-run.start lines pass through unchanged

**Given:** Three JSONL lines: `run.start`, `observation`, `decision`.
**When:** `Redact(lines, writer)`
**Then:** Lines 2 and 3 are byte-for-byte identical to input. Only line 1 is modified (wallClockUtc stripped).

### T5 -- Excluded field detected: characterName in observation data

**Given:** Two lines:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130,"wallClockUtc":null}}
{"v":1,"seq":1,"ts":50,"type":"observation","runId":"aabbccdd","data":{"characterName":"Warrior of Light"}}
```
**When:** `Redact(lines, writer)`
**Then:**
- Both lines are emitted to output (excluded fields are warned, not stripped)
- Report: `ExcludedFieldHits.Count == 1`, hit has `PropertyName == "characterName"`, `LineNumber == 2`

### T6 -- Multiple excluded fields on same line

**Given:** One line with both `characterName` and `worldName` in its data:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130,"wallClockUtc":null,"characterName":"Test","worldName":"Gilgamesh"}}
```
**When:** `Redact(lines, writer)`
**Then:** Report contains two `ExcludedFieldHit` entries, both on line 1.

### T7 -- Nested excluded field detected

**Given:** One line with `accountId` nested inside a `data.observed` object:
```jsonl
{"v":1,"seq":1,"ts":50,"type":"observation","runId":"aabbccdd","data":{"observed":{"accountId":"12345"}}}
```
**When:** `Redact(lines, writer)`
**Then:** Report contains one `ExcludedFieldHit` with `PropertyName == "accountId"`, `LineNumber == 1`.

### T8 -- Excluded field name inside a string value is NOT flagged

**Given:** One line where "characterName" appears as a string VALUE, not a property name:
```jsonl
{"v":1,"seq":1,"ts":50,"type":"observation","runId":"aabbccdd","data":{"method":"characterName"}}
```
**When:** `Redact(lines, writer)`
**Then:** Report: `ExcludedFieldHits.Count == 0`. The string `"characterName"` is a value of the `method` property, not a property name itself.

### T9 -- Malformed JSON line passes through unchanged

**Given:** Three lines: valid `run.start`, malformed JSON `"not json {{{"`, valid `run.end`.
**When:** `Redact(lines, writer)`
**Then:**
- All three lines appear in output
- Malformed line is byte-for-byte identical to input
- Report: `TotalLines == 3` (malformed line counts as a line)

Wait -- per TR10, malformed JSON lines DO pass through. But the `TotalLines` counter increments on non-blank lines. Let me correct: the malformed line is non-blank, so `totalLines` does increment. Actually, re-reading the code sketch -- `totalLines++` happens after the blank check but the `try/catch` for `JsonDocument.Parse` does a `continue` after writing the line. So `totalLines` would need to increment before the parse attempt. Let me revise the code sketch to increment `totalLines` before the parse.

Correction: `TotalLines == 3` is correct -- all three non-blank lines are counted.

### T10 -- Blank lines pass through

**Given:** Lines: `[validRunStart, "", validRunEnd]`
**When:** `Redact(lines, writer)`
**Then:**
- Output has three lines (including the blank one)
- Report: `TotalLines == 2` (blank lines are not counted)

### T11 -- run.start without wallClockUtc field

**Given:** One line: `run.start` event with no `wallClockUtc` in data:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd","data":{"questId":66130}}
```
**When:** `Redact(lines, writer)`
**Then:**
- Output line is byte-for-byte identical to input
- Report: `WallClockStripped == 0`, `AlreadyRedacted == 0`

### T12 -- run.start without data field

**Given:** One line: `run.start` with no `data` property at all:
```jsonl
{"v":1,"seq":0,"ts":0,"type":"run.start","runId":"aabbccdd"}
```
**When:** `Redact(lines, writer)`
**Then:** Output line is byte-for-byte identical to input. No crash.

### T13 -- All eleven excluded property names are detected

**Given:** Eleven separate lines, each containing one of the excluded property names as a JSON key.
**When:** `Redact(lines, writer)`
**Then:** Report contains exactly 11 `ExcludedFieldHit` entries, one per excluded property name: `characterName`, `worldName`, `serverId`, `contentId`, `accountId`, `friendList`, `fcName`, `partyMembers`, `retainerName`, `chatContent`, `chatMessage`.

### T14 -- Empty trace (no lines)

**Given:** Empty list `[]`.
**When:** `Redact(lines, writer)`
**Then:** Output is empty. Report: `TotalLines == 0`, `WallClockStripped == 0`, `ExcludedFieldHits.Count == 0`.

### T15 -- Output uses LF line endings

**Given:** A valid trace with one `run.start` line.
**When:** `Redact(lines, writer)` where `writer` is a `StringWriter`.
**Then:** The output string contains `\n` but not `\r\n`. (On Windows, `StringWriter` defaults to `\r\n`; the redactor must use `writer.Write(line); writer.Write('\n')` not `writer.WriteLine(line)`.)

### T16 -- CLI: redact with output file

**Given:** A temp input file with a valid trace, and an output path.
**When:** `qf-trace redact <input> <output>`
**Then:** Exit code 0. Output file contains the redacted trace. Stderr contains the redaction report.

### T17 -- CLI: redact to stdout

**Given:** A temp input file with a valid trace.
**When:** `qf-trace redact <input>` (no output path)
**Then:** Exit code 0. Stdout contains the redacted trace. Stderr contains the redaction report.

### T18 -- CLI: missing input argument

**Given:** `qf-trace redact` (no arguments after "redact").
**When:** Run CLI.
**Then:** Exit code 1. Stderr contains "requires <input>".

### T19 -- CLI: input file not found

**Given:** `qf-trace redact nonexistent.jsonl`
**When:** Run CLI.
**Then:** Exit code 1. Stderr contains "trace file not found".

### T20 -- CLI: exit code 2 when excluded fields found

**Given:** A temp input file containing a line with `characterName` as a property.
**When:** `qf-trace redact <input> <output>`
**Then:** Exit code 2. Output file is written. Stderr report contains "WARNING: excluded fields found".

### T21 -- Excluded field inside array element

**Given:** One line with an array containing objects with excluded fields:
```jsonl
{"v":1,"seq":1,"ts":50,"type":"observation","runId":"aabbccdd","data":{"items":[{"retainerName":"MyRetainer"}]}}
```
**When:** `Redact(lines, writer)`
**Then:** Report contains one `ExcludedFieldHit` with `PropertyName == "retainerName"`.

### T22 -- CLI args: "redact" is parsed as Redact subcommand

**Given:** `args = ["redact", "input.jsonl", "output.jsonl"]`
**When:** `CliArgsParser.Parse(args)`
**Then:** `Subcommand == Redact`, `TracePath == "input.jsonl"`, `OutputPath == "output.jsonl"`, `ParseError == null`.

### T23 -- CLI args: redact with only input (no output)

**Given:** `args = ["redact", "input.jsonl"]`
**When:** `CliArgsParser.Parse(args)`
**Then:** `Subcommand == Redact`, `TracePath == "input.jsonl"`, `OutputPath == null`.

### T24 -- CLI args: redact with three positionals is an error

**Given:** `args = ["redact", "a.jsonl", "b.jsonl", "c.jsonl"]`
**When:** `CliArgsParser.Parse(args)`
**Then:** `ParseError != null`, mentions "unexpected positional argument".

---

## Implementation order

### Phase A -- Types (est. 15 min)

1. Create `QuestForge.Tools.Trace/Redaction/RedactionReport.cs` (Task 1)
2. Add `Redact` to `CliSubcommand` enum
3. Create `QuestForge.Tools.Trace.Tests/Redaction/TraceRedactorTests.cs` test file skeleton with all T1--T24 tests as `[Fact]` stubs

Done-before-next: types compile, tests compile (all red).

### Phase B -- Redactor core (est. 1--2 hours)

1. Create `QuestForge.Tools.Trace/Redaction/TraceRedactor.cs` (Task 2)
2. Implement line-by-line processing loop
3. Implement `wallClockUtc` stripping with `JsonNode`
4. Implement excluded-field scanning with `JsonDocument`
5. All T1--T15, T21 green

Done-before-next: `dotnet test` passes all redactor unit tests.

### Phase C -- CLI wiring (est. 30 min)

1. Add `"redact"` case to `CliArgsParser` with second-positional support
2. Add `FormatRedactionReport` to `OutputFormatters`
3. Add `RunRedact` to `Program.cs` + dispatch arm
4. Update `PrintHelp`
5. Write CLI integration tests T16--T20, T22--T24

Done-before-next: `dotnet test` passes all tests including CLI tests.

---

## Done criteria

1. `qf-trace redact trace.jsonl redacted.jsonl` exits 0 and stderr shows "Redaction complete: N lines processed, 1 wallClockUtc stripped."
2. `qf-trace redact redacted.jsonl redacted2.jsonl` exits 0 and `redacted2.jsonl` is byte-for-byte identical to `redacted.jsonl` (idempotency)
3. `qf-trace redact bad-trace.jsonl` (trace with `characterName` key) exits 2 and stderr shows "WARNING: excluded fields found"
4. `qf-trace redact` (no args) exits 1 with usage message on stderr
5. `qf-trace redact missing.jsonl` exits 1 with "trace file not found" on stderr
6. `qf-trace redact trace.jsonl` (no output) writes redacted trace to stdout, report to stderr
7. All unit tests in `TraceRedactorTests` green
8. All CLI args tests green
9. `dotnet test QuestForge.Tools.Trace.Tests` exits 0

---

## Exclusions

- **Stripping excluded fields** -- the redactor warns about excluded fields but does NOT remove them. Removal would change the trace structure and potentially break replay. The recording proxy is the enforcement point; the redactor is a safety-net verifier.
- **In-process redaction for bug report export** -- TRACE_FORMAT.md SS7.4 describes an in-game "Export bug report" button that runs redaction in-process. That is a plugin-side feature (Phase 8+), not a CLI feature.
- **Trace rotation / multi-part support** -- `part.start` events are not handled specially.
- **Custom excluded-field lists** -- the list is hardcoded. A `--exclude-fields` flag could be added later if needed.
- **JSON output format for the report** -- text output only on stderr. A `--format json` flag for the report could be added later.
- **Validation of trace structure** -- the redactor does not validate `v`, `seq`, `runId`, or `type` fields. Use `qf-trace validate` for that.
