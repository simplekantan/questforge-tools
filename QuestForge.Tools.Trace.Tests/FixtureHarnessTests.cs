using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Cli;
using QuestForge.Tools.Trace.Fixture;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED PHASE: Group F — TraceToFixtureExtractor FilenameLookup expansion
/// and trace co-emission (--with-trace / --no-trace flag).
/// Per FIXTURE_REPLAY_HARNESS_PLAN.md §5 Group F.
///
/// Tests F1–F8 assert SuggestFilename for new and existing capability sets.
/// Tests F9–F10 assert the --with-trace / --no-trace co-emission behavior.
///
/// Expected RED signals:
/// F1–F5, F7 — assertion failure: SuggestFilename returns "simple-linear-acceptance.json"
///             for all unrecognised sets (the new FilenameLookup entries do not exist yet).
/// F8 — may pass (unknown shape → default fallback already returns the default).
/// F6 — should PASS (existing five mappings are unchanged).
/// F9, F10 — compile errors: CliArgs.WithTrace does not exist → CS1061.
/// </summary>
public sealed class FixtureHarnessTests
{
    // =====================================================================
    // GROUP F — FilenameLookup expansion (F1–F8)
    // =====================================================================

    // ------------------------------------------------------------------
    // F1 — accept shape → with-accept.json
    // ------------------------------------------------------------------

    /// <summary>F1 — step:accept, step:talk, step:travel → with-accept.json.</summary>
    [Fact]
    public void F1_SuggestFilename_AcceptShape_ReturnsWithAccept()
    {
        // Given a FixtureModel whose Capabilities contain exactly step:accept, step:talk, step:travel,
        // When SuggestFilename, Then "with-accept.json".
        //
        // RED: FilenameLookup does not have this entry yet → SuggestFilename returns
        //      "simple-linear-acceptance.json" → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:accept", "step:talk", "step:travel");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-accept.json", result);
    }

    // ------------------------------------------------------------------
    // F2 — turn-in shape → with-turn-in.json
    // ------------------------------------------------------------------

    /// <summary>F2 — step:talk, step:travel, step:turn-in → with-turn-in.json.</summary>
    [Fact]
    public void F2_SuggestFilename_TurnInShape_ReturnsWithTurnIn()
    {
        // RED: FilenameLookup entry absent → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:talk", "step:travel", "step:turn-in");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-turn-in.json", result);
    }

    // ------------------------------------------------------------------
    // F3 — attune shape → with-attunement.json
    // ------------------------------------------------------------------

    /// <summary>F3 — step:attune, step:travel → with-attunement.json.</summary>
    [Fact]
    public void F3_SuggestFilename_AttuneShape_ReturnsWithAttunement()
    {
        // RED: FilenameLookup entry absent → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:attune", "step:travel");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-attunement.json", result);
    }

    // ------------------------------------------------------------------
    // F4 — hand-over-item shape → with-hand-over-item.json
    // ------------------------------------------------------------------

    /// <summary>F4 — step:hand-over-item, step:talk, step:travel → with-hand-over-item.json.</summary>
    [Fact]
    public void F4_SuggestFilename_HandOverItemShape_ReturnsWithHandOverItem()
    {
        // RED: FilenameLookup entry absent → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:hand-over-item", "step:talk", "step:travel");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-hand-over-item.json", result);
    }

    // ------------------------------------------------------------------
    // F5 — interact-object and pickup-item shapes
    // ------------------------------------------------------------------

    /// <summary>F5a — step:interact-object, step:travel → with-interact-object.json.</summary>
    [Fact]
    public void F5a_SuggestFilename_InteractObjectShape_ReturnsWithInteractObject()
    {
        // RED: FilenameLookup entry absent → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:interact-object", "step:travel");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-interact-object.json", result);
    }

    /// <summary>F5b — step:pickup-item, step:travel → with-pickup-item.json.</summary>
    [Fact]
    public void F5b_SuggestFilename_PickupItemShape_ReturnsWithPickupItem()
    {
        // RED: FilenameLookup entry absent → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:pickup-item", "step:travel");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-pickup-item.json", result);
    }

    // ------------------------------------------------------------------
    // F6 — existing shapes unchanged (regression guard)
    // ------------------------------------------------------------------

    /// <summary>F6 — existing five mappings still resolve correctly.</summary>
    [Theory]
    [InlineData(new[] { "step:talk", "step:travel" },             "simple-linear-acceptance.json")]
    [InlineData(new[] { "step:duty" },                            "with-dungeon.json")]
    [InlineData(new[] { "step:spd" },                             "with-spd.json")]
    [InlineData(new[] { "step:branch" },                          "with-branching.json")]
    [InlineData(new[] { "step:fragment" },                        "with-fragments.json")]
    public void F6_SuggestFilename_ExistingMappings_StillResolveCorrectly(
        string[] capabilities, string expectedFilename)
    {
        // These five mappings exist in the current FilenameLookup and must not regress.
        // F6 is expected to PASS immediately (no new code required for existing entries).

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture(capabilities);

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal(expectedFilename, result);
    }

    // ------------------------------------------------------------------
    // F7 — multi-shape fallback by highest-priority distinguishing capability
    // ------------------------------------------------------------------

    /// <summary>
    /// F7 — capability set matching no exact entry falls back to the highest-priority
    /// distinguishing tag (§3.7 priority list).
    /// </summary>
    [Fact]
    public void F7_SuggestFilename_MultiShapeNoExactMatch_FallsBackToHighestPriorityTag()
    {
        // Given the 65644 capability set:
        //   step:accept, step:attune, step:hand-over-item, step:talk, step:travel, step:turn-in
        // (no exact entry exists), the fallback must pick step:attune (outranks the others)
        // and return "with-attunement.json" — NOT "simple-linear-acceptance.json".
        //
        // RED: fallback rule not implemented → SuggestFilename returns "simple-linear-acceptance.json"
        //      → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        // The 65644 full capability set (all sorted step: tags present)
        var fixture = MakeFixture(
            "step:accept", "step:attune", "step:hand-over-item",
            "step:talk",   "step:travel", "step:turn-in");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-attunement.json", result);
    }

    // Priority list for reference (§3.7):
    // step:duty > step:spd > step:branch > step:fragment > step:attune >
    // step:hand-over-item > step:turn-in > step:accept > step:interact-object > step:pickup-item

    /// <summary>F7b — duty outranks all others in the fallback priority list.</summary>
    [Fact]
    public void F7b_SuggestFilename_DutyPlusOtherCaps_FallsBackToDuty()
    {
        // step:duty outranks step:attune in the priority list.
        // RED: fallback rule not implemented → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:duty", "step:travel", "step:attune");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-dungeon.json", result);
    }

    /// <summary>F7c — hand-over-item outranks turn-in in the priority list.</summary>
    [Fact]
    public void F7c_SuggestFilename_HandOverItemPlusTurnIn_FallsBackToHandOverItem()
    {
        // step:hand-over-item outranks step:turn-in.
        // RED: fallback rule not implemented → assertion failure.

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:hand-over-item", "step:turn-in", "step:travel", "step:talk");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-hand-over-item.json", result);
    }

    // ------------------------------------------------------------------
    // F8 — unknown shape → default fallback
    // ------------------------------------------------------------------

    /// <summary>F8 — unknown / unmapped capability → "simple-linear-acceptance.json".</summary>
    [Fact]
    public void F8_SuggestFilename_UnknownShape_ReturnsSimpleLinearAcceptanceFallback()
    {
        // Given a FixtureModel with only an unmapped capability (e.g. step:cutscene alone),
        // SuggestFilename returns "simple-linear-acceptance.json".
        //
        // This is the existing fallback behavior — expected to PASS already (no new code).

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:cutscene"); // unmapped

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("simple-linear-acceptance.json", result);
    }

    // =====================================================================
    // GROUP F — Trace co-emission (F9, F10)
    // NOTE: F9, F9b, F10 are in a SEPARATE file: FixtureHarnessCliTests.cs
    //       because they reference CliArgs.WithTrace which does not exist yet
    //       (CS1061 compile error). Separating them allows F1–F8 to run as
    //       assertion-failure REDs while F9/F10 produce compile-error REDs.
    // =====================================================================

    // =====================================================================
    // S3_T3/T4 — UseItemOnObjectStep filename suggestion (Slice 3 tooling catch-up)
    // =====================================================================

    /// <summary>
    /// S3_T3 — exact capability match: step:talk + step:travel + step:use-item-on-object
    /// maps to "with-use-item-on-object.json".
    /// </summary>
    [Fact]
    public void S3_T3_SuggestFilename_UseItemOnObjectExactShape_ReturnsWithUseItemOnObject()
    {
        // RED: FilenameLookup does not have
        //   (["step:talk", "step:travel", "step:use-item-on-object"], "with-use-item-on-object.json")
        // yet, so SuggestFilename will not match the exact entry.
        // The DistinguishingCapPriority also lacks step:use-item-on-object, so the
        // final fallback returns "simple-linear-acceptance.json" instead.
        //
        // BUILDER GUIDANCE: Add to FilenameLookup:
        //   (["step:talk", "step:travel", "step:use-item-on-object"], "with-use-item-on-object.json"),

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture("step:talk", "step:travel", "step:use-item-on-object");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-use-item-on-object.json", result);
    }

    /// <summary>
    /// S3_T4 — fallback via DistinguishingCapPriority: a multi-cap fixture containing
    /// step:use-item-on-object (plus caps that prevent an exact FilenameLookup match)
    /// resolves to "with-use-item-on-object.json" through the priority fallback.
    /// </summary>
    [Fact]
    public void S3_T4_SuggestFilename_UseItemOnObjectFallback_ReturnsWithUseItemOnObject()
    {
        // Given a FixtureModel with Capabilities =
        //   ["step:accept", "step:talk", "step:travel", "step:use-item-on-object"]
        // This 4-cap set does NOT match any exact FilenameLookup entry.
        // The fallback walks DistinguishingCapPriority and should find
        // step:use-item-on-object before step:accept (which is not in the priority list).
        //
        // RED: DistinguishingCapPriority does not have
        //   ("step:use-item-on-object", "with-use-item-on-object.json")
        // yet, so the fallback skips it and eventually returns
        // "with-accept.json" (from the existing step:accept priority entry)
        // instead of the correct "with-use-item-on-object.json".
        //
        // BUILDER GUIDANCE: Add to DistinguishingCapPriority (between step:use-item
        // and step:say-chat-message):
        //   ("step:use-item-on-object", "with-use-item-on-object.json"),

        var extractor = new TraceToFixtureExtractor();
        var fixture = MakeFixture(
            "step:accept", "step:talk", "step:travel", "step:use-item-on-object");

        var result = extractor.SuggestFilename(fixture);

        Assert.Equal("with-use-item-on-object.json", result);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static FixtureModel MakeFixture(params string[] capabilities)
        => new(
            SchemaVersion: "1.1.0",
            Description: "Test fixture",
            InitialState: "fresh",
            Capabilities: capabilities,
            QuestFile: "quests/UNKNOWN/0.json",
            ExpectedTransitions: [],
            TerminalOutcome: "done");
}
