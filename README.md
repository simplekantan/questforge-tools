# questforge-tools

Schema validator and CLI tools for [QuestForge](https://github.com/simplekantan/questforge), a Dalamud plugin for automating FFXIV quest completion.

[![CI](https://github.com/simplekantan/questforge-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/simplekantan/questforge-tools/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-blueviolet)](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Projects

| Project | Purpose |
|---------|---------|
| `QuestForge.Schema` | C# types for the quest schema — step types, predicates, fragments. Source-generated `System.Text.Json` serialization. |
| `QuestForge.Tools.Validator` | Structural validator: 20+ rules covering required fields, step IDs, recovery gotos, branch nesting, fragment references, step-type constraints, and more. |
| `QuestForge.Tools.Validator.Tests` | xUnit test suite (148 tests). Runs without a game instance. |
| `qf-validate` | CLI entry point. Discovers `quests/**/*.json` and `fragments/**/*.json`, validates each file, and reports errors in text or JSON format. |
| `QuestForge.Tools.Trace` | Trace reader + fixture/quest extractor library. Reads `.jsonl` trace files. |
| `QuestForge.Tools.Trace.Tests` | xUnit test suite (107 tests). |
| `qf-trace` | CLI entry point for trace extraction. |

---

## Usage

```
dotnet run --project qf-validate -- [rootDir] [--format text|json] [--fail-on-warning]
```

`rootDir` defaults to the current directory. Discovers all quest and fragment JSON files under `quests/` and `fragments/` subdirectories.

**Exit codes:** `0` = clean, `1` = errors present, `2` = warnings only with `--fail-on-warning`

**Text output:**
```
ERROR  quests/arr/msq/65657-close-to-home.json  seq:0
  [structural/step-id-duplicate] Step ID "talk-to-baderon" is not unique

1 error(s), 0 warning(s). Validation failed.
```

**JSON output** (`--format json`):
```json
{
  "results": [
    {
      "code": "structural/step-id-duplicate",
      "message": "Step ID \"talk-to-baderon\" is not unique",
      "file": "quests/arr/msq/65657-close-to-home.json",
      "location": "seq:0",
      "stepId": "talk-to-baderon",
      "severity": "error"
    }
  ],
  "summary": { "errors": 1, "warnings": 0 }
}
```

---

## qf-trace

Four subcommands for working with engine run traces.

### extract-fixture

Reads a `.jsonl` trace file and produces a fixture JSON draft:

```
qf-trace extract-fixture <trace.jsonl> [--quest-data <dir>] [--out <file>]
```

Capability inference covers all 22 schema step types, including `step:attune` and `step:hand-over-item`.

### validate-fixture

Cross-validates a committed fixture against its referenced quest file:

```
qf-trace validate-fixture <fixture.json> [--quest-data <dir>] [--fail-on-warning]
```

### list-fixtures

Lists all fixtures in `questforge-data/fixtures/engine/` with capability coverage:

```
qf-trace list-fixtures [--quest-data <dir>]
```

### extract-quest

Reads a `.jsonl` trace and produces a `QuestDefinition` draft:

```
qf-trace extract-quest <trace.jsonl> [--quest-data <dir>] [--out <file>]
```

Output includes a TODO list of fields that require manual completion (name, expansion, prerequisites).

Extracted step types: `AcceptStep`, `TalkStep`/`TurnInStep` (interact), `TravelStep` (navigate + zone changes), `AttunementStep` (aetheryte attunement), `HandOverItemStep` (key item hand-over), `TravelStep` with aethernet route hint (`UseAethernet` action).

---

## Building and testing

```bash
dotnet build
dotnet test QuestForge.Tools.Validator.Tests
```

Requires .NET 10.

---

## Used by

- [questforge-data](https://github.com/simplekantan/questforge-data) — runs `qf-validate` on every PR via GitHub Actions
- [questforge](https://github.com/simplekantan/questforge) — plugin source; `QuestForge.Schema` types are shared