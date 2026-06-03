using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuestForge.Tools.Trace.Redaction;

public sealed class TraceRedactor
{
    internal static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.Ordinal)
    {
        "characterName",
        "worldName",
        "serverId",
        "contentId",
        "accountId",
        "friendList",
        "fcName",
        "partyMembers",
        "retainerName",
        "chatContent",
        "chatMessage",
    };

    private static readonly JsonSerializerOptions ReserializeOptions = new()
    {
        WriteIndented = false,
    };

    public RedactionReport RedactFile(string inputPath, TextWriter output)
    {
        var lines = File.ReadAllLines(inputPath);
        return Redact(lines, output);
    }

    public RedactionReport Redact(IReadOnlyList<string> lines, TextWriter output)
    {
        int totalLines = 0;
        int wallClockStripped = 0;
        int alreadyRedacted = 0;
        var excludedHits = new List<ExcludedFieldHit>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int lineNumber = i + 1;

            if (string.IsNullOrWhiteSpace(line))
            {
                output.Write(line);
                output.Write('\n');
                continue;
            }

            totalLines++;

            JsonDocument? doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                output.Write(line);
                output.Write('\n');
                continue;
            }

            bool isRunStart;
            using (doc)
            {
                ScanForExcludedKeys(doc.RootElement, lineNumber, excludedHits);
                isRunStart = doc.RootElement.TryGetProperty("type", out var typeProp)
                    && typeProp.GetString() == "run.start";
            }

            if (!isRunStart)
            {
                output.Write(line);
                output.Write('\n');
                continue;
            }

            var node = JsonNode.Parse(line);
            if (node is JsonObject rootObj
                && rootObj["data"] is JsonObject dataObj
                && dataObj.ContainsKey("wallClockUtc"))
            {
                var wallClock = dataObj["wallClockUtc"];
                if (wallClock is null || wallClock.GetValueKind() == JsonValueKind.Null)
                {
                    alreadyRedacted++;
                    output.Write(line);
                    output.Write('\n');
                }
                else
                {
                    wallClockStripped++;
                    dataObj["wallClockUtc"] = null;
                    output.Write(rootObj.ToJsonString(ReserializeOptions));
                    output.Write('\n');
                }
            }
            else
            {
                output.Write(line);
                output.Write('\n');
            }
        }

        return new RedactionReport(totalLines, wallClockStripped, alreadyRedacted, excludedHits);
    }

    private static void ScanForExcludedKeys(
        JsonElement element,
        int lineNumber,
        List<ExcludedFieldHit> hits)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (ExcludedPropertyNames.Contains(prop.Name))
                    hits.Add(new ExcludedFieldHit(prop.Name, lineNumber));
                ScanForExcludedKeys(prop.Value, lineNumber, hits);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ScanForExcludedKeys(item, lineNumber, hits);
        }
    }
}
