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
        if (string.IsNullOrEmpty(quest.SchemaVersion))
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'schemaVersion' is required and must be non-empty."));

        if (quest.SupportStatus is null)
            errors.Add(E(ctx, "structural/required-field-missing", "root",
                "'supportStatus' is required."));

        if (string.IsNullOrEmpty(quest.LastVerifiedPatch))
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

    private static void ValidateStepIds(
        QuestDefinition quest, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        // TODO: implement
    }

    private static void ValidateRecoveryRules(
        QuestDefinition quest, ValidationContext ctx,
        Dictionary<string, ValidationScope> idMap, HashSet<string> duplicates,
        List<ValidationError> errors)
    {
        // TODO: implement
    }

    private static void ValidateBranchRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        // TODO: implement
    }

    private static void ValidateFragmentRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        // TODO: implement
    }

    private static void ValidateStepTypeRules(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        // TODO: implement
    }

    private static void ValidateNotesLength(
        QuestDefinition quest, ValidationContext ctx, List<ValidationError> errors)
    {
        // TODO: implement
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