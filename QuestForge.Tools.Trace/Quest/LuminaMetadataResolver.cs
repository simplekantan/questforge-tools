using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace QuestForge.Tools.Trace.Quest;

public sealed class LuminaMetadataResolver : IQuestMetadataResolver
{
    private static readonly string[] ExpansionMap = ["arr", "hw", "sb", "shb", "ew", "dt"];

    private static readonly Dictionary<uint, string> JournalCategoryMap = new()
    {
        [1] = "msq",
        [2] = "side",
        [3] = "side",
        [4] = "side",
        [5] = "side",
    };

    private readonly GameData _gameData;

    public LuminaMetadataResolver(string sqpackPath)
    {
        _gameData = new GameData(sqpackPath);
    }

    public QuestMetadata? ResolveQuest(uint questId)
    {
        var sheet = _gameData.GetExcelSheet<Lumina.Excel.Sheets.Quest>();
        if (sheet == null) return null;

        var row = sheet.GetRowOrDefault(questId);
        if (row == null) return null;

        var quest = row.Value;
        var name = quest.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var exVersion = quest.Expansion.RowId;
        var expansion = exVersion < (uint)ExpansionMap.Length
            ? ExpansionMap[exVersion]
            : "unknown";

        var category = ResolveCategory(quest);

        var level = (int)quest.ClassJobLevel[0];
        int? minLevel = level > 0 ? level : null;

        var requiredJob = ResolveSingleJob(quest.ClassJobCategory0.RowId);

        var prereqs = new List<uint>();
        for (var i = 0; i < quest.PreviousQuest.Count; i++)
        {
            var pqId = quest.PreviousQuest[i].RowId;
            if (pqId != 0) prereqs.Add(pqId);
        }

        return new QuestMetadata(name, expansion, category, minLevel, requiredJob, prereqs.ToArray());
    }

    public string? ResolveNpcName(uint npcId)
    {
        var sheet = _gameData.GetExcelSheet<ENpcResident>();
        if (sheet == null) return null;
        var row = sheet.GetRowOrDefault(npcId);
        if (row == null) return null;
        var name = row.Value.Singular.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public string? ResolveZoneName(uint zoneId)
    {
        var sheet = _gameData.GetExcelSheet<TerritoryType>();
        if (sheet == null) return null;
        var row = sheet.GetRowOrDefault(zoneId);
        if (row == null) return null;
        var name = row.Value.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public string? ResolveJobAbbreviation(uint jobId)
    {
        var sheet = _gameData.GetExcelSheet<ClassJob>();
        if (sheet == null) return null;
        var row = sheet.GetRowOrDefault(jobId);
        if (row == null) return null;
        var abbr = row.Value.Abbreviation.ExtractText();
        return string.IsNullOrWhiteSpace(abbr) ? null : abbr;
    }

    private string ResolveCategory(Lumina.Excel.Sheets.Quest quest)
    {
        var genreId = quest.JournalGenre.RowId;
        if (genreId == 0) return "side";

        var genreSheet = _gameData.GetExcelSheet<JournalGenre>();
        if (genreSheet == null) return "side";

        var genre = genreSheet.GetRowOrDefault(genreId);
        if (genre == null) return "side";

        var catId = genre.Value.JournalCategory.RowId;
        return JournalCategoryMap.GetValueOrDefault(catId, "side");
    }

    private string? ResolveSingleJob(uint classJobCategoryId)
    {
        if (classJobCategoryId == 0) return null;

        var sheet = _gameData.GetExcelSheet<ClassJobCategory>();
        if (sheet == null) return null;

        var row = sheet.GetRowOrDefault(classJobCategoryId);
        if (row == null) return null;

        var cat = row.Value;
        var jobSheet = _gameData.GetExcelSheet<ClassJob>();
        if (jobSheet == null) return null;

        var matchedJobs = new List<string>();
        foreach (var job in jobSheet)
        {
            if (job.RowId == 0) continue;
            var abbr = job.Abbreviation.ExtractText();
            if (string.IsNullOrWhiteSpace(abbr)) continue;
            if (IsJobInCategory(cat, abbr))
                matchedJobs.Add(abbr);
        }

        return matchedJobs.Count == 1 ? matchedJobs[0] : null;
    }

    private static bool IsJobInCategory(ClassJobCategory cat, string abbr) => abbr switch
    {
        "GLA" => cat.GLA, "PLD" => cat.PLD, "MRD" => cat.MRD, "WAR" => cat.WAR,
        "DRK" => cat.DRK, "GNB" => cat.GNB, "CNJ" => cat.CNJ, "WHM" => cat.WHM,
        "SCH" => cat.SCH, "AST" => cat.AST, "SGE" => cat.SGE, "PGL" => cat.PGL,
        "MNK" => cat.MNK, "LNC" => cat.LNC, "DRG" => cat.DRG, "ROG" => cat.ROG,
        "NIN" => cat.NIN, "ARC" => cat.ARC, "BRD" => cat.BRD, "THM" => cat.THM,
        "BLM" => cat.BLM, "ACN" => cat.ACN, "SMN" => cat.SMN, "SAM" => cat.SAM,
        "RDM" => cat.RDM, "BLU" => cat.BLU, "MCH" => cat.MCH, "DNC" => cat.DNC,
        "RPR" => cat.RPR, "VPR" => cat.VPR, "PCT" => cat.PCT,
        "CRP" => cat.CRP, "BSM" => cat.BSM, "ARM" => cat.ARM, "GSM" => cat.GSM,
        "LTW" => cat.LTW, "WVR" => cat.WVR, "ALC" => cat.ALC, "CUL" => cat.CUL,
        "MIN" => cat.MIN, "BTN" => cat.BTN, "FSH" => cat.FSH, "ADV" => cat.ADV,
        _ => false
    };
}
