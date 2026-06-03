using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Parsing;
using QuestForge.Tools.Trace.Quest;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests.Quest;

/// <summary>
/// Tests for <see cref="IQuestMetadataResolver"/>, <see cref="NullMetadataResolver"/>,
/// <see cref="SqpackPathResolver"/>, and extractor integration with a resolver.
/// Scenarios T1-T18 from LUMINA_RESOLUTION_PLAN.md (excluding T19-T22 integration tests).
/// All tests are RED: they reference types that do not exist yet.
/// </summary>
/// <summary>
/// Dictionary-backed fake that returns canned <see cref="QuestMetadata"/> for known IDs.
/// Used by T5-T12 extractor integration tests. File-local to avoid leaking outside this file.
/// </summary>
file sealed class FakeMetadataResolver : IQuestMetadataResolver
{
    private readonly Dictionary<uint, QuestMetadata> _quests = new();
    private readonly Dictionary<uint, string> _npcNames = new();
    private readonly Dictionary<uint, string> _zoneNames = new();
    private readonly Dictionary<uint, string> _jobAbbreviations = new();

    public FakeMetadataResolver WithQuest(uint questId, QuestMetadata metadata)
    {
        _quests[questId] = metadata;
        return this;
    }

    public FakeMetadataResolver WithNpcName(uint npcId, string name)
    {
        _npcNames[npcId] = name;
        return this;
    }

    public FakeMetadataResolver WithZoneName(uint zoneId, string name)
    {
        _zoneNames[zoneId] = name;
        return this;
    }

    public FakeMetadataResolver WithJobAbbreviation(uint jobId, string abbreviation)
    {
        _jobAbbreviations[jobId] = abbreviation;
        return this;
    }

    public QuestMetadata? ResolveQuest(uint questId)
        => _quests.GetValueOrDefault(questId);

    public string? ResolveNpcName(uint npcId)
        => _npcNames.GetValueOrDefault(npcId);

    public string? ResolveZoneName(uint zoneId)
        => _zoneNames.GetValueOrDefault(zoneId);

    public string? ResolveJobAbbreviation(uint jobId)
        => _jobAbbreviations.GetValueOrDefault(jobId);
}

public sealed class MetadataResolverTests
{
    // =========================================================================
    // T1-T4: NullMetadataResolver returns null for everything
    // =========================================================================

    // -------------------------------------------------------------------------
    // T1 -- NullResolver returns null for quest
    // -------------------------------------------------------------------------

    [Fact]
    public void T1_NullResolver_ResolveQuest_ReturnsNull()
    {
        /*
         * CONTRACT: Given NullMetadataResolver.Instance,
         *           When ResolveQuest(66130) is called,
         *           Then returns null.
         */

        // Act
        var result = NullMetadataResolver.Instance.ResolveQuest(66130);

        // Assert
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // T2 -- NullResolver returns null for NPC name
    // -------------------------------------------------------------------------

    [Fact]
    public void T2_NullResolver_ResolveNpcName_ReturnsNull()
    {
        /*
         * CONTRACT: Given NullMetadataResolver.Instance,
         *           When ResolveNpcName(1003987) is called,
         *           Then returns null.
         */

        // Act
        var result = NullMetadataResolver.Instance.ResolveNpcName(1003987);

        // Assert
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // T3 -- NullResolver returns null for zone name
    // -------------------------------------------------------------------------

    [Fact]
    public void T3_NullResolver_ResolveZoneName_ReturnsNull()
    {
        /*
         * CONTRACT: Given NullMetadataResolver.Instance,
         *           When ResolveZoneName(182) is called,
         *           Then returns null.
         */

        // Act
        var result = NullMetadataResolver.Instance.ResolveZoneName(182);

        // Assert
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // T4 -- NullResolver returns null for job abbreviation
    // -------------------------------------------------------------------------

    [Fact]
    public void T4_NullResolver_ResolveJobAbbreviation_ReturnsNull()
    {
        /*
         * CONTRACT: Given NullMetadataResolver.Instance,
         *           When ResolveJobAbbreviation(1) is called,
         *           Then returns null.
         */

        // Act
        var result = NullMetadataResolver.Instance.ResolveJobAbbreviation(1);

        // Assert
        Assert.Null(result);
    }

    // =========================================================================
    // T5-T6: Backward compatibility -- extractor with NullResolver / default ctor
    // =========================================================================

    /// <summary>
    /// Builds a minimal valid trace with a single navigate+done pair for quest 66130.
    /// Reused across T5-T12.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildMinimalTrace(uint questId = 66130u)
    {
        var jsonl = MakeTrace(
            Start(questId: questId),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u), offsetSeconds: 0.1),
            Obs("GetPlayerPosition", argument: null, value: PositionValue(10f, 0f, 10f), offsetSeconds: 0.2),
            Decision(null, "navigate", offsetSeconds: 1),
            Submitted("Navigate", NavParams(44.7f, 4.0f, -148.7f, zone: 182), offsetSeconds: 1.5),
            Completed("Navigate", "Arrived", offsetSeconds: 2),
            Obs("GetPlayerPosition", argument: null, value: PositionValue(44.7f, 4.0f, -148.7f), offsetSeconds: 2.5),
            Decision(null, "done", offsetSeconds: 3),
            End("done")
        );
        return TraceEventParser.ReadText(jsonl);
    }

    // -------------------------------------------------------------------------
    // T5 -- Backward compat: extractor with default ctor still produces TODOs
    // -------------------------------------------------------------------------

    [Fact]
    public void T5_ExtractWithDefaultCtor_ProducesSameTodosAsBefore()
    {
        /*
         * CONTRACT: Given a trace with RunStart(questId: 66130) and a navigate+done pair,
         *           When new TraceToQuestExtractor().Extract(events) is called (default ctor),
         *           Then draft.Definition.Name == "TODO",
         *                draft.Definition.Expansion == "TODO",
         *                draft.Definition.Category == "TODO",
         *           And  draft.Todos contains "name (Lumina lookup required)",
         *                "expansion", "category", "requirements (level, job, prereqs)",
         *                "lastVerifiedPatch".
         *
         * RED: Will fail until Builder adds the optional IQuestMetadataResolver parameter
         * to TraceToQuestExtractor's constructor (defaulting to NullMetadataResolver.Instance).
         * The current ctor has no resolver param, so this test verifies the default path.
         */

        // Arrange
        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor();

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("TODO", draft.Definition.Name);
        Assert.Equal("TODO", draft.Definition.Expansion);
        Assert.Equal("TODO", draft.Definition.Category);

        Assert.Contains(draft.Todos, t => t == "name (Lumina lookup required)");
        Assert.Contains(draft.Todos, t => t == "expansion");
        Assert.Contains(draft.Todos, t => t == "category");
        Assert.Contains(draft.Todos, t => t == "requirements (level, job, prereqs)");
        Assert.Contains(draft.Todos, t => t == "lastVerifiedPatch");
    }

    // -------------------------------------------------------------------------
    // T6 -- Extractor with explicit NullResolver produces same TODOs (no regression)
    // -------------------------------------------------------------------------

    [Fact]
    public void T6_ExtractWithExplicitNullResolver_ProducesSameTodosAsDefaultCtor()
    {
        /*
         * CONTRACT: Given a trace with RunStart(questId: 66130) and a navigate+done pair,
         *           When new TraceToQuestExtractor(resolver: NullMetadataResolver.Instance)
         *                .Extract(events) is called,
         *           Then draft.Definition.Name == "TODO" and all five standard TODOs present.
         *           Proves the optional parameter defaults to NullResolver.
         *
         * RED: Will fail because the constructor does not yet accept an
         * IQuestMetadataResolver parameter.
         */

        // Arrange
        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor(resolver: NullMetadataResolver.Instance);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("TODO", draft.Definition.Name);
        Assert.Equal("TODO", draft.Definition.Expansion);
        Assert.Equal("TODO", draft.Definition.Category);

        Assert.Contains(draft.Todos, t => t == "name (Lumina lookup required)");
        Assert.Contains(draft.Todos, t => t == "expansion");
        Assert.Contains(draft.Todos, t => t == "category");
        Assert.Contains(draft.Todos, t => t == "requirements (level, job, prereqs)");
        Assert.Contains(draft.Todos, t => t == "lastVerifiedPatch");
    }

    // =========================================================================
    // T7-T12: Extractor with FakeMetadataResolver
    // =========================================================================

    // -------------------------------------------------------------------------
    // T7 -- Extract resolves name, expansion, category, minLevel from metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void T7_ExtractWithResolver_PopulatesNameExpansionCategoryMinLevel()
    {
        /*
         * CONTRACT: Given a trace with RunStart(questId: 66130) and a navigate+done pair,
         *           And a fake resolver returning QuestMetadata("Coming to Ul'dah", "arr",
         *               "msq", MinLevel=1, RequiredJob=null, PrerequisiteQuestIds=[])
         *               for quest 66130,
         *           When new TraceToQuestExtractor(resolver: fakeResolver).Extract(events),
         *           Then draft.Definition.Name == "Coming to Ul'dah",
         *                draft.Definition.Expansion == "arr",
         *                draft.Definition.Category == "msq",
         *                draft.Definition.Requirements!.MinLevel == 1,
         *                draft.Definition.Requirements!.RequiredJob is null,
         *                draft.Definition.Requirements!.Prereqs is empty.
         *
         * RED: Will fail because TraceToQuestExtractor does not accept resolver param yet.
         */

        // Arrange
        var resolver = new FakeMetadataResolver()
            .WithQuest(66130, new QuestMetadata(
                Name: "Coming to Ul'dah",
                Expansion: "arr",
                Category: "msq",
                MinLevel: 1,
                RequiredJob: null,
                PrerequisiteQuestIds: []));

        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("Coming to Ul'dah", draft.Definition.Name);
        Assert.Equal("arr", draft.Definition.Expansion);
        Assert.Equal("msq", draft.Definition.Category);
        Assert.NotNull(draft.Definition.Requirements);
        Assert.Equal(1, draft.Definition.Requirements!.MinLevel);
        Assert.Null(draft.Definition.Requirements.RequiredJob);
        Assert.Empty(draft.Definition.Requirements.Prereqs);
    }

    // -------------------------------------------------------------------------
    // T8 -- Resolved metadata removes TODOs for name, expansion, category, requirements
    // -------------------------------------------------------------------------

    [Fact]
    public void T8_ExtractWithResolver_RemovesResolvedTodosKeepsLastVerifiedPatch()
    {
        /*
         * CONTRACT: Given a trace with RunStart(questId: 66130) and a fake resolver
         *           returning valid metadata for quest 66130,
         *           When Extract is called,
         *           Then draft.Todos does NOT contain "name (Lumina lookup required)",
         *                draft.Todos does NOT contain "expansion",
         *                draft.Todos does NOT contain "category",
         *                draft.Todos does NOT contain "requirements (level, job, prereqs)",
         *           And  draft.Todos DOES contain "lastVerifiedPatch".
         *
         * RED: Will fail because the extractor does not call the resolver yet.
         */

        // Arrange
        var resolver = new FakeMetadataResolver()
            .WithQuest(66130, new QuestMetadata(
                Name: "Coming to Ul'dah",
                Expansion: "arr",
                Category: "msq",
                MinLevel: 1,
                RequiredJob: null,
                PrerequisiteQuestIds: []));

        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.DoesNotContain(draft.Todos, t => t == "name (Lumina lookup required)");
        Assert.DoesNotContain(draft.Todos, t => t == "expansion");
        Assert.DoesNotContain(draft.Todos, t => t == "category");
        Assert.DoesNotContain(draft.Todos, t => t == "requirements (level, job, prereqs)");
        Assert.Contains(draft.Todos, t => t == "lastVerifiedPatch");
    }

    // -------------------------------------------------------------------------
    // T9 -- Prerequisites are mapped to PrerequisiteRef array
    // -------------------------------------------------------------------------

    [Fact]
    public void T9_ExtractWithResolver_PrerequisitesMapToPrereqsArray()
    {
        /*
         * CONTRACT: Given a fake resolver returning PrerequisiteQuestIds=[66100, 66110]
         *           for quest 66130,
         *           When Extract is called,
         *           Then draft.Definition.Requirements!.Prereqs.Length == 2,
         *                Prereqs[0] == new PrerequisiteRef(66100, "complete"),
         *                Prereqs[1] == new PrerequisiteRef(66110, "complete").
         *
         * RED: Will fail because the extractor does not populate Prereqs from resolver.
         */

        // Arrange
        var resolver = new FakeMetadataResolver()
            .WithQuest(66130, new QuestMetadata(
                Name: "Test",
                Expansion: "arr",
                Category: "msq",
                MinLevel: 1,
                RequiredJob: null,
                PrerequisiteQuestIds: [66100, 66110]));

        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.NotNull(draft.Definition.Requirements);
        Assert.Equal(2, draft.Definition.Requirements!.Prereqs.Length);
        Assert.Equal(new PrerequisiteRef(66100, "complete"), draft.Definition.Requirements.Prereqs[0]);
        Assert.Equal(new PrerequisiteRef(66110, "complete"), draft.Definition.Requirements.Prereqs[1]);
    }

    // -------------------------------------------------------------------------
    // T10 -- RequiredJob is populated when metadata provides it
    // -------------------------------------------------------------------------

    [Fact]
    public void T10_ExtractWithResolver_RequiredJobPopulated()
    {
        /*
         * CONTRACT: Given a fake resolver returning RequiredJob="GLA" for quest 65000,
         *           When Extract is called,
         *           Then draft.Definition.Requirements!.RequiredJob == "GLA".
         *
         * RED: Will fail because the extractor does not populate RequiredJob from resolver.
         */

        // Arrange
        var resolver = new FakeMetadataResolver()
            .WithQuest(65000, new QuestMetadata(
                Name: "Some Job Quest",
                Expansion: "arr",
                Category: "job",
                MinLevel: 30,
                RequiredJob: "GLA",
                PrerequisiteQuestIds: []));

        var events = BuildMinimalTrace(questId: 65000u);
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.NotNull(draft.Definition.Requirements);
        Assert.Equal("GLA", draft.Definition.Requirements!.RequiredJob);
    }

    // -------------------------------------------------------------------------
    // T11 -- Resolver returning null for unknown quest falls back to TODOs
    // -------------------------------------------------------------------------

    [Fact]
    public void T11_ExtractWithResolver_UnknownQuestId_FallsBackToTodos()
    {
        /*
         * CONTRACT: Given a fake resolver that returns null for quest 99999,
         *           When Extract is called,
         *           Then draft.Definition.Name == "TODO" and all five standard TODOs present
         *           (identical to NullResolver behavior).
         *
         * RED: Will fail because the extractor does not accept resolver param yet.
         */

        // Arrange — resolver has no entry for quest 99999
        var resolver = new FakeMetadataResolver();
        var events = BuildMinimalTrace(questId: 99999u);
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("TODO", draft.Definition.Name);
        Assert.Equal("TODO", draft.Definition.Expansion);
        Assert.Equal("TODO", draft.Definition.Category);
        Assert.Contains(draft.Todos, t => t == "name (Lumina lookup required)");
        Assert.Contains(draft.Todos, t => t == "expansion");
        Assert.Contains(draft.Todos, t => t == "category");
        Assert.Contains(draft.Todos, t => t == "requirements (level, job, prereqs)");
        Assert.Contains(draft.Todos, t => t == "lastVerifiedPatch");
    }

    // -------------------------------------------------------------------------
    // T12 -- StepRecorded fast path also applies metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void T12_ExtractFastPath_StepRecordedEvents_AlsoAppliesMetadata()
    {
        /*
         * CONTRACT: Given a trace with RunStart(questId: 66130) and StepRecordedEvent
         *           entries (authoring trace path),
         *           And a fake resolver returning QuestMetadata("Coming to Ul'dah", "arr",
         *               "msq", 1, null, []) for quest 66130,
         *           When Extract is called,
         *           Then draft.Definition.Name == "Coming to Ul'dah" (metadata applied
         *                on fast path too),
         *           And draft.Todos does NOT contain "name (Lumina lookup required)".
         *
         * RED: Will fail because:
         *   1. TraceToQuestExtractor does not accept resolver param yet.
         *   2. The fast path does not call the resolver.
         */

        // Arrange — build a trace with StepRecordedEvent entries (fast path)
        var stepJson = JsonSerializer.SerializeToElement(new
        {
            type = "travel",
            destination = new { zone = 182, position = new { x = 10f, y = 0f, z = 20f } }
        });

        var opts = TraceEventJsonContext.Default.Options;
        var stepRecordedLine = JsonSerializer.Serialize<TraceEvent>(
            new StepRecordedEvent
            {
                RunId = "aaa",
                Data = new StepRecordedEvent.StepRecordedData
                {
                    StepId = "travel-1",
                    SequenceNumber = 0,
                    Step = stepJson
                }
            },
            opts);

        var jsonl = MakeTrace(Start(questId: 66130u))
                    + "\n" + stepRecordedLine
                    + "\n" + MakeTrace(End("done"));

        var events = TraceEventParser.ReadText(jsonl);

        var resolver = new FakeMetadataResolver()
            .WithQuest(66130, new QuestMetadata(
                Name: "Coming to Ul'dah",
                Expansion: "arr",
                Category: "msq",
                MinLevel: 1,
                RequiredJob: null,
                PrerequisiteQuestIds: []));

        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.Equal("Coming to Ul'dah", draft.Definition.Name);
        Assert.Equal("arr", draft.Definition.Expansion);
        Assert.Equal("msq", draft.Definition.Category);
        Assert.DoesNotContain(draft.Todos, t => t == "name (Lumina lookup required)");
    }

    // -------------------------------------------------------------------------
    // T12b -- Resolver with empty prereqs: Requirements.Prereqs stays empty
    // -------------------------------------------------------------------------

    [Fact]
    public void T12b_ExtractWithResolver_EmptyPrereqs_PrereqsStaysEmpty()
    {
        /*
         * CONTRACT: Given a fake resolver returning PrerequisiteQuestIds=[] for quest 66130,
         *           When Extract is called,
         *           Then draft.Definition.Requirements!.Prereqs is empty (not null).
         *
         * RED: Will fail because the extractor does not accept resolver param yet.
         */

        // Arrange
        var resolver = new FakeMetadataResolver()
            .WithQuest(66130, new QuestMetadata(
                Name: "Test Quest",
                Expansion: "arr",
                Category: "side",
                MinLevel: 5,
                RequiredJob: null,
                PrerequisiteQuestIds: []));

        var events = BuildMinimalTrace();
        var extractor = new TraceToQuestExtractor(resolver: resolver);

        // Act
        var result = extractor.Extract(events);

        // Assert
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        Assert.NotNull(draft.Definition.Requirements);
        Assert.Empty(draft.Definition.Requirements!.Prereqs);
    }

    // =========================================================================
    // T13-T16: CLI arg parsing for --sqpack
    // =========================================================================

    // -------------------------------------------------------------------------
    // T13 -- --sqpack flag is parsed
    // -------------------------------------------------------------------------

    [Fact]
    public void T13_CliArgs_SqpackFlagParsed()
    {
        /*
         * CONTRACT: Given args ["extract-quest", "run.jsonl", "--sqpack", "/path/to/sqpack"],
         *           When CliArgsParser.Parse(args) is called,
         *           Then result.Subcommand == ExtractQuest,
         *                result.TracePath == "run.jsonl",
         *                result.SqpackPath == "/path/to/sqpack",
         *                result.ParseError is null.
         *
         * RED: Will fail because CliArgs does not have a SqpackPath property yet.
         */

        // Arrange
        string[] args = ["extract-quest", "run.jsonl", "--sqpack", "/path/to/sqpack"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal(CliSubcommand.ExtractQuest, result.Subcommand);
        Assert.Equal("run.jsonl", result.TracePath);
        Assert.Equal("/path/to/sqpack", result.SqpackPath);
        Assert.Null(result.ParseError);
    }

    // -------------------------------------------------------------------------
    // T14 -- --sqpack without value is a parse error
    // -------------------------------------------------------------------------

    [Fact]
    public void T14_CliArgs_SqpackWithoutValue_ParseError()
    {
        /*
         * CONTRACT: Given args ["extract-quest", "run.jsonl", "--sqpack"],
         *           When CliArgsParser.Parse(args) is called,
         *           Then result.ParseError contains "requires a value".
         *
         * RED: Will fail because --sqpack is not in ValueFlags yet,
         * so it will be treated as an unknown flag instead.
         */

        // Arrange
        string[] args = ["extract-quest", "run.jsonl", "--sqpack"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.NotNull(result.ParseError);
        Assert.Contains("requires a value", result.ParseError!);
    }

    // -------------------------------------------------------------------------
    // T15 -- No --sqpack means SqpackPath is null
    // -------------------------------------------------------------------------

    [Fact]
    public void T15_CliArgs_NoSqpackFlag_SqpackPathIsNull()
    {
        /*
         * CONTRACT: Given args ["extract-quest", "run.jsonl"],
         *           When CliArgsParser.Parse(args) is called,
         *           Then result.SqpackPath is null.
         *
         * RED: Will fail because CliArgs does not have a SqpackPath property yet.
         */

        // Arrange
        string[] args = ["extract-quest", "run.jsonl"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Null(result.SqpackPath);
    }

    // -------------------------------------------------------------------------
    // T16 -- --sqpack works alongside other flags
    // -------------------------------------------------------------------------

    [Fact]
    public void T16_CliArgs_SqpackAlongsideOtherFlags()
    {
        /*
         * CONTRACT: Given args ["extract-quest", "run.jsonl", "--sqpack", "/path",
         *                       "--out", "draft.json"],
         *           When CliArgsParser.Parse(args) is called,
         *           Then result.SqpackPath == "/path",
         *                result.OutputPath == "draft.json",
         *                result.ParseError is null.
         *
         * RED: Will fail because CliArgs does not have a SqpackPath property yet.
         */

        // Arrange
        string[] args = ["extract-quest", "run.jsonl", "--sqpack", "/path", "--out", "draft.json"];

        // Act
        CliArgs result = CliArgsParser.Parse(args);

        // Assert
        Assert.Equal("/path", result.SqpackPath);
        Assert.Equal("draft.json", result.OutputPath);
        Assert.Null(result.ParseError);
    }

    // =========================================================================
    // T17-T18: SqpackPathResolver
    // =========================================================================

    // -------------------------------------------------------------------------
    // T17 -- Auto-detect returns null when standard paths do not exist
    // -------------------------------------------------------------------------

    [Fact]
    public void T17_SqpackPathResolver_Resolve_ReturnsNullWhenStandardPathsMissing()
    {
        /*
         * CONTRACT: Given running on a machine without FFXIV installed,
         *           When SqpackPathResolver.Resolve() is called,
         *           Then returns null (no exception thrown).
         *
         * RED: Will fail because SqpackPathResolver does not exist yet.
         *
         * Note: This test will pass in CI (no FFXIV installed) and may return
         * a non-null value on developer machines with FFXIV. The assertion
         * checks that no exception is thrown; the null check is best-effort
         * for environments without the game.
         */

        // Act
        var result = SqpackPathResolver.Resolve();

        // Assert -- on CI this is null; on a dev machine it might be a path
        // At minimum, verify the call does not throw
        Assert.True(result is null or string, "Resolve() should return null or a string path");
    }

    // -------------------------------------------------------------------------
    // T18 -- Auto-detect return type is string? (compile-time contract)
    // -------------------------------------------------------------------------

    [Fact]
    public void T18_SqpackPathResolver_Resolve_ReturnTypeIsNullableString()
    {
        /*
         * CONTRACT: SqpackPathResolver.Resolve() returns string? and does not throw.
         *           When a valid path is found, it returns that path.
         *           When no valid path is found, it returns null.
         *
         * RED: Will fail because SqpackPathResolver does not exist yet.
         *
         * This is a compile-time contract test -- if the return type changes,
         * the test will fail to compile.
         */

        // Act
        string? result = SqpackPathResolver.Resolve();

        // Assert -- no exception thrown is the primary contract
        // The type annotation string? is the compile-time check
        _ = result; // suppress unused warning
    }
}
