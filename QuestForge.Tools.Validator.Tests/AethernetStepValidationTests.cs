using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Tests for AethernetStep structural validation rules:
///   - structural/aethernet-from-zero  when From == 0
///   - structural/aethernet-to-zero    when To == 0
/// </summary>
public sealed class AethernetStepValidationTests
{
    // =========================================================================
    // Schema: "aethernet" discriminator deserializes to AethernetStep
    // =========================================================================

    [Fact]
    public void AV_Deserialize_AethernetStep_FromJson()
    {
        const string json = """
            {
                "type": "aethernet",
                "id": "aethernet-a",
                "from": 10,
                "to": 20,
                "fromPosition": { "x": 1.0, "y": 2.0, "z": 3.0 }
            }
            """;

        var step = JsonSerializer.Deserialize<Step>(json, QuestForgeJsonContext.QuestFileOptions);

        var aethernet = Assert.IsType<AethernetStep>(step);
        Assert.Equal("aethernet-a", aethernet.Id);
        Assert.Equal(10u, aethernet.From);
        Assert.Equal(20u, aethernet.To);
    }

    // =========================================================================
    // structural/aethernet-from-zero
    // =========================================================================

    [Fact]
    public void AV_FromZero_ReportsError()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 0, To = 20u, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/aethernet-from-zero", stepId: "aethernet-a");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void AV_FromNonZero_NoFromError()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 10u, To = 20u, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/aethernet-from-zero");
    }

    // =========================================================================
    // structural/aethernet-to-zero
    // =========================================================================

    [Fact]
    public void AV_ToZero_ReportsError()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 10u, To = 0, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        QuestBuilder.AssertSingleError(errors, "structural/aethernet-to-zero", stepId: "aethernet-a");
        Assert.Equal(Severity.Error, errors[0].Severity);
    }

    [Fact]
    public void AV_ToNonZero_NoToError()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 10u, To = 20u, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoError(errors, "structural/aethernet-to-zero");
    }

    [Fact]
    public void AV_BothFromAndToZero_ReportsBothErrors()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 0, To = 0, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)])).ToList();

        Assert.Contains(errors, e => e.Code == "structural/aethernet-from-zero");
        Assert.Contains(errors, e => e.Code == "structural/aethernet-to-zero");
    }

    [Fact]
    public void AV_ValidAethernetStep_NoErrors()
    {
        var step = new AethernetStep { Id = "aethernet-a", From = 10u, To = 20u, FromPosition = new Position3(0f, 0f, 0f) };
        var errors = QuestBuilder.Validate(QuestBuilder.Valid(
            sequences: [QuestBuilder.Seq(0, step)]));

        QuestBuilder.AssertNoErrors(errors);
    }
}
