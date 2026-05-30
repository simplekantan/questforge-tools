using System.Text.Json;
using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

/// <summary>
/// Validates the structural integrity of a QuestDefinition (SCHEMA.md §8.1).
/// Uses two passes:
///   Pass 1 — walk the entire step tree, build step-ID → scope map, detect duplicates.
///   Pass 2 — walk again, validate all rules using the pass 1 map.
/// Scope tracking is private to this class; it does not leak into ValidationContext.
/// </summary>
public sealed class StructuralValidator(IFragmentRegistry fragments) : IValidator
{
    public IEnumerable<ValidationError> Validate(QuestDefinition quest, ValidationContext ctx)
    {
        var errors = new List<ValidationError>();

        ValidateRequiredFields(quest, ctx, errors);
        ValidateSequences(quest, ctx, errors);

        var (idMap, duplicates) = BuildStepIdMap(quest);

        ValidateStepIds(quest, ctx, idMap, duplicates, errors);
        ValidateRecoveryRules(quest, ctx, idMap, duplicates, errors);
        ValidateBranchRules(quest, ctx, errors);
        ValidateFragmentRules(quest, ctx, errors);
        ValidateStepTypeRules(quest, ctx, errors);
        ValidateNotesLength(quest, ctx, errors);
        ValidateRouteHints(quest, ctx, errors);
        ValidateRequiredZone(quest, ctx, errors);
        ValidateResumePoint(quest, ctx, errors);

        return errors;
    }

    public IEnumerable<ValidationError> ValidateFragmentDefinition(FragmentDefinition fragment, ValidationContext ctx)
    {
        var errors = new List<ValidationError>();
        CheckResumePointInFragment(fragment.Steps, fragment, ctx, errors);
        return errors;
    }

    // -------------------------------------------------------------------------
    // Pass 1 — build step-ID map
    // -------------------------------------------------------------------------

    private static (Dictionary<string, ValidationScope> IdMap, HashSet<string> Duplicates)
        BuildStepIdMap(QuestDefinition quest)
    {
        var map = new Dictionary<string, ValidationScope>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seq in quest.Sequences)
        {
            var scope = new ValidationScope(seq.Sequence);
            CollectIds(seq.Steps, scope, map, duplicates);
        }

        return (map, duplicates);
    }

    private static void CollectIds(
        Step[] steps,
        ValidationScope scope,
        Dictionary<string, ValidationScope> map,
        HashSet<string> duplicates)
    {
        foreach (var step in steps)
        {
            if (map.ContainsKey(step.Id))
                duplicates.Add(step.Id);
            else
                map[step.Id] = scope;

            if (step is BranchStep branch)
            {
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var branchScope = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CollectIds(branch.Branches[i].Steps ?? [], branchScope, map, duplicates);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Pass 2 — validation rules (stubs return empty; filled in rule by rule)
    // -------------------------------------------------------------------------

    private static void ValidateRequiredFields(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(quest.SchemaVersion))
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'schemaVersion' is required and must be non-empty."));

        if (quest.SupportStatus is null)
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'supportStatus' is required."));

        if (string.IsNullOrWhiteSpace(quest.LastVerifiedPatch))
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'lastVerifiedPatch' is required and must be non-empty."));

        if (quest.Requirements is null)
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'requirements' is required."));

        if (quest.AcceptFrom is null)
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'acceptFrom' is required."));

        if (quest.Sequences.Length == 0)
            errors.Add(E(ctx, "structural/sequences-empty", "root",
                "'sequences' must contain at least one entry."));
    }

    private static void ValidateSequences(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        if (quest.Sequences.Length == 0) return; // already reported above

        if (!quest.Sequences.Any(s => s.Sequence == 0))
            errors.Add(E(ctx, "structural/sequence-zero-missing", "root",
                "At least one sequence with 'sequence: 0' is required."));

        var seen = new HashSet<int>();
        for (var i = 0; i < quest.Sequences.Length; i++)
        {
            var seq = quest.Sequences[i];

            if (!seen.Add(seq.Sequence))
                errors.Add(E(ctx, "structural/sequence-duplicate", $"seq:{seq.Sequence}",
                    $"Sequence number {seq.Sequence} appears more than once."));

            if (i > 0 && seq.Sequence <= quest.Sequences[i - 1].Sequence)
                errors.Add(E(ctx, "structural/sequence-not-increasing", $"seq:{seq.Sequence}",
                    $"Sequence numbers must be strictly increasing; " +
                    $"{quest.Sequences[i - 1].Sequence} → {seq.Sequence} is not."));
        }
    }

    private static readonly System.Text.RegularExpressions.Regex StepIdRegex =
        new(@"^[a-z][a-z0-9-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void ValidateStepIds(
        QuestDefinition quest, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        // Format check — walk all steps at all nesting levels
        foreach (var seq in quest.Sequences)
            CheckStepIdFormats(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);

        // Uniqueness — duplicates were already detected in pass 1
        foreach (var id in duplicates)
            errors.Add(E(ctx, "structural/step-id-duplicate",
                idMap.TryGetValue(id, out var scope) ? scope.ToString() : "root",
                $"Step ID '{id}' is not unique within this quest.",
                stepId: id));
    }

    private static void CheckStepIdFormats(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.Id) || !StepIdRegex.IsMatch(step.Id))
                errors.Add(E(ctx, "structural/step-id-invalid-format", scope.ToString(),
                    $"Step ID '{step.Id}' must match ^[a-z][a-z0-9-]*$ (lowercase letters, digits, hyphens; must start with a letter).",
                    stepId: step.Id));

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckStepIdFormats(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    private static void ValidateRecoveryRules(
        QuestDefinition quest, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckRecoveryRules(seq.Steps, new ValidationScope(seq.Sequence), ctx, idMap, duplicates, errors);
    }

    private static void CheckRecoveryRules(
        Step[] steps, ValidationScope scope, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step is AwaitUserStep awaitStep && awaitStep.Reason?.Length > 200)
                errors.Add(E(ctx, "structural/recover-reason-too-long", scope.ToString(),
                    $"Step '{step.Id}': 'reason' must be ≤200 characters (got {awaitStep.Reason.Length}).",
                    stepId: step.Id));

            if (step.Recover is { } recover)
            {
                RecoverAction?[] actions =
                [
                    recover.OnTimeout, recover.OnObstacle, recover.OnAdapterError,
                    recover.OnPostconditionFailed, recover.OnPlayerDefeated
                ];

                foreach (var action in actions)
                {
                    if (action is GotoRecoverAction gotoAction)
                        ValidateGotoAction(gotoAction, step, scope, ctx, idMap, duplicates, errors);
                    else if (action is AwaitUserRecoverAction awaitAction && awaitAction.Reason?.Length > 200)
                        errors.Add(E(ctx, "structural/recover-reason-too-long", scope.ToString(),
                            $"Step '{step.Id}': recovery 'reason' must be ≤200 characters (got {awaitAction.Reason.Length}).",
                            stepId: step.Id));
                }
            }

            if (step is BranchStep branch)
            {
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckRecoveryRules(branch.Branches[i].Steps ?? [], inner, ctx, idMap, duplicates, errors);
                }
            }
        }
    }

    private static void ValidateGotoAction(
        GotoRecoverAction gotoAction, Step step, ValidationScope sourceScope, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        var targetId = gotoAction.StepId;

        if (duplicates.Contains(targetId))
            return; // target is ambiguous — step-id-duplicate already reported, suppress goto checks

        if (!idMap.TryGetValue(targetId, out var targetScope))
        {
            errors.Add(E(ctx, "structural/recovery-goto-unresolved", sourceScope.ToString(),
                $"Step '{step.Id}': recovery goto references unknown step ID '{targetId}'.",
                stepId: step.Id));
        }
        else if (!sourceScope.IsCompatibleWith(targetScope))
        {
            errors.Add(E(ctx, "structural/recovery-goto-cross-branch", sourceScope.ToString(),
                $"Step '{step.Id}': recovery goto to '{targetId}' crosses scope boundaries " +
                $"(source: {sourceScope}, target: {targetScope}).",
                stepId: step.Id));
        }
    }

    private static void ValidateBranchRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckBranchRules(seq.Steps, new ValidationScope(seq.Sequence), depth: 0, ctx, errors);
    }

    private static void CheckBranchRules(
        Step[] steps, ValidationScope scope, int depth, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step is not BranchStep branch)
                continue;

            var branchDepth = depth + 1;

            if (branchDepth >= 4)
                errors.Add(E(ctx, "structural/branch-nesting-too-deep", scope.ToString(),
                    $"Branch '{branch.Id}' at nesting depth {branchDepth} exceeds the maximum of 3.",
                    stepId: branch.Id, severity: Severity.Error));
            else if (branchDepth >= 2)
                errors.Add(E(ctx, "structural/branch-nesting-too-deep", scope.ToString(),
                    $"Branch '{branch.Id}' at nesting depth {branchDepth} approaches the limit of 3.",
                    stepId: branch.Id, severity: Severity.Warning));

            if (branch.Branches.Length == 0 || branch.Branches[^1].When != "default")
                errors.Add(E(ctx, "structural/branch-missing-default", scope.ToString(),
                    $"Branch '{branch.Id}': last case must have 'when: \"default\"'.",
                    stepId: branch.Id));

            for (var i = 0; i < branch.Branches.Length; i++)
            {
                var branchCase = branch.Branches[i];
                var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };

                if ((branchCase.Steps?.Length ?? 0) == 0)
                    errors.Add(E(ctx, "structural/branch-empty", inner.ToString(),
                        $"Branch '{branch.Id}' case {i} (when: \"{branchCase.When}\") has no steps.",
                        stepId: branch.Id));

                CheckBranchRules(branchCase.Steps ?? [], inner, branchDepth, ctx, errors);
            }
        }
    }

    private void ValidateFragmentRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckFragmentRules(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private void CheckFragmentRules(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step is FragmentStep fragmentStep)
                ValidateFragmentStep(fragmentStep, scope, ctx, errors);

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckFragmentRules(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    private void ValidateFragmentStep(
        FragmentStep step, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        if (!fragments.TryGetFragment(step.Ref, out var fragment))
        {
            errors.Add(E(ctx, "structural/fragment-not-found", scope.ToString(),
                $"Step '{step.Id}': fragment '{step.Ref}' not found in registry.",
                stepId: step.Id));
            return; // suppress param checks per plan GWT
        }

        if (ContainsFragmentStep(fragment!.Steps))
            errors.Add(E(ctx, "structural/fragment-nested", scope.ToString(),
                $"Step '{step.Id}': fragment '{step.Ref}' references another fragment (nesting not allowed in v1).",
                stepId: step.Id));

        foreach (var param in fragment.Parameters.Where(p => p.Required))
            if (step.Params is null || !step.Params.ContainsKey(param.Name))
                errors.Add(E(ctx, "structural/fragment-missing-param", scope.ToString(),
                    $"Step '{step.Id}': required parameter '{param.Name}' not provided for fragment '{step.Ref}'.",
                    stepId: step.Id));

        if (step.Params is not null)
            foreach (var (paramName, paramValue) in step.Params)
            {
                var declared = fragment.Parameters.FirstOrDefault(p => p.Name == paramName);
                if (declared is null) continue;
                if (!IsParamTypeMatch(declared.Type, paramValue))
                    errors.Add(E(ctx, "structural/fragment-param-type-mismatch", scope.ToString(),
                        $"Step '{step.Id}': parameter '{paramName}' expected type '{declared.Type}' but got {paramValue.ValueKind}.",
                        stepId: step.Id));
            }
    }

    private static bool ContainsFragmentStep(Step[] steps)
    {
        foreach (var step in steps)
        {
            if (step is FragmentStep) return true;
            if (step is BranchStep branch)
                foreach (var c in branch.Branches)
                    if (ContainsFragmentStep(c.Steps ?? [])) return true;
        }
        return false;
    }

    private static bool IsParamTypeMatch(string declaredType, JsonElement value) =>
        declaredType switch
        {
            "position" => value.ValueKind == JsonValueKind.Object,
            "npcId"    => value.ValueKind == JsonValueKind.Number,
            "itemId"   => value.ValueKind == JsonValueKind.Number,
            "string"   => value.ValueKind == JsonValueKind.String,
            _          => true
        };

    private static void ValidateStepTypeRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        ValidatePrereqStates(quest, ctx, errors);
        ValidateChainRules(quest, ctx, errors);

        foreach (var seq in quest.Sequences)
            CheckStepTypeRules(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private static void CheckStepTypeRules(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case TalkStep talk:
                    if (talk.Target is not null && talk.Targets is not null)
                        errors.Add(E(ctx, "structural/step-target-conflict", scope.ToString(),
                            $"Step '{step.Id}': 'target' and 'targets' are mutually exclusive.", stepId: step.Id));
                    ValidateDialogueChoices(step.Id, talk.DialogueChoices, scope, ctx, errors);
                    break;

                case TurnInStep turnIn:
                    ValidateDialogueChoices(step.Id, turnIn.DialogueChoices, scope, ctx, errors);
                    break;

                case DutyStep duty:
                    ValidateDutyStep(duty, scope, ctx, errors);
                    break;

                case UseItemStep useItem:
                    ValidateUseItemStep(useItem, scope, ctx, errors);
                    break;

                case CutsceneStep cutscene:
                    if (cutscene.Skip is not ("never" or "ifAllowed"))
                        errors.Add(E(ctx, "structural/cutscene-skip-invalid", scope.ToString(),
                            $"Step '{step.Id}': 'skip' must be 'never' or 'ifAllowed' (got '{cutscene.Skip}').",
                            stepId: step.Id));
                    break;

                case MinigameStep minigame:
                    if (minigame.Kind is not ("sniping" or "memory" or "aiming" or "rhythm" or "selection" or "other"))
                        errors.Add(E(ctx, "structural/minigame-kind-invalid", scope.ToString(),
                            $"Step '{step.Id}': minigame 'kind' '{minigame.Kind}' is not a known value.",
                            stepId: step.Id));
                    break;

                case CombatStep combat:
                    if (combat.Spawn == CombatSpawn.OverworldEnemies && combat.KillEnemyDataIds.Length == 0)
                        errors.Add(E(ctx, "structural/combat-overworld-needs-kill-ids", scope.ToString(),
                            $"Step '{combat.Id}': 'overworldEnemies' spawn requires at least one entry in 'killEnemyDataIds'.",
                            stepId: combat.Id));
                    if (combat.Expect is null)
                        errors.Add(E(ctx, "structural/combat-missing-expect", scope.ToString(),
                            $"Step '{combat.Id}': combat step has no 'expect' predicate and can never complete.",
                            stepId: combat.Id, severity: Severity.Warning));
                    if (combat.KillEnemyDataIds.Any(id => id == 0))
                        errors.Add(E(ctx, "structural/combat-kill-id-zero", scope.ToString(),
                            $"Step '{combat.Id}': 'killEnemyDataIds' contains a zero entry; 0 is not a valid BNpc data-id.",
                            stepId: combat.Id));
                    break;

                case PurchaseItemStep purchase:
                    if (purchase.ItemId == 0)
                        errors.Add(E(ctx, "structural/purchase-item-id-zero", scope.ToString(),
                            $"Step '{purchase.Id}': 'itemId' is 0; a valid item id is required.",
                            stepId: purchase.Id));
                    if (purchase.Quantity < 1)
                        errors.Add(E(ctx, "structural/purchase-quantity-nonpositive", scope.ToString(),
                            $"Step '{purchase.Id}': 'quantity' must be >= 1 (got {purchase.Quantity}).",
                            stepId: purchase.Id));
                    if (purchase.Target.NpcId == 0)
                        errors.Add(E(ctx, "structural/purchase-npc-id-zero", scope.ToString(),
                            $"Step '{purchase.Id}': vendor 'target.npcId' is 0; a valid NPC id is required.",
                            stepId: purchase.Id));
                    if (purchase.GcCategory.HasValue && (purchase.GcCategory.Value < 0 || purchase.GcCategory.Value > 3))
                        errors.Add(E(ctx, "structural/purchase-gc-category-out-of-range", scope.ToString(),
                            $"Step '{purchase.Id}': 'gcCategory' must be 0..3 (0=Weapons, 1=Armor, 2=Materiel, 3=Materials); got {purchase.GcCategory.Value}.",
                            stepId: purchase.Id));
                    if (purchase.GcRankTier.HasValue && (purchase.GcRankTier.Value < 0 || purchase.GcRankTier.Value > 2))
                        errors.Add(E(ctx, "structural/purchase-gc-rank-tier-out-of-range", scope.ToString(),
                            $"Step '{purchase.Id}': 'gcRankTier' must be 0..2 (0=lowest visible, 2=highest); got {purchase.GcRankTier.Value}.",
                            stepId: purchase.Id));
                    if (purchase.Currency != PurchaseCurrency.GcSeals && (purchase.GcCategory.HasValue || purchase.GcRankTier.HasValue))
                        errors.Add(E(ctx, "structural/purchase-gc-fields-on-gil", scope.ToString(),
                            $"Step '{purchase.Id}': 'gcCategory'/'gcRankTier' are ignored when 'currency' is not 'gcSeals'; remove them or set 'currency: gcSeals'.",
                            stepId: purchase.Id, severity: Severity.Warning));
                    break;

                case WaitStep wait:
                    if (wait.Seconds <= 0)
                        errors.Add(E(ctx, "structural/wait-seconds-nonpositive", scope.ToString(),
                            $"Step '{wait.Id}': wait 'seconds' must be > 0 (got {wait.Seconds}); a non-positive wait completes immediately.",
                            stepId: wait.Id, severity: Severity.Warning));
                    break;

                case BranchStep branch:
                    for (var i = 0; i < branch.Branches.Length; i++)
                    {
                        var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                        CheckStepTypeRules(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                    }
                    break;
            }
        }
    }

    private static void ValidateDialogueChoices(
        string stepId, DialogueChoice[] choices, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            if (choice.Type is not ("list" or "yesno" or "talk"))
                errors.Add(E(ctx, "structural/dialogue-choice-type-invalid", scope.ToString(),
                    $"Step '{stepId}': dialogueChoice[{i}].type '{choice.Type}' must be 'list', 'yesno', or 'talk'.",
                    stepId: stepId));

            if (choice.Type == "yesno" && choice.Answer is not ("yes" or "no"))
                errors.Add(E(ctx, "structural/dialogue-choice-answer-invalid", scope.ToString(),
                    $"Step '{stepId}': dialogueChoice[{i}] answer '{choice.Answer}' must be 'yes' or 'no'.",
                    stepId: stepId));
        }
    }

    private static void ValidateDutyStep(
        DutyStep step, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        if (step.Kind == "regular")
        {
            if (step.DutyId is null)
                errors.Add(E(ctx, "structural/duty-missing-required-field", scope.ToString(),
                    $"Step '{step.Id}': duty kind 'regular' requires 'dutyId'.", stepId: step.Id));
            if (step.EntryNpc is null)
                errors.Add(E(ctx, "structural/duty-missing-required-field", scope.ToString(),
                    $"Step '{step.Id}': duty kind 'regular' requires 'entryNpc'.", stepId: step.Id));
        }
        else if (step.Kind == "spd")
        {
            if (step.Trigger is null)
                errors.Add(E(ctx, "structural/duty-missing-required-field", scope.ToString(),
                    $"Step '{step.Id}': duty kind 'spd' requires 'trigger'.", stepId: step.Id));
            if (step.DutyId is not null)
                errors.Add(E(ctx, "structural/duty-invalid-field-for-kind", scope.ToString(),
                    $"Step '{step.Id}': 'dutyId' must not be set for duty kind 'spd'.", stepId: step.Id));
        }
    }

    // ValidateUseItemStep — rewritten for new flat schema shape (Decision UI11 / USE_ITEM_STEP_PLAN.md)
    // Old shape: step.Target (UseItemTarget? with Kind discriminator) — DELETED
    // New shape: step.ItemId, step.TargetNpcId (uint?), step.TargetPosition (Position3?)
    //
    // TODO: This method will compile once the new UseItemStep shape is in QuestForge.Schema.
    //       Until then it produces compile errors referencing the deleted UseItemTarget — that
    //       is the intended RED state (Builder task: apply schema rewrite so this compiles).
    private static void ValidateUseItemStep(
        UseItemStep step, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        // structural/use-item-itemid-zero — mirrors engine-side E13
        if (step.ItemId == 0)
            errors.Add(E(ctx, "structural/use-item-itemid-zero", scope.ToString(),
                $"Step '{step.Id}': 'itemId' is 0; a valid item id is required.",
                stepId: step.Id));

        // structural/use-item-target-npc-id-zero — mirrors engine-side E14
        // null is allowed (means self-cast); explicit zero is always invalid
        if (step.TargetNpcId == 0)
            errors.Add(E(ctx, "structural/use-item-target-npc-id-zero", scope.ToString(),
                $"Step '{step.Id}': 'targetNpcId' is 0; use null to indicate no NPC target, not 0.",
                stepId: step.Id));

        // structural/use-item-ambiguous-target — mirrors engine-side E15
        // At most one of TargetNpcId / TargetPosition may be set (Decision UI4)
        if (step.TargetNpcId.HasValue && step.TargetPosition is not null)
            errors.Add(E(ctx, "structural/use-item-ambiguous-target", scope.ToString(),
                $"Step '{step.Id}': both 'targetNpcId' and 'targetPosition' are set; they are mutually exclusive. " +
                "Use 'targetNpcId' for NPC targets or 'targetPosition' for AoE ground targets, not both.",
                stepId: step.Id));
    }

    private static void ValidatePrereqStates(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        if (quest.Requirements is null) return;
        foreach (var prereq in quest.Requirements.Prereqs)
            if (prereq.State is not ("complete" or "accepted"))
                errors.Add(E(ctx, "structural/prereq-state-invalid", "root",
                    $"Prereq {prereq.QuestId}: state '{prereq.State}' must be 'complete' or 'accepted'."));
    }

    private static void ValidateChainRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        if (quest.Chain is null) return;
        var next = quest.Chain.Next;
        if (next.Length == 0) return;

        for (var i = 0; i < next.Length; i++)
            if (string.IsNullOrEmpty(next[i].When))
                errors.Add(E(ctx, "structural/chain-when-empty", "root",
                    $"chain.next[{i}]: 'when' must not be empty or null."));

        if (next[^1].When != "default")
            errors.Add(E(ctx, "structural/chain-missing-default", "root",
                "chain.next: last entry must have 'when: \"default\"'."));
    }

    private static void ValidateNotesLength(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        if (quest.Notes?.Length > 500)
            errors.Add(E(ctx, "structural/notes-too-long", "root",
                $"Quest notes must be ≤500 characters (got {quest.Notes.Length})."));

        foreach (var seq in quest.Sequences)
            CheckNotesLength(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private static void CheckNotesLength(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step.Notes?.Length > 500)
                errors.Add(E(ctx, "structural/notes-too-long", scope.ToString(),
                    $"Step '{step.Id}': notes must be ≤500 characters (got {step.Notes.Length}).",
                    stepId: step.Id));

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckNotesLength(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    // -------------------------------------------------------------------------
    // RouteHint validation
    // -------------------------------------------------------------------------

    private static void ValidateRouteHints(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckRouteHints(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private static void CheckRouteHints(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step is TravelStep travel && travel.RouteHint?.Aethernet is { } aethernet)
            {
                if (aethernet.To == 0)
                    errors.Add(E(ctx, "structural/route-hint-aethernet-to-missing", scope.ToString(),
                        $"Step '{step.Id}': RouteHint.Aethernet.To must be non-zero (the destination shard ID is required).",
                        stepId: step.Id, severity: Severity.Error));

                if (aethernet.From == null)
                    errors.Add(E(ctx, "structural/route-hint-aethernet-from-missing", scope.ToString(),
                        $"Step '{step.Id}': RouteHint.Aethernet.From is not set (source shard ID recommended for full route hint).",
                        stepId: step.Id, severity: Severity.Warning));
            }

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckRouteHints(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    // -------------------------------------------------------------------------
    // RequiredZone validation
    // -------------------------------------------------------------------------

    private static void ValidateRequiredZone(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckRequiredZone(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private static void CheckRequiredZone(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step.RequiredZone is { } rz && rz.Length > 0
                && !uint.TryParse(rz, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                errors.Add(E(ctx, "structural/required-zone-not-numeric", scope.ToString(),
                    $"Step '{step.Id}': RequiredZone must be a numeric zone ID string (e.g. \"128\"), but was \"{rz}\".",
                    stepId: step.Id, severity: Severity.Error));
            }

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckRequiredZone(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    // -------------------------------------------------------------------------
    // ResumePoint validation (R1)
    // -------------------------------------------------------------------------

    private static void ValidateResumePoint(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var seq in quest.Sequences)
            CheckResumePoint(seq.Steps, new ValidationScope(seq.Sequence), ctx, errors);
    }

    private static void CheckResumePoint(
        Step[] steps, ValidationScope scope, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            if (step.ResumePointFragmentId is { } id && string.IsNullOrWhiteSpace(id))
            {
                errors.Add(E(ctx, "structural/resume-point-fragment-id-empty", scope.ToString(),
                    $"Step '{step.Id}': ResumePointFragmentId, when set, must be a non-empty, non-whitespace string.",
                    stepId: step.Id, severity: Severity.Error));
            }

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                {
                    var inner = scope with { BranchStepId = branch.Id, BranchCaseIndex = i };
                    CheckResumePoint(branch.Branches[i].Steps ?? [], inner, ctx, errors);
                }
        }
    }

    private static void CheckResumePointInFragment(
        Step[] steps, FragmentDefinition fragment, ValidationContext ctx, List<ValidationError> errors)
    {
        foreach (var step in steps)
        {
            var r2Fired = false;

            // R2: any presence of ResumePointFragmentId inside a fragment is an error
            if (step.ResumePointFragmentId is not null)
            {
                r2Fired = true;
                errors.Add(E(ctx, "structural/resume-point-nested", "fragment",
                    $"Fragment '{fragment.FragmentId}' step '{step.Id}': fragments may not declare ResumePointFragmentId (resume positioning is depth-1 only; nested resumes are not supported).",
                    stepId: step.Id, severity: Severity.Error));

                // R1 in fragment context: also check if the value is empty/whitespace
                if (string.IsNullOrWhiteSpace(step.ResumePointFragmentId))
                {
                    errors.Add(E(ctx, "structural/resume-point-fragment-id-empty", "fragment",
                        $"Step '{step.Id}': ResumePointFragmentId, when set, must be a non-empty, non-whitespace string.",
                        stepId: step.Id, severity: Severity.Error));
                }
            }

            // R3: fragment step IDs should start with "fragment-" — suppressed when R2 fired
            if (!r2Fired && !step.Id.StartsWith("fragment-", StringComparison.Ordinal))
            {
                errors.Add(E(ctx, "structural/fragment-step-id-convention", "fragment",
                    $"Fragment '{fragment.FragmentId}' step '{step.Id}': fragment step IDs should follow the 'fragment-*' naming convention.",
                    stepId: step.Id, severity: Severity.Warning));
            }

            if (step is BranchStep branch)
                for (var i = 0; i < branch.Branches.Length; i++)
                    CheckResumePointInFragment(branch.Branches[i].Steps ?? [], fragment, ctx, errors);
        }
    }

    // -------------------------------------------------------------------------
    // Factory helper — keeps error construction concise
    // -------------------------------------------------------------------------

    private static ValidationError E(
        ValidationContext ctx,
        string code,
        string location,
        string message,
        string? stepId = null,
        Severity severity = Severity.Error) =>
        new(code, message, ctx.FilePath, location, stepId, severity);
}