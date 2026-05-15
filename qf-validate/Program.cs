using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestForge.Schema;
using QuestForge.Tools.Validator;

// ---- Argument parsing -------------------------------------------------------

var rootDir       = ".";
var format        = "text";
var failOnWarning = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--format" when i + 1 < args.Length:
            format = args[++i];
            break;
        case "--fail-on-warning":
            failOnWarning = true;
            break;
        default:
            if (!args[i].StartsWith("--"))
                rootDir = args[i];
            break;
    }
}

rootDir = Path.GetFullPath(rootDir);

if (!Directory.Exists(rootDir))
{
    Console.Error.WriteLine($"qf-validate: directory not found: {rootDir}");
    return 1;
}

// ---- Load fragments ---------------------------------------------------------

var allErrors    = new List<ValidationError>();
var fragments    = new List<FragmentDefinition>();
var fileLineMap  = new Dictionary<string, string[]>(StringComparer.Ordinal);
var fragmentsDir = Path.Combine(rootDir, "fragments");

if (Directory.Exists(fragmentsDir))
{
    foreach (var filePath in Directory.EnumerateFiles(fragmentsDir, "*.json", SearchOption.AllDirectories).Order())
    {
        var rel = Rel(rootDir, filePath);
        try
        {
            var json     = File.ReadAllText(filePath);
            fileLineMap[rel] = json.Split('\n');
            var fragment = JsonSerializer.Deserialize<FragmentDefinition>(json, QuestForgeJsonContext.QuestFileOptions);
            if (fragment is not null)
                fragments.Add(fragment);
        }
        catch (JsonException ex)
        {
            allErrors.Add(new ValidationError(
                "structural/json-parse-error",
                $"JSON parse failure: {ex.Message}",
                rel,
                ex.LineNumber.HasValue ? $"line:{ex.LineNumber}" : "root"));
        }
        catch (IOException ex)
        {
            allErrors.Add(new ValidationError(
                "structural/file-read-error",
                $"Could not read file: {ex.Message}",
                rel, "root"));
        }
    }
}

// ---- Validate quests --------------------------------------------------------

var registry  = new InMemoryFragmentRegistry(fragments);
var pipeline  = new ValidatorPipeline([new StructuralValidator(registry), new PredicateValidator(registry)]);
var questsDir = Path.Combine(rootDir, "quests");

if (Directory.Exists(questsDir))
{
    foreach (var filePath in Directory.EnumerateFiles(questsDir, "*.json", SearchOption.AllDirectories).Order())
    {
        var rel = Rel(rootDir, filePath);

        // Read lines for line-number resolution (heuristic text scan)
        try { fileLineMap[rel] = File.ReadAllLines(filePath); }
        catch (IOException) { /* ignore; line numbers will be null for this file */ }

        var (quest, loadErrors) = QuestLoader.LoadQuest(filePath);

        if (loadErrors.Count > 0)
        {
            allErrors.AddRange(loadErrors.Select(e => e with { FilePath = rel }));
            continue;
        }

        var ctx = new ValidationContext(rel, quest!.Id);
        allErrors.AddRange(pipeline.Validate(quest, ctx));
    }
}

// ---- Output -----------------------------------------------------------------

var errorCount   = allErrors.Count(e => e.Severity == Severity.Error);
var warningCount = allErrors.Count(e => e.Severity == Severity.Warning);

if (format == "json")
    EmitJson(allErrors, errorCount, warningCount, fileLineMap);
else
    EmitText(allErrors, errorCount, warningCount);

// ---- Exit code --------------------------------------------------------------

if (errorCount > 0)   return 1;
if (warningCount > 0 && failOnWarning) return 2;
return 0;

// ---- Helpers ----------------------------------------------------------------

static string Rel(string root, string path) =>
    Path.GetRelativePath(root, path).Replace('\\', '/');

// Heuristic: find the 1-based line number for an error by searching for its
// step ID or sequence number in the raw file text. Imprecise but avoids a
// full re-parse. Replaced by exact positions once issue #N is resolved.
static int? ResolveLineNumber(ValidationError err, Dictionary<string, string[]> lineMap)
{
    if (!lineMap.TryGetValue(err.FilePath, out var lines)) return null;

    // Best match: step ID — unique within a file
    if (err.StepId is not null)
    {
        var pattern = $"\"{err.StepId}\"";
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(pattern, StringComparison.Ordinal))
                return i + 1;
    }

    // Fallback: sequence number
    var loc = err.Location;
    if (loc.StartsWith("seq:", StringComparison.Ordinal))
    {
        var seqToken = loc.Split('/')[0]; // "seq:N"
        if (seqToken.Length > 4 && int.TryParse(seqToken[4..], out var seqNum))
        {
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains($"\"sequence\": {seqNum}", StringComparison.Ordinal) ||
                    lines[i].Contains($"\"sequence\":{seqNum}", StringComparison.Ordinal))
                    return i + 1;
        }
    }

    // Fallback: chain key
    if (loc.StartsWith("chain", StringComparison.Ordinal))
    {
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains("\"chain\"", StringComparison.Ordinal))
                return i + 1;
    }

    return null;
}

static void EmitText(IReadOnlyList<ValidationError> errors, int errorCount, int warningCount)
{
    foreach (var e in errors)
    {
        Console.WriteLine($"{e.Severity.ToString().ToUpperInvariant()}  {e.FilePath}  {e.Location}");
        Console.WriteLine($"  [{e.Code}] {e.Message}");
        Console.WriteLine();
    }

    Console.WriteLine(errorCount > 0
        ? $"{errorCount} error(s), {warningCount} warning(s). Validation failed."
        : warningCount > 0
            ? $"0 error(s), {warningCount} warning(s). Validation passed with warnings."
            : "Validation passed.");
}

static void EmitJson(
    IReadOnlyList<ValidationError> errors, int errorCount, int warningCount,
    Dictionary<string, string[]> lineMap)
{
    var output = new
    {
        results = errors.Select(e => new
        {
            code     = e.Code,
            message  = e.Message,
            file     = e.FilePath,
            location = e.Location,
            stepId   = e.StepId,
            severity = e.Severity.ToString().ToLowerInvariant(),
            line     = ResolveLineNumber(e, lineMap)
        }),
        summary = new { errors = errorCount, warnings = warningCount }
    };

    var opts = new JsonSerializerOptions
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    Console.WriteLine(JsonSerializer.Serialize(output, opts));
}