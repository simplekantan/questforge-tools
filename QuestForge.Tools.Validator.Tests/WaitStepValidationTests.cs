using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Tests for the WaitStep tools mirror: the "wait" step type must deserialize through the
/// shared quest-file options (so qf-validate doesn't reject a quest that uses it), and the
/// structural validator warns when 'seconds' is non-positive.
///
/// Rule under test:
///   - Warning "structural/wait-seconds-nonpositive" when WaitStep.Seconds &lt;= 0.
/// </summary>
public sealed class WaitStepValidationTests
{
    // =========================================================================
    // Schema: a "wait" step deserializes via the shared quest-file options.
    // =========================================================================

    [Fact]
    public void WV_Deserialize_WaitStep_FromJson()
    {
        // Given the JSON an author would write,
        // When deserialized through QuestFileOptions (the qf-validate load path),
        // Then it resolves to a WaitStep with the given Seconds (no throw / no unknown-discriminator).
        const string json = """{ "type": "wait", "id": "wait-a", "seconds": 5 }""";

        var step = JsonSerializer.Deserialize<Step>(json, QuestForgeJsonContext.QuestFileOptions);

        var wait = Assert.IsType<WaitStep>(step);
        Assert.Equal("wait-a", wait.Id);
        Assert.Equal(5.0, wait.Seconds);
    }

    // =========================================================================
    // structural/wait-seconds-nonpositive
    // =========================================================================

    [Fact]
    public void WV_ZeroSeconds_ReportsWarning()
    {
        // Given a wait step with seconds = 0,
        // Then exactly one structural/wait-seconds-nonpositive Warning is produced.
        var step = new WaitStep { Id = "wait-a", Seconds = 0 };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/wait-seconds-nonpositive");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void WV_NegativeSeconds_ReportsWarning()
    {
        // Given a wait step with negative seconds,
        // Then the structural/wait-seconds-nonpositive Warning is produced.
        var step = new WaitStep { Id = "wait-a", Seconds = -3 };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/wait-seconds-nonpositive");
        Assert.Equal(Severity.Warning, errors[0].Severity);
    }

    [Fact]
    public void WV_PositiveSeconds_NoWarning()
    {
        // Given a well-formed wait step (seconds > 0),
        // Then no wait-seconds-nonpositive warning (a WaitStep needs no Expect — it is time-based).
        var step = new WaitStep { Id = "wait-a", Seconds = 5 };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/wait-seconds-nonpositive");
    }

    [Fact]
    public void WV_WaitStepInBranch_ZeroSeconds_ReportsWarning()
    {
        // Verifies the wait rule recurses into BranchStep cases (matching the combat pattern).
        var wait = new WaitStep { Id = "wait-nested", Seconds = 0 };
        var branch = new BranchStep
        {
            Id       = "branch-a",
            Branches = [new BranchCase("default", [wait])],
            Expect   = new PredicateExpect { Predicate = "questSequence(65657) >= 1" }
        };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, branch)])).ToList();

        Assert.True(
            errors.Any(e => e.Code == "structural/wait-seconds-nonpositive"),
            $"Expected structural/wait-seconds-nonpositive for nested wait step but got: " +
            $"[{string.Join(", ", errors.Select(e => $"{e.Code}: {e.Message}"))}]");
    }
}
