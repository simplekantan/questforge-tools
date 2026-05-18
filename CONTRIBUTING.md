# Contributing to questforge-tools

## Setup

**Prerequisites:** .NET 10 SDK

Clone `questforge-tools` and `questforge` as siblings — the tools reference `QuestForge.Schema` from the plugin repo:

```
parent/
  questforge/
  questforge-tools/
```

```bash
git clone https://github.com/simplekantan/questforge
git clone https://github.com/simplekantan/questforge-tools
```

---

## Building

```bash
dotnet build questforge-tools.slnx
```

---

## Running tests

```bash
dotnet test QuestForge.Tools.Validator.Tests/QuestForge.Tools.Validator.Tests.csproj
dotnet test QuestForge.Tools.Trace.Tests/QuestForge.Tools.Trace.Tests.csproj
```

Both test suites run without a game instance or Dalamud installation. 148 validator tests, 107 trace tests.

---

## Adding a validation rule

1. Add the rule implementation in `QuestForge.Tools.Validator`
2. Add a test (or tests) in `QuestForge.Tools.Validator.Tests` covering the error and non-error cases
3. Add the new error code to the error code table in the validator's documentation

Follow the existing rule pattern: rules are self-contained, return typed `ValidationResult` values, and are independent of one another.

---

## Adding a qf-trace subcommand

1. Add a class implementing the subcommand logic in `QuestForge.Tools.Trace`
2. Add tests in `QuestForge.Tools.Trace.Tests`
3. Wire CLI dispatch in `qf-trace/Program.cs`

---

## Adding a new step type to the extractor

When `TraceToQuestExtractor` needs to produce a new step type from trace events:

1. **If the step requires new state tracking** — add the relevant field(s) to `SnapshotState` and update the snapshot logic to populate them from preceding events.
2. **If the step is triggered by a new event type** — add recognition of that event in `TraceEventParser` so the parser emits a typed record for it.
3. **Add capability inference** — add a `step:<type>` entry to `CapabilityInferrer.StepCapabilities` so `extract-fixture` reports coverage for the new step type.
4. Add tests in `QuestForge.Tools.Trace.Tests` covering the extraction and (if applicable) capability inference for the new step type.

---

## PR process

- All tests must pass before requesting review
- No Dalamud references in any library project — only the plugin repo (`questforge`) may depend on Dalamud
- Keep commits clean
