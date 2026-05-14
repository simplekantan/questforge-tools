using System.Text.Json;
using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

public static class QuestLoader
{
    public static (QuestDefinition? Quest, IReadOnlyList<ValidationError> Errors)
        LoadQuest(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var quest = JsonSerializer.Deserialize<QuestDefinition>(
                json, QuestForgeJsonContext.QuestFileOptions);

            if (quest is null)
                return (null, [new ValidationError(
                    "structural/json-null",
                    "File deserialized to null.",
                    filePath, "root")]);

            return (quest, []);
        }
        catch (JsonException ex)
        {
            return (null, [new ValidationError(
                "structural/json-parse-error",
                $"JSON parse failure: {ex.Message}",
                filePath,
                ex.LineNumber.HasValue ? $"line:{ex.LineNumber}" : "root")]);
        }
        catch (IOException ex)
        {
            return (null, [new ValidationError(
                "structural/file-read-error",
                $"Could not read file: {ex.Message}",
                filePath, "root")]);
        }
    }
}