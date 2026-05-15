# questforge-tools

Schema validator and CLI tools for [QuestForge](https://github.com/simplekantan/questforge), a Dalamud plugin for automating FFXIV quest completion.

**License:** MIT

---

## Projects

| Project | Purpose |
|---------|---------|
| `QuestForge.Schema` | C# types for the quest schema — step types, predicates, fragments. Source-generated `System.Text.Json` serialization. |
| `QuestForge.Tools.Validator` | Structural validator: 20+ rules covering required fields, step IDs, recovery gotos, branch nesting, fragment references, step-type constraints, and more. |
| `QuestForge.Tools.Validator.Tests` | xUnit test suite (136 tests). Runs without a game instance. |
| `qf-validate` | CLI entry point. Discovers `quests/**/*.json` and `fragments/**/*.json`, validates each file, and reports errors in text or JSON format. |

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