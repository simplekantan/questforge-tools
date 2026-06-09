using Lumina;
using Lumina.Excel.Sheets;

namespace QuestForge.Tools.Trace.Coverage;

public sealed class QuestListGenerator
{
    public static List<QuestListEntry> Generate(string sqpackPath)
    {
        var gameData = new GameData(sqpackPath);
        var questSheet = gameData.GetExcelSheet<Lumina.Excel.Sheets.Quest>()
            ?? throw new InvalidOperationException("Failed to load Quest sheet from Lumina");
        var genreSheet = gameData.GetExcelSheet<JournalGenre>();
        var categorySheet = gameData.GetExcelSheet<JournalCategory>();

        var result = new List<QuestListEntry>();

        foreach (var quest in questSheet)
        {
            if (quest.RowId == 0) continue;
            var name = quest.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var exVersion = quest.Expansion.RowId;
            var expansion = exVersion switch
            {
                0 => "arr",
                1 => "heavensward",
                2 => "stormblood",
                3 => "shadowbringers",
                4 => "endwalker",
                5 => "dawntrail",
                _ => $"ex{exVersion}"
            };

            var category = "other";
            var genreId = quest.JournalGenre.RowId;
            if (genreId != 0 && genreSheet is not null)
            {
                var genre = genreSheet.GetRowOrDefault(genreId);
                if (genre is not null)
                {
                    var catId = genre.Value.JournalCategory.RowId;
                    if (categorySheet is not null)
                    {
                        var cat = categorySheet.GetRowOrDefault(catId);
                        if (cat is not null)
                        {
                            var catName = cat.Value.Name.ExtractText().ToLowerInvariant();
                            category = catName switch
                            {
                                _ when catName.Contains("main scenario") => "msq",
                                _ when catName.Contains("job quest") => "class",
                                _ when catName.Contains("class quest") => "class",
                                _ when catName.Contains("disciple of war") => "class",
                                _ when catName.Contains("disciple of magic") => "class",
                                _ when catName.Contains("disciple of the hand") => "class",
                                _ when catName.Contains("disciple of the land") => "class",
                                _ when catName.Contains("role quest") => "role",
                                _ when catName.Contains("seasonal") => "seasonal",
                                _ when catName.Contains("hildibrand") => "side",
                                _ when catName.Contains("crystal tower") => "side",
                                _ when catName.Contains("chronicles") => "side",
                                _ when catName.Contains("side") => "side",
                                _ => catName.Length > 0 ? catName : "other"
                            };
                        }
                    }
                }
            }

            var level = (int)quest.ClassJobLevel[0];

            result.Add(new QuestListEntry(
                Id: quest.RowId,
                Name: name,
                Expansion: expansion,
                Category: category,
                Level: level > 0 ? level : null));
        }

        return result;
    }
}

public sealed record QuestListEntry(
    uint Id,
    string Name,
    string Expansion,
    string Category,
    int? Level);
