# PR-B — Offline combat correlation: mirror the target-based attribution model

**Status:** READY FOR TEST CREATION (RED phase)
**Role of this doc:** Architect spec. The Tester converts the GWT scenarios in §6 into failing tests; the Builder then reworks `SnapshotState.cs` to GREEN. **No production or test code is written by the Architect.**

**Input docs / sources of truth (read before touching code):**
- `questforge/QuestForge.Engine/Authoring/SnapshotAggregator.cs` — the canonical LIVE model to mirror (target-based span attribution).
- `questforge/QuestForge.Engine/Authoring/StepInferenceEngine.cs` Rule 2.2 — the shared consumer of `KillCorrelatedTargets` (already on the branch; produces expect / step-id).
- `questforge/QuestForge.Engine.Tests/Authoring/CombatCorrelationAggregatorTests.cs` — GwtT1/T2'/T2b/T3'/T4/T5/T6/T7'/T8/T9/T10' — the live GWT corpus this PR mirrors offline.
- `questforge/docs/COMBAT_TARGET_ATTRIBUTION_PLAN.md` — the PR-A plan that reworked the live model.

**Output (what changes):** `qf-trace extract-quest` replaying a recorded authoring trace reconstructs the SAME `CombatStep` (Expect + Id + KillEnemyDataIds + Spawn + Location) the live author tool now produces. The offline mirror (`SnapshotState`) moves from the old kill→variable-bump time-window model to **target-based attribution**.

**File reworked:** `questforge-tools/QuestForge.Tools.Trace/SnapshotState.cs`.
**File UNCHANGED:** `questforge-tools/QuestForge.Tools.Trace/Quest/TraceToQuestExtractor.cs` — already routes `inference.StepType == "combat"` through the shared `StepInferenceEngine.Infer` + `StepFactory.Build`. If `SnapshotState` builds `KillCorrelatedTargets` correctly, the expect/step-id/zone come out identical for free.

---

## 1. Dependency graph and ordering note

```
questforge (branch feat/combat-dalamud-probe, commit 1f1c53e)
  QuestForge.Engine/Authoring/SnapshotAggregator.cs   ← target-based (PR-A, DONE)
  QuestForge.Engine/Authoring/StepInferenceEngine.cs  ← Rule 2.2 nibble/target (PR-A, DONE)
  QuestForge.Engine/Authoring/NibbleKey.cs            ← NibbleKey(int VarIndex, NibbleHalf Half)
  QuestForge.Engine/Authoring/GameStateSnapshot.cs    ← KillCorrelation(IReadOnlyList<uint> DataIds, int FinalValue)
        │  (ProjectReference)
        ▼
questforge-tools
  QuestForge.Tools.Trace/SnapshotState.cs             ← THIS PR (offline mirror)
  QuestForge.Tools.Trace/Quest/TraceToQuestExtractor.cs (UNCHANGED)
```

**HARD ORDERING DEPENDENCY (state this up front):** `QuestForge.Tools.Trace` ProjectReferences the LOCAL `questforge` engine. That engine must be checked out on a commit where the PR-A rework has landed (`feat/combat-dalamud-probe` @ `1f1c53e` or later / merged to main).

**This matters immediately, not just at runtime:** `SnapshotAggregator.CombatCorrelationWindow` **no longer exists** in the engine on this branch (confirmed: a `public static` grep of `QuestForge.Engine` shows it gone, and the PR-A plan lists it among the removed members). `SnapshotState.cs` references it in three places (lines 124, 416, 431). **The tools solution does not currently compile against the branch engine.** The first Builder task is therefore not optional cleanup — it is a compile blocker. Until the window references are removed, neither the existing ~152 tests nor the new tests can run.

**Build / test commands (net10 SDK, no global.json):**
```
$env:PATH = "C:\Users\publi\.dotnet;$env:PATH"
& "C:\Users\publi\.dotnet\dotnet.exe" build  QuestForge.Tools.Trace/QuestForge.Tools.Trace.csproj
& "C:\Users\publi\.dotnet\dotnet.exe" test  QuestForge.Tools.Trace.Tests/QuestForge.Tools.Trace.Tests.csproj
```

---

## 2. Architectural decisions

### D1 — `GetTarget` case: scope the `true` return to the hostile-object shape only

The combat target signal arrives as `GetTarget`:
- Combat (BattleNpc): `{"method":"GetTarget","value":{"baseId":347,"kind":"hostile"}}` — object with `kind=="hostile"` and a `baseId`.
- Non-combat (NPC): `{"method":"GetTarget","value":1000927}` — a plain JSON number.

`SnapshotState.Apply` currently has **no** `GetTarget` case → it hits `default: return false` (unrecognised) for **both** shapes today.

**Decision: add a `GetTarget` case. Return `true` (recognised) ONLY for the hostile-object shape; for the plain-number / other-kind / non-object shape, fall through to `return false` (still unrecognised), matching today's behaviour.**

- **Alternatives rejected:**
  - *Return `true` for all `GetTarget` shapes.* Rejected. Flipping plain-number `GetTarget` from unrecognised→recognised is a behaviour change with unknown blast radius. The recognised/unrecognised bit is observable — `Apply` is documented to "Return `false` only when the method name is not recognised," and the unrecognised-method count is a property tests and the fixture validator can assert on. Plain-number `GetTarget` events appear in real traces (NPC targeting). Recognising them now could silently change unrecognised-method metrics or a "no unknown methods" assertion. There is no offline behaviour that *needs* plain-number `GetTarget` (NPC interaction is sourced from the `RecordInteract` action boundary, not from `GetTarget`), so recognising it buys nothing.
  - *Treat plain-number `GetTarget` as an NPC-interaction signal.* Rejected and explicitly out of scope. `LastNpcInteracted` is set only via `RecordInteract` (the action boundary). Preserve that. The hostile object must NOT touch `LastNpcInteracted`.
- **What breaks if violated:** if a future hostile target value omits `kind` or `baseId`, the object branch must NOT crash — guard every `TryGetProperty`/`GetUInt32` and on a malformed object, return `false` (unrecognised, no mutation). A `baseId == 0` hostile object is recognised (`true`) but performs no combat mutation (mirrors the `dataId == 0` no-op guards elsewhere in `Apply`).
- **Testability:** D1 is pinned by GWT-O-T-A (hostile object → mutation) and GWT-O-T-B (plain number → no mutation, `false`).

Mirror semantics inside the hostile branch (exactly like `OnBattleNpcTargeted`):
```
_lastBattleNpcTarget = baseId;          // unconditional
if (_inCombat) _spanBattleNpcTargets.Add(baseId);
```

### D2 — `EnemyKilled` stays a recognised no-op (keep the case, strip the correlation body)

Real traces still EMIT `EnemyKilled`. Removing the `case "EnemyKilled"` entirely would route those events to `default: return false`, flipping them recognised→unrecognised — the same class of regression D1 avoids.

**Decision: keep `case "EnemyKilled"` as a recognised no-op.** Parse-and-discard, or simply `return true` immediately. It performs NO span mutation, NO buffering, NO correlation. The `_recentKills` buffer and all kill-arrival correlation are removed.

- **Alternative rejected:** delete the case. Rejected — flips recognised→unrecognised for events present in every real combat trace.
- **Testability:** pinned by GWT-O-T-NOOP (an `EnemyKilled` in a span with NO target produces no attribution, and `Apply` still returns `true`).

### D3 — Span-scoped nibble bumps, gated on `_inCombat` (drop the time window entirely)

Replace `_recentNibbleBumps` (`List<(NibbleKey,int,DateTimeOffset)>`) with `_spanNibbleBumps` (`Dictionary<NibbleKey,int>` → key → highest value reached). Mirror `OnQuestVariablesUpdated`:
- First-ever `GetQuestVariables` (no `_prevQuestVariables`) sets baseline only — no bump, no attribution. (Unchanged from today.)
- A nibble increment (`(n&0x0F) > (p&0x0F)` low; `(n>>4) > (p>>4)` high) records `_spanNibbleBumps[key] = newNibbleValue` **only while `_inCombat`** (the gate matches the aggregator's `if (lowUp && _inCombat)` / `if (highUp && _inCombat)`). Bumps that land outside a combat span are dropped.
- `ev.At` timestamps are no longer used for correlation. (`At` is still used elsewhere for ordering; the combat path ignores it.)

- **What breaks if violated:** if the `_inCombat` gate is dropped, a post-combat bump (e.g. a turn-in flag flip while no target is in the span) would attribute spuriously once a target seeds. The aggregator gates; the mirror must gate identically.
- **Testability:** GWT-O-T1 (gated bump attributes), GWT-O-T3' (no-target span → null), GWT-O-T-BASELINE (first obs no bump).

### D4 — `InCombat` false→true seeds the span from `_lastBattleNpcTarget`

Rework the `inCombat && !_inCombat` branch to mirror `OnInCombatChanged`:
```
_combatStartPosition = Position;        // already done
_combatStartZone     = (int)Zone.Value; // already done
_spanBattleNpcTargets.Clear();
_spanNibbleBumps.Clear();
if (_lastBattleNpcTarget is { } t) _spanBattleNpcTargets.Add(t);
```
This is THE bug fix: events replay in timestamp order, and a real trace forwards the pre-combat `GetTarget{hostile}` ONCE (observer dedup) BEFORE `InCombat{true}`. Without seeding, that single pre-combat target is dropped and the span has no target → no attribution. The same ordering forced the live seeding fix; the offline path needs the identical seed.

- **Testability:** GWT-O-T2' (pre-combat target seeded), GWT-O-T2b (seed + in-combat swap), GWT-O-T10' (new span re-seeds from current `_lastBattleNpcTarget`, not stale).

### D5 — `BuildKillCorrelatedTargets`: every bumped nibble gets the span target-set

Mirror the aggregator's helper exactly:
```
if (_spanNibbleBumps.Count == 0 || _spanBattleNpcTargets.Count == 0) return null;
var dataIds = _spanBattleNpcTargets.OrderBy(id => id).ToList();
foreach (var (key, value) in _spanNibbleBumps)
    result[key] = new KillCorrelation(dataIds, value);
return result.Count > 0 ? result : null;
```
Replace the existing field `_killCorrelatedTargets` (`Dictionary<NibbleKey,(HashSet<uint>,int)>`) and the per-key accumulation. The span target-set is shared across all nibbles (this is the raw data that lets Rule 2.2 detect multi-target/multi-nibble ambiguity).

### D6 — `ResetPendingKeyItemDeltas`: clear span sets, PRESERVE baseline + last target

Mirror `ResetDeltas`:
- Clear `_spanBattleNpcTargets`, `_spanNibbleBumps` (and existing `_pendingKeyItemsAdded`/`Removed`).
- **PRESERVE** `_prevQuestVariables` (already preserved) **AND** `_lastBattleNpcTarget`.
- Remove `_recentKills.Clear()`, `_recentNibbleBumps.Clear()`, `_killCorrelatedTargets.Clear()`.

Rationale (from live GWT-T7'): `_lastBattleNpcTarget` is "the mob the player is currently targeting" — a persistent observation, not a per-window delta. The extractor calls `ResetPendingKeyItemDeltas` after each decision; if it cleared `_lastBattleNpcTarget`, a pre-combat target observed in one window would be lost before the `InCombat{true}` in the next window seeded it.

- **Testability:** GWT-O-T7' (reset clears span, `_lastBattleNpcTarget` + `_prevQuestVariables` survive → re-seed + correct delta baseline on next span).

### D7 — REMOVE list (and the compile blocker)

Delete: `_recentKills`, `_recentNibbleBumps`, `_killCorrelatedTargets` (old shape), `EvictStale`, `AbsDelta`, `CorrelateKillsToBump`, `AddToCorrelation`, all three `SnapshotAggregator.CombatCorrelationWindow` references. **Confirmed:** the only references to `CombatCorrelationWindow` anywhere in questforge-tools are these three lines in `SnapshotState.cs`; nothing else depends on it. Add fields `_spanBattleNpcTargets` (`HashSet<uint>`), `_spanNibbleBumps` (`Dictionary<NibbleKey,int>`), `_lastBattleNpcTarget` (`uint?`).

---

## 3. Target wire shapes (verified against a real authoring trace)

| Method | Value shape | Apply behaviour |
|---|---|---|
| `GetTarget` (combat) | `{"baseId":347,"kind":"hostile"}` | hostile branch: set `_lastBattleNpcTarget`; add to span if `_inCombat`. Return `true`. |
| `GetTarget` (NPC) | `1000927` (plain number) | NO combat mutation. Return `false` (unchanged from today — D1). |
| `GetTarget` (other) | object w/o `kind=="hostile"` or w/o `baseId`; null | NO mutation. Return `false`. |
| `InCombat` | `{"value":true}` OR bare `true`/`false` | existing parse retained; false→true seeds (D4). |
| `GetQuestVariables` | `[v0..v5]` OR `{"value":[...]}` | nibble bump into `_spanNibbleBumps` if `_inCombat` (D3). |
| `EnemyKilled` | `{"dataId":N}` | recognised no-op (D2). Return `true`. |

---

## 4. The mirror-correspondence table (live aggregator → offline state)

| `SnapshotAggregator` | `SnapshotState` (after PR-B) |
|---|---|
| `OnBattleNpcTargeted(dataId)` | `case "GetTarget"` hostile branch (D1) |
| `_lastBattleNpcTarget` | `_lastBattleNpcTarget` (new) |
| `_spanBattleNpcTargets` | `_spanBattleNpcTargets` (new) |
| `_spanNibbleBumps` | `_spanNibbleBumps` (new) |
| `OnInCombatChanged(true)` seed | `case "InCombat"` false→true seed (D4) |
| `OnQuestVariablesUpdated` (gated bumps) | `case "GetQuestVariables"` (D3) |
| `BuildKillCorrelatedTargets()` | `BuildKillCorrelatedTargets()` (D5) |
| `ResetDeltas()` | `ResetPendingKeyItemDeltas()` (D6) |
| (none — REMOVED in PR-A) | `EnemyKilled` correlation, `_recentKills`, window (D7) |

---

## 5. Existing tests: which get rewritten

These were written for the kill→bump window model and assert behaviour that no longer exists (kill-before/after-bump correlation, symmetric 500 ms window, `EnemyKilled` driving attribution). They must be **rewritten** to the target-based model.

**`SnapshotStateCombatTests.cs` — REWRITE the following:**
- `Apply_BumpBeforeKill_CorrelatesWithinWindow_GwtO1Prime` → becomes target-during-span happy path (GWT-O-T1). Attribution now comes from `GetTarget{hostile}`, not `EnemyKilled`.
- `Apply_GetQuestVariables_WrongQuest_NoCorrelation_GwtO2Prime` → keep intent (wrong quest → no correlation), restate with a target in the span (GWT-O-T-WRONGQUEST).
- `Apply_KillOutsideSymmetricWindow_NotCorrelated_GwtO3Prime` → **DELETE.** The window no longer exists; there is no "outside the window" concept. Replaced by GWT-O-T3' (no-target span → null).
- `Apply_BothNibblesInOneWrite_TwoNibbleKeyEntries_GwtO4Prime` → rewrite to seed via target, not kill (GWT-O-T5).
- `Apply_HighNibbleObjective_CorrectNibbleKeyEntry_GwtO5Prime` → rewrite to target-based (GWT-O-T5-HIGH).
- `ResetPendingKeyItemDeltas_ClearsBothBuffers_KeepsBaseline_GwtO6Prime` → rewrite to GWT-O-T7' (clear span; preserve `_prevQuestVariables` AND `_lastBattleNpcTarget`; re-seed on next span).
- `Apply_SequenceAdvance_DoesNotCorrelate_GwtO8Prime` → **KEEP** (still valid: sequence advance never attributes). Restate without kill buffering assumptions.
- `Apply_FirstGetQuestVariables_NonZeroNoKill_NoSpuriousCorrelation` → **KEEP** the intent (first obs = baseline only) as GWT-O-T-BASELINE; drop the kill.
- `Apply_InCombatTransition_RecordsCombatStartZoneAndPosition_GwtO7Prime` → **KEEP unchanged** (GWT-O-T8). Position/zone capture is unaffected.
- `ToSnapshot_ProjectsInCombatField`, `Apply_InCombat_BareBoolShape_IsRecognised` → **KEEP unchanged.**

**`TraceToQuestExtractorCombatTests.cs` — REWRITE the trace builders + the following:**
- `BuildNibbleCombatTrace` / `BuildUncorrelatedKillsTrace` → rebuild around `GetTarget{hostile}` + `InCombat` + `GetQuestVariables`, dropping `EnemyKilled` as the attribution driver (it may remain in the stream as a no-op).
- `Extract_NibbleCombatWindow_ProducesCombatStep_WithNibbleExpect_GwtE1Prime` → GWT-O-E1' (same assertions: `KillEnemyDataIds==[347]`, `Spawn==OverworldEnemies`, `Expect=="questVariableLow(65847, 0) >= 3"`, Spawn-review TODO).
- `Extract_CombatWindowWithWaitDecision_StillEmitsCombatStep_GwtE2Prime` → keep (wait-skip guard still deferred); rebuild trace.
- `Extract_KillsWithNoInWindowNibbleBump_NoCombatStep_GwtE3Prime` → reframe as "no target in span → no CombatStep" (GWT-O-E3'): `EnemyKilled` present but no `GetTarget{hostile}` ⇒ `KillCorrelatedTargets` null ⇒ inference falls through to navigate ⇒ no `CombatStep`.
- `Extract_CombatStep_ExpectPredicate_MatchesStepInferenceEngine_GwtE4Prime` → keep as the LIVE==OFFLINE parity lock (GWT-O-E4'); rebuild trace.
- `Extract_CombatStep_LocationZone_FromCombatStartZone`, `Extract_CombatStep_PopulatesZoneAndRequiredZone_MatchingLiveStepFactory` → keep; rebuild trace.

**`SnapshotStateTests.cs` / `TraceToQuestExtractorTests.cs` (non-combat):** no rewrite expected. Verify none assert "GetTarget is unrecognised" (none should — there was no GetTarget case). One belt-and-braces test (GWT-O-T-B) pins plain-number `GetTarget` returns `false`.

---

## 6. GWT scenarios (Tester writes these as failing tests)

All event sequences are applied to a fresh `SnapshotState(new QuestId(65847))` in timestamp order, then assert on `ToSnapshot(at).KillCorrelatedTargets` (`Dictionary<NibbleKey, KillCorrelation>`) and/or the extracted `CombatStep`. Each mirrors the named live `CombatCorrelationAggregatorTests` case. Use `GetTarget{hostile, baseId}` for targets (NOT `EnemyKilled`).

### Unit (SnapshotState) — mirror Slice A

**GWT-O-T1 — target during span + nibble bump attributes (mirror GwtT1)**
- Given baseline `[0,0,0,0,0,0]` (first `GetQuestVariables`).
- When `InCombat{true}`; `GetTarget{baseId:338,kind:hostile}`; `GetQuestVariables [0,0x30,0,0,0,0]` (V1 high 0→3).
- Then `KillCorrelatedTargets[(1,High)] == ([338], 3)`; `(1,Low)` absent.

**GWT-O-T2' — pre-combat target seeded (THE bug; mirror GwtT2')**
- Given baseline.
- When `GetTarget{baseId:347,hostile}` (NOT in combat); `InCombat{true}` (seeds 347); `GetQuestVariables [0x03,0,...]` (V0 low 0→3).
- Then `KillCorrelatedTargets[(0,Low)] == ([347], 3)`; `(0,High)` absent.
- **RED today:** pre-combat target dropped; span empty; KCT null.

**GWT-O-T2b — seed + in-combat swap (mirror GwtT2b)**
- Given baseline.
- When `GetTarget{347}`; `InCombat{true}` (seeds 347); `GetTarget{348}` (in-combat add); `GetQuestVariables [0x03,0,...]`.
- Then `(0,Low).DataIds == [347, 348]` (sorted), `FinalValue == 3`.

**GWT-O-T3' — no target before/in span → no attribution (mirror GwtT3'; replaces O3')**
- Given baseline.
- When `InCombat{true}` (no prior `GetTarget`); `GetQuestVariables [0x01,0,...]`.
- Then `KillCorrelatedTargets` is null/empty (bump but empty span target-set).

**GWT-O-T4 — successive bumps → highest value (mirror GwtT4)**
- Given baseline; `InCombat{true}`; `GetTarget{347}` (in-combat).
- When `GetQuestVariables 0x01`, then `0x02`, then `0x03` (V0 low).
- Then `(0,Low) == ([347], 3)`.

**GWT-O-T5 — both nibbles one write, single target (mirror GwtT5)**
- Given baseline `[0x02,0,...]`; `InCombat{true}`; `GetTarget{347}`.
- When `GetQuestVariables [0x13,0,...]` (low 2→3, high 0→1).
- Then `(0,Low)==([347],3)` AND `(0,High)==([347],1)`.

**GWT-O-T5-HIGH — high-nibble objective at V1 (mirror GwtT5 high variant)**
- Given baseline `[0,0x02,0,...]`; `InCombat{true}`; `GetTarget{338}`.
- When `GetQuestVariables [0,0x32,0,...]` (V1 high 0→3, V1 low 2→2 unchanged).
- Then `(1,High)==([338],3)`; `(1,Low)` absent.

**GWT-O-T6 — mixed-pack span, both targets on every nibble (mirror GwtT6)**
- Given baseline; `InCombat{true}`; `GetTarget{347}`; `GetTarget{49}` (swap).
- When `GetQuestVariables [0x03,0,...]` (V0 low); `GetQuestVariables [0x03,0x03,...]` (V1 low).
- Then `(0,Low)` and `(1,Low)` both present, each `DataIds==[49,347]` (sorted).

**GWT-O-T7' — ResetPendingKeyItemDeltas clears span; `_lastBattleNpcTarget` + `_prevQuestVariables` persist (mirror GwtT7'; replaces O6')**
- Given baseline; `GetTarget{347}`; `InCombat{true}`; `GetQuestVariables [0x03,0,...]` ⇒ `(0,Low)==([347],3)` (precondition).
- Part A: `ResetPendingKeyItemDeltas()` ⇒ KCT empty.
- Part B: `InCombat{false}`; `InCombat{true}` (re-seeds persisted 347); `GetQuestVariables [0x03,0,...]` (3→3, no delta — baseline preserved) ⇒ KCT empty.
- Part C: `GetQuestVariables [0x04,0,...]` (3→4) ⇒ `(0,Low)==([347],4)` (347 re-seeded from persistent `_lastBattleNpcTarget`).

**GWT-O-T8 — InCombat false→true records start pos/zone (mirror GwtT8 / keep O7')**
- Given `GetPlayerZone{148}`; `GetPlayerPosition{10,0,20}`.
- When `InCombat{true}`.
- Then `CombatStartZone==148`, `CombatStartPosition==(10,0,20)`, `InCombat==true`.

**GWT-O-T9 — resumed-quest first obs, no spurious attribution (mirror GwtT9)**
- Given `InCombat{true}`; `GetTarget{338}`.
- When `GetQuestVariables [0,0x30,0,...]` as the FIRST observation (no prior baseline).
- Then `KillCorrelatedTargets` is null (first obs = baseline only).

**GWT-O-T10' — new span re-seeds from current target, not stale (mirror GwtT10')**
- Given baseline; `GetTarget{347}`; `InCombat{true}`; `GetQuestVariables [0x03,0,...]`; `InCombat{false}`.
- When `GetTarget{338}` (retarget between spans); `InCombat{true}` (clears span A; seeds 338); `GetQuestVariables [0x03,0x03,0,...]` (V1 low 0→3).
- Then `(1,Low)==([338],3)`; `(0,Low)` absent; `347` not in `(1,Low).DataIds`.

### D1/D2 recognition pins

**GWT-O-T-A — hostile-object GetTarget is recognised and mutates**
- When `InCombat{true}`; `Apply(GetTarget{baseId:347,kind:hostile})`.
- Then `Apply` returns `true`; `GetQuestVariables [0x03,0,...]` ⇒ `(0,Low).DataIds` contains 347.

**GWT-O-T-B — plain-number GetTarget is unrecognised, no combat mutation**
- When `InCombat{true}`; `r = Apply(GetTarget value:1000927)` (plain number); `GetQuestVariables [0x03,0,...]`.
- Then `r == false`; `KillCorrelatedTargets` is null/empty (no target in span). `LastNpcInteracted` unchanged (null).

**GWT-O-T-NOOP — EnemyKilled is a recognised no-op**
- When `InCombat{true}`; `r = Apply(EnemyKilled{dataId:347})`; `GetQuestVariables [0x03,0,...]` (no `GetTarget`).
- Then `r == true`; `KillCorrelatedTargets` is null/empty (kill does not attribute).

**GWT-O-T-BASELINE — first GetQuestVariables is baseline only (keep)**
- When `InCombat{true}`; first `GetQuestVariables [0x02,0,...]`; later same `[0x02,0,...]`.
- Then no bump; KCT null even with a target seeded.

**GWT-O-T-WRONGQUEST — wrong-quest GetQuestVariables ignored (mirror O2')**
- When `InCombat{true}`; `GetTarget{347}`; `GetQuestVariables` for quest 12345 `[0x01,0,...]`.
- Then `Apply` returns `true`; KCT null/empty.

### Extractor (end-to-end) — mirror Slice B

**GWT-O-E1' — end-to-end target combat → CombatStep with nibble expect**
- Trace: RunStart 65847; zone 148; pos (10,0,20); `InCombat{true}`; baseline `GetQuestVariables [0x00,...]`; `GetTarget{baseId:347,hostile}`; `GetQuestVariables [0x01]`→`[0x02]`→`[0x03]`; a `wait` decision; RunEnd.
- Then a `CombatStep` with `KillEnemyDataIds` contains 347; `Spawn==OverworldEnemies`; `Expect.Predicate == "questVariableLow(65847, 0) >= 3"`; Spawn-review TODO present.

**GWT-O-E2' — combat `wait` decision window still emits CombatStep**
- Same trace, decision `wait`. Then CombatStep emitted (skip guard deferred); `Expect` starts with `questVariableLow`.

**GWT-O-E3' — kills with no target in span → no CombatStep**
- Trace: `InCombat{true}`; baseline; `EnemyKilled{347}` (NO `GetTarget{hostile}`); `GetQuestVariables [0x01]`; `navigate` decision + submitted/completed; RunEnd.
- Then no `CombatStep` (KCT null ⇒ inference falls through to navigate/Rule 3).

**GWT-O-E4' — LIVE==OFFLINE parity lock (mirror E4')**
- Part a: run extractor on the GWT-O-E1' trace ⇒ `CombatStep.Expect.Predicate`.
- Part b: build an equivalent `GameStateSnapshot` with `KillCorrelatedTargets={ (0,Low): ([347],3) }`, `InCombat`, zone 148; before = that `with { KillCorrelatedTargets=null, InCombat=false }`; call `new StepInferenceEngine().Infer(before, after)`.
- Then `inference.StepType=="combat"`; `extracted == inference.SuggestedExpect == "questVariableLow(65847, 0) >= 3"`. Also assert `CombatStep.Id == "defeat-347"` to pin step-id parity.

**GWT-O-E-LOCZONE — CombatStep.Location.Zone from CombatStartZone (keep)**
- GWT-O-E1' trace ⇒ `CombatStep.Location.Zone == 148`, and `Zone=="148"`, `RequiredZone=="148"`.

---

## 7. Implementation order (Builder, after RED)

- **Phase A — unblock compile (DONE-BEFORE-NEXT).** Remove the three `CombatCorrelationWindow` references and the dead members (`EvictStale`, `AbsDelta`, `CorrelateKillsToBump`, `AddToCorrelation`, `_recentKills`, `_recentNibbleBumps`, old `_killCorrelatedTargets`). Add new fields. Solution must compile (existing ~152 tests will fail/error on combat assertions — that's expected RED). ~0.5 day.
- **Phase B — target + combat span.** `GetTarget` hostile case (D1); `EnemyKilled` no-op (D2); `InCombat` seed (D4); `GetQuestVariables` gated bumps (D3); `BuildKillCorrelatedTargets` (D5); `ResetPendingKeyItemDeltas` (D6). ~0.5 day.
- **Phase C — green the extractor parity** (GWT-O-E1'..E4'); no extractor edits expected. ~0.25 day.

---

## 8. Done criteria

1. `QuestForge.Tools.Trace` compiles against the `feat/combat-dalamud-probe` engine (no `CombatCorrelationWindow` reference).
2. `qf-trace extract-quest` on a target-based combat trace emits a `CombatStep` with `Id=="defeat-<lowestDataId>"`, `Expect=="questVariableLow|High(<questId>, <idx>) >= <final>"`, `Spawn==OverworldEnemies`, `Location.Zone==<combatStartZone>` — byte-identical to the live author tool for the same event stream (GWT-O-E4' parity lock).
3. A pre-combat `GetTarget{hostile}` followed by `InCombat{true}` attributes the bumped nibble to the seeded target (GWT-O-T2' green) — the bug is fixed offline.
4. `EnemyKilled` and plain-number `GetTarget` recognition bits are unchanged vs pre-PR behaviour (`EnemyKilled`→true, plain `GetTarget`→false).
5. All Slice A live cases have an offline mirror that passes (GWT-O-T1..T10', plus recognition pins).
6. Full `QuestForge.Tools.Trace.Tests` suite green; non-combat tests unaffected.

---

## 9. Exclusions

- No change to `TraceToQuestExtractor.cs`, `StepFactory`, `StepInferenceEngine`, or any engine code (PR-A owns those).
- No new `qf-trace` CLI surface, flags, or output format.
- Plain-number `GetTarget` is NOT wired into NPC-interaction inference (`LastNpcInteracted` stays sourced from `RecordInteract`).
- No fixture re-recording; synthetic event streams only (real-trace corpus migration is separate).
- No time-window correlation in any form — the model is purely span/target based.

---

✅ READY FOR TEST CREATION

Tester: Write failing tests from the GWT specs in §6. Rewrite the existing combat tests per §5.
- Happy paths: 7 scenarios (O-T1, T2', T2b, T4, T5, T5-HIGH, T6)
- Edge cases: 6 scenarios (O-T3', T7', T8, T9, T10', T-BASELINE)
- Recognition/no-op cases: 4 scenarios (O-T-A, T-B, T-NOOP, T-WRONGQUEST)
- Extractor / parity cases: 5 scenarios (O-E1', E2', E3', E4', E-LOCZONE)
- Expected total: ~22 tests in QuestForge.Tools.Trace.Tests (replacing/rewriting GWT-O1'..O8' and GWT-E1'..E4').
