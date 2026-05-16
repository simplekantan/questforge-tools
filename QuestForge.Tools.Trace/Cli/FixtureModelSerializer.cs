using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestForge.Tools.Trace.Fixture;

namespace QuestForge.Tools.Trace.Cli;

public static class FixtureModelSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(FixtureModel fixture)
        => JsonSerializer.Serialize(fixture, Options);
}
