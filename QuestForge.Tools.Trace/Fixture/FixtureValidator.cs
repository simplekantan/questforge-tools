using System.Text.Json;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Capabilities;

namespace QuestForge.Tools.Trace.Fixture;

/// <summary>
/// Cross-validates a committed <see cref="FixtureModel"/> against the quest file it references.
/// </summary>
public sealed class FixtureValidator
{
    private readonly string _questDataRoot;

    private static readonly HashSet<string> AllowedTerminalOutcomes = TraceConstants.AllowedTerminalOutcomes;
    private static readonly HashSet<string> SupportedSchemaVersions = ["1.0.0"];
    private static readonly HashSet<string> KnownInitialStates = ["fresh"];

    public FixtureValidator(string questDataRoot)
        => _questDataRoot = questDataRoot;

    /// <summary>
    /// Validate a fixture model against the quest file it references inside
    /// <see cref="_questDataRoot"/>.
    /// </summary>
    public FixtureValidationResult Validate(FixtureModel fixture)
    {
        var errors = new List<FixtureValidationIssue>();
        var warnings = new List<FixtureValidationIssue>();

        // Check schema version first
        if (!SupportedSchemaVersions.Contains(fixture.SchemaVersion))
        {
            errors.Add(new FixtureValidationIssue(
                "fixture/schema-version-unsupported",
                $"schemaVersion '{fixture.SchemaVersion}' is not supported by this tool"));
        }

        // Check terminal outcome
        if (fixture.TerminalOutcome != null &&
            !AllowedTerminalOutcomes.Contains(fixture.TerminalOutcome))
        {
            errors.Add(new FixtureValidationIssue(
                "fixture/terminal-outcome-unknown",
                $"terminalOutcome '{fixture.TerminalOutcome}' is not one of: done, awaitUser"));
        }

        // Check initial state
        if (!KnownInitialStates.Contains(fixture.InitialState))
        {
            warnings.Add(new FixtureValidationIssue(
                "fixture/initial-state-unknown",
                $"initialState '{fixture.InitialState}' is not recognised; expected 'fresh'"));
        }

        // Check quest file existence
        var questPath = Path.Combine(_questDataRoot, fixture.QuestFile);
        if (!File.Exists(questPath))
        {
            errors.Add(new FixtureValidationIssue(
                "fixture/quest-file-missing",
                $"quest file not found: {fixture.QuestFile}"));
            return new FixtureValidationResult(errors, warnings);
        }

        // Load quest file
        QuestDefinition? quest;
        try
        {
            var json = File.ReadAllText(questPath);
            quest = JsonSerializer.Deserialize<QuestDefinition>(json, QuestForgeJsonContext.QuestFileOptions);
        }
        catch (Exception ex)
        {
            errors.Add(new FixtureValidationIssue(
                "fixture/quest-file-unreadable",
                $"failed to parse quest file: {ex.Message}"));
            return new FixtureValidationResult(errors, warnings);
        }

        if (quest == null)
        {
            errors.Add(new FixtureValidationIssue(
                "fixture/quest-file-unreadable",
                "failed to parse quest file: deserialized to null"));
            return new FixtureValidationResult(errors, warnings);
        }

        // Collect all valid step IDs — walk nested branch steps so inner step IDs
        // from BranchStep.Branches[i].Steps are included (FragmentStep has no child steps).
        static IEnumerable<Step> AllSteps(Step s) => s switch
        {
            BranchStep b => new[] { s }.Concat(
                b.Branches?.SelectMany(c => c.Steps?.SelectMany(AllSteps) ?? []) ?? []),
            _ => [s]
        };
        var validStepIds = quest.Sequences
            .SelectMany(seq => seq.Steps.SelectMany(AllSteps))
            .Select(s => s.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        // Also include step IDs from fragments (resume-point and inline fragment steps
        // produce transitions with step IDs from the fragment, not the quest).
        var fragmentsDir = Path.Combine(_questDataRoot, "fragments");
        if (Directory.Exists(fragmentsDir))
        {
            foreach (var file in Directory.EnumerateFiles(fragmentsDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var fragJson = File.ReadAllText(file);
                    var frag = JsonSerializer.Deserialize<FragmentDefinition>(fragJson, QuestForgeJsonContext.QuestFileOptions);
                    if (frag?.Steps is { } fragSteps)
                        foreach (var step in fragSteps)
                            if (!string.IsNullOrEmpty(step.Id))
                                validStepIds.Add(step.Id);
                }
                catch { }
            }
        }

        // Check step IDs in transitions
        foreach (var transition in fixture.ExpectedTransitions)
        {
            if (transition.StepId != null && !validStepIds.Contains(transition.StepId))
            {
                var colonIdx = transition.StepId.LastIndexOf(':');
                var innerId = colonIdx >= 0 ? transition.StepId[(colonIdx + 1)..] : null;
                if (innerId is null || !validStepIds.Contains(innerId))
                {
                    errors.Add(new FixtureValidationIssue(
                        "fixture/step-id-unknown",
                        $"step id '{transition.StepId}' not found in quest {quest.Id}",
                        StepId: transition.StepId));
                }
            }
        }

        // Capability comparison
        var inferredCaps = CapabilityInferrer.Infer(quest).ToHashSet();
        var fixtureCaps = fixture.Capabilities.ToHashSet();

        // Missing: in inferred but not in fixture
        foreach (var cap in inferredCaps.Where(c => !fixtureCaps.Contains(c)))
        {
            warnings.Add(new FixtureValidationIssue(
                "fixture/capability-missing",
                $"quest uses '{cap}' but capabilities list does not include it"));
        }

        // Extra: in fixture but not in inferred
        foreach (var cap in fixtureCaps.Where(c => !inferredCaps.Contains(c)))
        {
            warnings.Add(new FixtureValidationIssue(
                "fixture/capability-extra",
                $"capabilities list includes '{cap}' but quest does not use it"));
        }

        return new FixtureValidationResult(errors, warnings);
    }

    /// <summary>
    /// Convenience overload: load the fixture from disk, then validate.
    /// </summary>
    public FixtureValidationResult ValidateFile(string fixturePath)
    {
        var json = File.ReadAllText(fixturePath);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var fixture = JsonSerializer.Deserialize<FixtureModel>(json, options)
            ?? throw new InvalidOperationException($"Failed to deserialize fixture: {fixturePath}");
        return Validate(fixture);
    }
}
