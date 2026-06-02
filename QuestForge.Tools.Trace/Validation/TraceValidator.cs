using System.Text.Json;

namespace QuestForge.Tools.Trace.Validation;

public sealed class TraceValidator
{
    public TraceValidationResult Validate(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        return Validate(lines);
    }

    public TraceValidationResult Validate(IReadOnlyList<string> lines)
    {
        var errors   = new List<TraceValidationIssue>();
        var warnings = new List<TraceValidationIssue>();

        int     expectedSeq      = 0;
        string? firstRunId       = null;
        long    lastTs           = long.MinValue;
        bool    sawRunStart      = false;
        bool    sawRunEnd        = false;
        int     runEndEventIndex = -1;
        int     totalEvents      = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            int lineNum = i + 1;
            var line    = lines[i];

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // TV2 / TV8: parse JSON; skip all field checks on malformed lines
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                errors.Add(new TraceValidationIssue("TV-E001", $"malformed JSON: {ex.Message}", lineNum));
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;

                // TV-E008: check type field presence
                bool hasType = root.TryGetProperty("type", out var typeProp);
                string? typeValue = hasType ? typeProp.GetString() : null;

                if (!hasType)
                    errors.Add(new TraceValidationIssue("TV-E008", "missing \"type\" field", lineNum));

                // TV9: check v field (TV-E002, TV-E003)
                if (!root.TryGetProperty("v", out var vProp))
                {
                    errors.Add(new TraceValidationIssue("TV-E002", "missing \"v\" field", lineNum));
                }
                else if (vProp.ValueKind != JsonValueKind.Number || !vProp.TryGetInt32(out var vVal))
                {
                    errors.Add(new TraceValidationIssue("TV-E002", "non-integer \"v\" field", lineNum));
                }
                else if (vVal != 1)
                {
                    errors.Add(new TraceValidationIssue("TV-E003", $"\"v\" is {vVal}, expected 1", lineNum));
                }

                // TV10: check seq field (TV-E004)
                bool seqOk = false;
                if (!root.TryGetProperty("seq", out var seqProp))
                {
                    errors.Add(new TraceValidationIssue("TV-E004", $"missing \"seq\" field; expected {expectedSeq}", lineNum));
                }
                else if (seqProp.ValueKind != JsonValueKind.Number || !seqProp.TryGetInt32(out var seqVal))
                {
                    errors.Add(new TraceValidationIssue("TV-E004", $"non-integer \"seq\" field; expected {expectedSeq}", lineNum));
                }
                else if (seqVal != expectedSeq)
                {
                    errors.Add(new TraceValidationIssue("TV-E004", $"seq is {seqVal}, expected {expectedSeq}", lineNum));
                    expectedSeq = seqVal + 1; // resync per TV10
                }
                else
                {
                    expectedSeq = seqVal + 1;
                    seqOk = true;
                }

                // TV-E005: check runId consistency
                if (root.TryGetProperty("runId", out var runIdProp))
                {
                    var runId = runIdProp.GetString();
                    if (firstRunId is null)
                        firstRunId = runId;
                    else if (runId != firstRunId)
                        errors.Add(new TraceValidationIssue("TV-E005", $"runId \"{runId}\" does not match first runId \"{firstRunId}\"", lineNum));
                }

                // TV-E006: run.start / seq-0 rules
                // Rule A: if this is seq 0 (expected before increment) and type != run.start
                bool wasSeq0 = seqOk && (expectedSeq - 1 == 0);
                if (wasSeq0 && typeValue != "run.start")
                    errors.Add(new TraceValidationIssue("TV-E006", "seq 0 event must be type \"run.start\"", lineNum));

                // Rule B: if type is run.start and it's not at seq 0
                if (typeValue == "run.start")
                {
                    sawRunStart = true;
                    if (!wasSeq0)
                        errors.Add(new TraceValidationIssue("TV-E006", "\"run.start\" must be at seq 0", lineNum));
                }

                // Track run.end position
                if (typeValue == "run.end")
                {
                    sawRunEnd        = true;
                    runEndEventIndex = totalEvents; // 0-based index of this event
                }

                // TV-W001: ts monotonicity
                if (root.TryGetProperty("ts", out var tsProp) && tsProp.TryGetInt64(out var tsVal))
                {
                    if (tsVal < lastTs)
                        warnings.Add(new TraceValidationIssue("TV-W001", $"ts decreased from {lastTs} to {tsVal}", lineNum));
                    lastTs = tsVal;
                }

                totalEvents++;
            }
        }

        // Post-loop checks
        if (totalEvents == 0)
        {
            warnings.Add(new TraceValidationIssue("TV-W003", "empty trace (no events)"));
        }
        else
        {
            // TV-E006: no run.start seen and seq 0 didn't error yet
            // (handled inline above — if first event is not run.start, error already emitted)

            // TV-E007: run.end exists but is not the last event
            if (sawRunEnd && runEndEventIndex != totalEvents - 1)
                errors.Add(new TraceValidationIssue("TV-E007", $"\"run.end\" is not the last event (event index {runEndEventIndex}, last index {totalEvents - 1})"));

            // TV-W002: no run.end in non-empty trace
            if (!sawRunEnd)
                warnings.Add(new TraceValidationIssue("TV-W002", "missing \"run.end\" (trace may be from aborted run)"));

            // TV-E006 post-loop: no run.start seen at all
            // This is already covered by the inline check on seq=0 line.
            // But if there were NO events at seq 0 (e.g. trace starts at seq 1), the inline
            // check won't fire. We need a separate post-loop check.
            if (!sawRunStart && totalEvents > 0)
            {
                // Check if we already emitted E006 for the first event (seq 0 case)
                // If the first event had seq != 0, we got E004 but not E006 for "no run.start"
                // The plan says: "seq 0 is not run.start" → E006. If there's no seq 0 at all,
                // we still need to flag it.
                bool alreadyHasE006ForFirstEvent = errors.Any(e => e.Code == "TV-E006");
                if (!alreadyHasE006ForFirstEvent)
                    errors.Add(new TraceValidationIssue("TV-E006", "\"run.start\" event is missing from trace"));
            }
        }

        return new TraceValidationResult(errors, warnings);
    }
}
