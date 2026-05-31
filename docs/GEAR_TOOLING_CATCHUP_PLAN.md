# Gear Steps Tooling Catch-up Plan

**Status:** spec complete, ready for test creation
**Scope:** TraceConstants, FilenameLookup, DistinguishingCapPriority, FIXTURES.md for EquipGearForQuestStep, EquipBestGearStep, ChangeJobStep
**Pattern:** mirrors use-item / use-emote / say-chat-message catch-up (PR #104, #112)

---

## 1. TraceConstants.cs -- 3 new entries

Add after `ActionEngage`:

```csharp
internal const string ActionEquipGear     = "equipgear";      // lowercased from "EquipGear"
internal const string ActionEquipBestGear = "equipbestgear";  // lowercased from "EquipBestGear"
internal const string ActionChangeJob     = "changejob";      // lowercased from "ChangeJob"
```

No behavioral change -- `IsTerminalAction` is unaffected. These are documentation constants consumed by future fixture validation.

## 2. FilenameLookup -- 3 new entries

Add after the `with-say-chat-message` entry (before `with-teleport`):

```csharp
(["step:change-job", "step:talk", "step:travel"], "with-change-job.json"),
(["step:equip-best-gear", "step:talk", "step:travel"], "with-equip-best-gear.json"),
(["step:equip-gear-for-quest", "step:talk", "step:travel"], "with-equip-gear-for-quest.json"),
```

Note: arrays are sorted lexicographically (`change-job` < `equip-best-gear` < `equip-gear-for-quest` < `talk` < `travel`).

## 3. DistinguishingCapPriority -- 3 new entries

Place after `step:say-chat-message`, before `step:teleport`. Gear steps are less shape-defining than chat/action/emote but more so than teleport/purchase:

```csharp
("step:equip-gear-for-quest", "with-equip-gear-for-quest.json"),
("step:equip-best-gear",      "with-equip-best-gear.json"),
("step:change-job",            "with-change-job.json"),
```

Ordering rationale: equip-gear-for-quest is the most specific (references a particular item), equip-best-gear is a generic action, change-job is the least distinguishing of the three.

## 4. FIXTURES.md (questforge repo) -- actionType table

Add 3 rows to the `actionType canonical strings` table after `"engage"`:

| Canonical string | C# type | Notes |
|---|---|---|
| `"equipgear"` | `EngineAction.EquipGear` | `EquipGearForQuestStep` dispatch -- equip a specific quest-required item. |
| `"equipbestgear"` | `EngineAction.EquipBestGear` | `EquipBestGearStep` dispatch -- equip recommended gear via Stylist/RecommendEquip. |
| `"changejob"` | `EngineAction.ChangeJob` | `ChangeJobStep` dispatch -- switch active job/class. |

Also add 3 rows to the fixture naming convention list:

```
with-equip-gear-for-quest.json  # quest with EquipGearForQuestStep
with-equip-best-gear.json       # quest with EquipBestGearStep
with-change-job.json            # quest with ChangeJobStep
```

Update `with-gear-requirement.json` line to note it is now superseded by the three specific filenames above.

## 5. Files touched

### questforge-tools repo (branch: feat/gear-steps-tooling-catchup)
- `QuestForge.Tools.Trace/TraceConstants.cs` -- 3 const additions
- `QuestForge.Tools.Trace/Fixture/TraceToFixtureExtractor.cs` -- 3 FilenameLookup + 3 DistinguishingCapPriority entries
- `QuestForge.Tools.Trace.Tests/TraceToFixtureExtractorTests.cs` -- 3 new test methods

### questforge repo (branch: feat/gear-steps-fixtures-doc)
- `docs/FIXTURES.md` -- actionType table + naming convention updates

## 6. Test scenarios (GWT)

### T1: SuggestFilename_WithEquipGearForQuest_ReturnsWithEquipGearForQuest

**Given** a FixtureModel with Capabilities `["step:equip-gear-for-quest", "step:talk", "step:travel"]`
**When** `SuggestFilename` is called
**Then** returns `"with-equip-gear-for-quest.json"`

### T2: SuggestFilename_WithEquipBestGear_ReturnsWithEquipBestGear

**Given** a FixtureModel with Capabilities `["step:equip-best-gear", "step:talk", "step:travel"]`
**When** `SuggestFilename` is called
**Then** returns `"with-equip-best-gear.json"`

### T3: SuggestFilename_WithChangeJob_ReturnsWithChangeJob

**Given** a FixtureModel with Capabilities `["step:change-job", "step:talk", "step:travel"]`
**When** `SuggestFilename` is called
**Then** returns `"with-change-job.json"`

### T4: SuggestFilename_MultiShape_WithEquipGearForQuest_FallsBackToDistinguishing

**Given** a FixtureModel with Capabilities `["step:change-job", "step:equip-gear-for-quest", "step:talk", "step:travel"]` (no exact match)
**When** `SuggestFilename` is called
**Then** returns `"with-equip-gear-for-quest.json"` (higher priority in DistinguishingCapPriority)

---

READY FOR TEST CREATION

Tester: Write failing tests from the GWT specs in section 6.
- Happy paths: 3 scenarios (T1-T3)
- Edge cases: 1 scenario (T4, multi-shape fallback)
- Error cases: 0 (no new error paths)
- Expected total: ~4 tests in QuestForge.Tools.Trace.Tests
