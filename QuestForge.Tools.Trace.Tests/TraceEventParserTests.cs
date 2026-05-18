using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Tools.Trace.Parsing;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="TraceEventParser"/>.
/// Sanity tests from PHASE_10_PLAN.md §12.5.
/// All tests are RED: they will fail until Builder implements TraceEventParser.
/// </summary>
public sealed class TraceEventParserTests
{
    // -------------------------------------------------------------------------
    // Blank lines and malformed lines are skipped; valid lines are returned
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_BlankAndMalformedLines_AreSkipped_ValidLinesReturned()
    {
        /*
         * RED: Will fail until Builder implements TraceEventParser.ReadText.
         *
         * CONTRACT: Given a JSONL string with one valid run.start event, one blank line,
         *           and one malformed JSON fragment,
         *           When  ReadText,
         *           Then  result contains exactly 1 event (the valid RunStartEvent).
         *                 No exception is thrown.
         *
         * BUILDER GUIDANCE:
         *   - Split on '\n'.
         *   - Skip lines where string.IsNullOrWhiteSpace.
         *   - Catch JsonException for malformed lines; write a warning but continue.
         *   - Unknown "type" discriminator also triggers JsonException → skip gracefully.
         */

        // Arrange
        var jsonl = MakeTrace(Start()) + "\n" +
                    "\n" +
                    "this is not json at all\n";

        var warnings = new StringWriter();

        // Act
        var events = TraceEventParser.ReadText(jsonl, warnings);

        // Assert
        Assert.Single(events);
        Assert.IsType<RunStartEvent>(events[0]);
    }

    // -------------------------------------------------------------------------
    // All six event types round-trip through ReadText
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_AllSixEventTypes_DeserialiseCorrectly()
    {
        /*
         * RED: Will fail until Builder implements TraceEventParser.ReadText.
         *
         * CONTRACT: Given a JSONL string with one of each event type in order,
         *           When  ReadText,
         *           Then  result contains 6 events with the correct concrete types.
         *
         * BUILDER GUIDANCE:
         *   - Uses TraceEventJsonContext.Default.Options for deserialization.
         *   - The [JsonPolymorphic] "type" discriminator drives concrete type selection.
         */

        // Arrange
        var jsonl = MakeTrace(
            Start("r1"),
            Obs("GetPlayerZone", argument: null, value: ZoneValue(182u)),
            Decision("s1", "navigate"),
            Submitted("Navigate", NavParams(1f, 2f, 3f, zone: 182)),
            Completed("Navigate", "Arrived"),
            End("done", "r1")
        );

        // Act
        var events = TraceEventParser.ReadText(jsonl);

        // Assert
        Assert.Equal(6, events.Count);
        Assert.IsType<RunStartEvent>(events[0]);
        Assert.IsType<ObservationEvent>(events[1]);
        Assert.IsType<DecisionEvent>(events[2]);
        Assert.IsType<ActionSubmittedEvent>(events[3]);
        Assert.IsType<ActionCompletedEvent>(events[4]);
        Assert.IsType<RunEndEvent>(events[5]);
    }

    // -------------------------------------------------------------------------
    // Missing trailing newline is not an error
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_NoTrailingNewline_ParsesSuccessfully()
    {
        /*
         * RED: Will fail until Builder implements TraceEventParser.ReadText.
         *
         * CONTRACT: Per TRACE_FORMAT.md §9.2, a missing trailing newline is acceptable.
         *           Given a JSONL string with no trailing newline after the last event,
         *           When  ReadText,
         *           Then  result contains the expected events without error.
         *
         * BUILDER GUIDANCE:
         *   - Do not special-case the last line; standard string.Split('\n') handles this
         *     correctly because the final non-empty token is still a valid JSON line.
         */

        // Arrange — MakeTrace uses '\n' joiner, no trailing newline by default.
        var jsonl = MakeTrace(Start(), End());

        // Act
        var events = TraceEventParser.ReadText(jsonl);

        // Assert
        Assert.Equal(2, events.Count);
    }

    // -------------------------------------------------------------------------
    // Unknown type discriminator is skipped with a warning
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_UnknownTypeDiscriminator_IsSkippedWithWarning()
    {
        /*
         * RED: Will fail until Builder implements TraceEventParser.ReadText.
         *
         * CONTRACT: Given a JSONL line with {"type":"future.unknown","at":"..."},
         *           When  ReadText,
         *           Then  that line is skipped (not in result), a warning is written,
         *                 and surrounding valid events are still returned.
         *
         * BUILDER GUIDANCE:
         *   - JsonException from unknown discriminator → catch, log to warnings, continue.
         */

        // Arrange
        var validLine = MakeTrace(Start());
        var unknownLine = """{"type":"future.unknown","at":"2026-05-16T10:00:00+00:00"}""";
        var jsonl = validLine + "\n" + unknownLine;

        var warnings = new StringWriter();

        // Act
        var events = TraceEventParser.ReadText(jsonl, warnings);

        // Assert
        Assert.Single(events);
        Assert.IsType<RunStartEvent>(events[0]);
        // A warning should have been written (exact text not mandated).
        Assert.NotEmpty(warnings.ToString());
    }

    // -------------------------------------------------------------------------
    // Test 1: InventoryChangedEvent with discriminator round-trips correctly
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_InventoryChangedWithDiscriminator_ParsesCorrectly()
    {
        /*
         * RED: Will fail until Builder adds "inventory.changed" case to TraceEventParser
         * and wires InventoryChangedEvent into DetectAndDeserialize.
         *
         * CONTRACT: Given a JSONL line with {"type":"inventory.changed",...} containing
         *           Gained=[{ItemId:101,Qty:1}], Lost=[], NewHash=42, RunId="r1",
         *           When  ReadText,
         *           Then  result contains exactly 1 InventoryChangedEvent with:
         *                 Gained[0].ItemId == 101, Gained[0].Qty == 1,
         *                 Lost.Count == 0, NewHash == 42u, RunId == "r1".
         *
         * BUILDER GUIDANCE:
         *   - Add "inventory.changed" to the discriminator switch in ParseLine.
         *   - Deserialize to TraceEventJsonContext.Default.InventoryChangedEvent (or TraceEvent).
         */

        // Arrange
        var ev = InventoryChanged(
            gained:        [(101u, 1)],
            lost:          [],
            newHash:       42u,
            runId:         "r1",
            offsetSeconds: 5.0);

        var opts = TraceEventJsonContext.Default.Options;
        // Serialize with the polymorphic context so the "type" discriminator is included.
        var line = JsonSerializer.Serialize<TraceEvent>(ev, opts);
        Assert.Contains("\"type\"", line); // guard: ensure discriminator is present

        var warnings = new StringWriter();

        // Act
        var events = TraceEventParser.ReadText(line, warnings);

        // Assert
        Assert.Single(events);
        var parsed = Assert.IsType<InventoryChangedEvent>(events[0]);
        Assert.Equal("r1",   parsed.RunId);
        Assert.Equal(42u,    parsed.NewHash);
        Assert.Single(parsed.Gained);
        Assert.Equal(101u,   parsed.Gained[0].ItemId);
        Assert.Equal(1,      parsed.Gained[0].Qty);
        Assert.Empty(parsed.Lost);
    }

    // -------------------------------------------------------------------------
    // Test 2: InventoryChangedEvent without discriminator still parses (heuristic fallback)
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_InventoryChangedHeuristicFallback_NoDiscriminator_StillParses()
    {
        /*
         * RED: Will fail until Builder adds InventoryChangedEvent detection to
         * DetectAndDeserialize (the heuristic branch used by test helpers).
         *
         * CONTRACT: Given a JSONL line serialised via the concrete type (no "type" field),
         *           When  ReadText,
         *           Then  result contains exactly 1 InventoryChangedEvent.
         *                 Gained, Lost, NewHash, RunId must round-trip correctly.
         *
         * BUILDER GUIDANCE:
         *   - In DetectAndDeserialize, add a heuristic: if root has "gained" and "lost"
         *     and "newHash", deserialize to InventoryChangedEvent.
         *   - The heuristic must be tested separately from the discriminator path.
         */

        // Arrange — serialize via concrete type (skips [JsonPolymorphic] discriminator).
        var ev = InventoryChanged(
            gained:        [(202u, 2)],
            lost:          [(303u, 1)],
            newHash:       99u,
            runId:         "r2",
            offsetSeconds: 3.0);

        var opts = TraceEventJsonContext.Default.Options;
        var line = JsonSerializer.Serialize(ev, ev.GetType(), opts);
        Assert.DoesNotContain("\"type\"", line); // guard: ensure NO discriminator

        var warnings = new StringWriter();

        // Act
        var events = TraceEventParser.ReadText(line, warnings);

        // Assert
        Assert.Single(events);
        var parsed = Assert.IsType<InventoryChangedEvent>(events[0]);
        Assert.Equal("r2",   parsed.RunId);
        Assert.Equal(99u,    parsed.NewHash);
        Assert.Single(parsed.Gained);
        Assert.Equal(202u,   parsed.Gained[0].ItemId);
        Assert.Equal(2,      parsed.Gained[0].Qty);
        Assert.Single(parsed.Lost);
        Assert.Equal(303u,   parsed.Lost[0].ItemId);
    }

    // -------------------------------------------------------------------------
    // Test 3: InventoryChangedEvent with empty arrays and NewHash=0
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_InventoryChanged_EmptyArrays_ParsesCorrectly()
    {
        /*
         * RED: Will fail until Builder wires InventoryChangedEvent parsing.
         *
         * CONTRACT: Given an InventoryChangedEvent with empty Gained/Lost arrays
         *           and NewHash == 0,
         *           When  ReadText (discriminator present),
         *           Then  parsed event has Gained.Count == 0, Lost.Count == 0,
         *                 NewHash == 0u.
         *
         * BUILDER GUIDANCE:
         *   - Empty arrays in JSON ("gained":[], "lost":[]) must not throw or produce nulls.
         */

        // Arrange
        var ev = InventoryChanged(
            gained:        [],
            lost:          [],
            newHash:       0u,
            runId:         "r3",
            offsetSeconds: 1.0);

        var opts = TraceEventJsonContext.Default.Options;
        var line = JsonSerializer.Serialize<TraceEvent>(ev, opts);

        // Act
        var events = TraceEventParser.ReadText(line);

        // Assert
        Assert.Single(events);
        var parsed = Assert.IsType<InventoryChangedEvent>(events[0]);
        Assert.Empty(parsed.Gained);
        Assert.Empty(parsed.Lost);
        Assert.Equal(0u, parsed.NewHash);
    }

    // -------------------------------------------------------------------------
    // Test 4: Regression guard — RunStartEvent still parses after inventory case added
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadText_RunStartStillParsesAfterInventoryAdded_RegressionGuard()
    {
        /*
         * RED: Will fail until Builder implements the inventory case without breaking
         * existing heuristic dispatch for RunStartEvent.
         *
         * CONTRACT: Given a JSONL string with a RunStart event (no discriminator, via
         *           MakeTrace helper), the heuristic DetectAndDeserialize must still
         *           return a RunStartEvent — the new InventoryChangedEvent heuristic
         *           must not accidentally intercept it.
         *
         * BUILDER GUIDANCE:
         *   - Place the InventoryChangedEvent heuristic check AFTER the RunStartEvent check
         *     in DetectAndDeserialize (RunStartEvent has both "questId" and "questSchemaId").
         */

        // Arrange — MakeTrace uses concrete-type serialization (no discriminator).
        var jsonl = MakeTrace(Start("r4", questId: 66130u));

        // Act
        var events = TraceEventParser.ReadText(jsonl);

        // Assert
        Assert.Single(events);
        var runStart = Assert.IsType<RunStartEvent>(events[0]);
        Assert.Equal("r4",    runStart.RunId);
        Assert.Equal(66130u,  runStart.QuestId);
    }

    // -------------------------------------------------------------------------
    // ReadStream produces same result as ReadText
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadStream_ProducesSameResultAsReadText()
    {
        /*
         * RED: Will fail until Builder implements TraceEventParser.ReadStream.
         *
         * CONTRACT: ReadStream and ReadText must produce identical event sequences
         *           for the same JSONL content.
         *
         * BUILDER GUIDANCE:
         *   - ReadStream can wrap the stream in a StreamReader and delegate to ReadText,
         *     or implement line-by-line reading natively.
         */

        // Arrange
        var jsonl = MakeTrace(Start("x"), Decision("s1", "navigate", "x"), End("done", "x"));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonl));

        // Act
        var fromText   = TraceEventParser.ReadText(jsonl);
        var fromStream = TraceEventParser.ReadStream(stream);

        // Assert
        Assert.Equal(fromText.Count, fromStream.Count);
        for (var i = 0; i < fromText.Count; i++)
        {
            Assert.Equal(fromText[i].GetType(), fromStream[i].GetType());
        }
    }
}
