using System.Text.Json;
using QuestForge.Adapters.Tracing;

namespace QuestForge.Tools.Trace.Analysis;

public sealed class QuestStateChangeAnalyzer
{
    public QuestStateChangeReport Analyze(IReadOnlyList<TraceEvent> events)
    {
        var runStart = events.OfType<RunStartEvent>().FirstOrDefault();
        uint? questId = runStart?.Data.QuestId;

        if (questId is null)
            return new QuestStateChangeReport(null, []);

        var changes = new List<QuestStateChange>();
        DecisionEvent? lastDecision = null;

        int? lastSequence = null;
        uint? lastFlags = null;
        string? lastVariables = null;

        foreach (var ev in events)
        {
            switch (ev)
            {
                case DecisionEvent d:
                    lastDecision = d;
                    break;

                case ObservationEvent obs:
                    if (!obs.Data.Value.HasValue) break;
                    var val = obs.Data.Value.Value;

                    if (obs.Data.Method == "GetQuestSequence" && QuestArgMatches(obs, questId.Value))
                    {
                        var parsed = ParseSequenceValue(val);
                        if (parsed is null) break;

                        if (lastSequence is null)
                        {
                            lastSequence = parsed.Value;
                            break;
                        }

                        if (parsed.Value == lastSequence.Value) break;

                        changes.Add(new QuestStateChange(
                            Seq: obs.Seq,
                            Kind: "sequence",
                            PreviousValue: lastSequence.Value.ToString(),
                            NewValue: parsed.Value.ToString(),
                            Detail: null,
                            AfterStepId: lastDecision?.Data.StepId,
                            AfterActionType: lastDecision?.Data.ActionType));

                        lastSequence = parsed.Value;
                    }
                    else if (obs.Data.Method == "GetQuestFlags" && QuestArgMatches(obs, questId.Value))
                    {
                        var parsed = ParseFlagsValue(val);
                        if (parsed is null) break;

                        if (lastFlags is null)
                        {
                            lastFlags = parsed.Value;
                            break;
                        }

                        if (parsed.Value == lastFlags.Value) break;

                        changes.Add(new QuestStateChange(
                            Seq: obs.Seq,
                            Kind: "flags",
                            PreviousValue: lastFlags.Value.ToString(),
                            NewValue: parsed.Value.ToString(),
                            Detail: ComputeFlagsDiff(lastFlags.Value, parsed.Value),
                            AfterStepId: lastDecision?.Data.StepId,
                            AfterActionType: lastDecision?.Data.ActionType));

                        lastFlags = parsed.Value;
                    }
                    else if (obs.Data.Method == "GetQuestVariables" && QuestArgMatches(obs, questId.Value))
                    {
                        var parsed = ParseVariablesValue(val);
                        if (parsed is null) break;

                        if (lastVariables is null)
                        {
                            lastVariables = parsed;
                            break;
                        }

                        if (parsed == lastVariables) break;

                        changes.Add(new QuestStateChange(
                            Seq: obs.Seq,
                            Kind: "variables",
                            PreviousValue: lastVariables,
                            NewValue: parsed,
                            Detail: null,
                            AfterStepId: lastDecision?.Data.StepId,
                            AfterActionType: lastDecision?.Data.ActionType));

                        lastVariables = parsed;
                    }
                    break;
            }
        }

        return new QuestStateChangeReport(questId, changes);
    }

    private static bool QuestArgMatches(ObservationEvent ev, uint questId)
    {
        if (!ev.Data.Argument.HasValue) return false;
        var arg = ev.Data.Argument.Value;

        if (arg.ValueKind == JsonValueKind.Number)
        {
            try { return arg.GetUInt32() == questId; } catch { return false; }
        }

        if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("value", out var v))
        {
            try { return v.GetUInt32() == questId; } catch { return false; }
        }

        return false;
    }

    private static int? ParseSequenceValue(JsonElement val)
    {
        if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var n))
            return n;

        if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var wrapped)
            && wrapped.ValueKind == JsonValueKind.Number && wrapped.TryGetInt32(out var n2))
            return n2;

        return null;
    }

    private static uint? ParseFlagsValue(JsonElement val)
    {
        if (val.ValueKind == JsonValueKind.Number && val.TryGetUInt32(out var f))
            return f;

        if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var wrapped)
            && wrapped.ValueKind == JsonValueKind.Number && wrapped.TryGetUInt32(out var f2))
            return f2;

        return null;
    }

    private static string? ParseVariablesValue(JsonElement val)
    {
        JsonElement arrayEl;
        if (val.ValueKind == JsonValueKind.Array)
            arrayEl = val;
        else if (val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out var wrappedArr)
            && wrappedArr.ValueKind == JsonValueKind.Array)
            arrayEl = wrappedArr;
        else
            return null;

        var parts = new List<int>();
        foreach (var el in arrayEl.EnumerateArray())
        {
            try { parts.Add(el.GetInt32()); } catch { parts.Add(0); }
        }

        return "[" + string.Join(",", parts) + "]";
    }

    private static string? ComputeFlagsDiff(uint oldFlags, uint newFlags)
    {
        var set = newFlags & ~oldFlags;
        var cleared = oldFlags & ~newFlags;
        var parts = new List<string>();
        if (set != 0) parts.Add(FormatBits(set, "set"));
        if (cleared != 0) parts.Add(FormatBits(cleared, "cleared"));
        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static string FormatBits(uint mask, string verb)
    {
        var bits = new List<int>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1u << i)) != 0) bits.Add(i);
        var label = bits.Count == 1 ? "bit" : "bits";
        return $"{label} {string.Join(",", bits)} {verb}";
    }
}
