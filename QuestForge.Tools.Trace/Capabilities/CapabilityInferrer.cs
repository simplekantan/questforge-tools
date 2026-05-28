using System.Text.RegularExpressions;
using QuestForge.Schema;

namespace QuestForge.Tools.Trace.Capabilities;

/// <summary>
/// Infers the capability tags exercised by a <see cref="QuestDefinition"/>.
/// Tags follow the pattern <c>step:&lt;discriminator&gt;</c>, <c>predicate:&lt;name&gt;</c>,
/// and <c>engine:&lt;feature&gt;</c>.
/// Returns a sorted, de-duplicated list.
/// </summary>
public static class CapabilityInferrer
{
    private static readonly Dictionary<Type, string> StepCapabilities = new()
    {
        [typeof(TravelStep)]              = "step:travel",
        [typeof(TalkStep)]                = "step:talk",
        [typeof(AcceptStep)]              = "step:accept",
        [typeof(TurnInStep)]              = "step:turn-in",
        [typeof(CombatStep)]              = "step:combat",
        [typeof(DutyStep)]                = "step:duty",
        [typeof(CutsceneStep)]            = "step:cutscene",
        [typeof(UseItemStep)]             = "step:use-item",
        [typeof(UseActionStep)]           = "step:use-action",
        [typeof(UseEmoteStep)]            = "step:use-emote",
        [typeof(SayChatMessageStep)]      = "step:say-chat-message",
        [typeof(EquipGearForQuestStep)]   = "step:equip-gear-for-quest",
        [typeof(EquipBestGearStep)]       = "step:equip-best-gear",
        [typeof(ChangeJobStep)]           = "step:change-job",
        [typeof(MinigameStep)]            = "step:minigame",
        [typeof(AwaitUserStep)]           = "step:await-user",
        [typeof(BranchStep)]              = "step:branch",
        [typeof(FragmentStep)]            = "step:fragment",
        [typeof(InteractObjectStep)]      = "step:interact-object",
        [typeof(PickupItemStep)]          = "step:pickup-item",
        [typeof(AttunementStep)]          = "step:attune",
        [typeof(HandOverItemStep)]        = "step:hand-over-item",
        [typeof(PurchaseItemStep)]        = "step:purchase-item",
    };

    /// <summary>
    /// Walk every step in every sequence and produce a sorted, de-duplicated capability list.
    /// </summary>
    public static IReadOnlyList<string> Infer(QuestDefinition quest)
    {
        var caps = new HashSet<string>();
        bool hasBranch = false;
        bool hasFragment = false;

        foreach (var seq in quest.Sequences)
        {
            foreach (var step in seq.Steps)
            {
                // Emit step capability
                var stepType = step.GetType();
                if (StepCapabilities.TryGetValue(stepType, out var cap))
                    caps.Add(cap);

                // DutyStep with kind=="spd" also emits step:spd
                if (step is DutyStep duty && duty.Kind == "spd")
                    caps.Add("step:spd");

                // Track engine-level features
                if (step is BranchStep branch)
                {
                    hasBranch = true;
                    // Walk BranchCase.When predicates
                    foreach (var bc in branch.Branches)
                    {
                        if (!string.IsNullOrEmpty(bc.When))
                        {
                            foreach (var fn in ExtractFunctionNames(bc.When))
                                caps.Add($"predicate:{fn}");
                        }
                        // Also walk steps inside branches
                        foreach (var innerStep in (bc.Steps ?? []))
                            WalkStepExpects(innerStep, caps);
                    }
                }

                if (step is FragmentStep)
                    hasFragment = true;

                // Walk Expect and SkipIf recursively
                WalkExpect(step.Expect, caps);
                WalkExpect(step.SkipIf, caps);
            }
        }

        if (hasBranch)  caps.Add("engine:branching");
        if (hasFragment) caps.Add("engine:fragments");

        var sorted = caps.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static void WalkStepExpects(Step step, HashSet<string> caps)
    {
        WalkExpect(step.Expect, caps);
        WalkExpect(step.SkipIf, caps);
    }

    private static void WalkExpect(ExpectValue? ev, HashSet<string> caps)
    {
        switch (ev)
        {
            case PredicateExpect p:
                foreach (var fn in ExtractFunctionNames(p.Predicate))
                    caps.Add($"predicate:{fn}");
                break;
            case AllExpect a:
                foreach (var pred in a.All)
                    foreach (var fn in ExtractFunctionNames(pred))
                        caps.Add($"predicate:{fn}");
                break;
            case AnyExpect any:
                foreach (var pred in any.Any)
                    foreach (var fn in ExtractFunctionNames(pred))
                        caps.Add($"predicate:{fn}");
                break;
        }
    }

    // Matches every identifier immediately followed by '(' — i.e., every function call name.
    private static readonly Regex FunctionCallPattern =
        new(@"\b([a-zA-Z]\w*)(?=\()", RegexOptions.Compiled);

    private static IEnumerable<string> ExtractFunctionNames(string predicate)
        => FunctionCallPattern.Matches(predicate).Select(m => m.Value);
}
