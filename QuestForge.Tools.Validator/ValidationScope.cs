namespace QuestForge.Tools.Validator;

/// <summary>
/// Records where in the quest tree a step lives.
/// Used by StructuralValidator Pass 1 to build the step-ID → scope map,
/// and by Pass 2 to detect cross-branch goto violations.
/// </summary>
public record ValidationScope(
    int SequenceNumber,
    string? BranchStepId = null,
    int? BranchCaseIndex = null
)
{
    public static readonly ValidationScope Root = new(-1);

    public override string ToString()
    {
        if (BranchStepId is null)
            return $"seq:{SequenceNumber}";
        return $"seq:{SequenceNumber}/branch:{BranchStepId}/case:{BranchCaseIndex}";
    }

    /// <summary>
    /// Two scopes are compatible for a goto if they share the same sequence
    /// and the same branch step + case (or are both at the sequence level).
    /// </summary>
    public bool IsCompatibleWith(ValidationScope other) =>
        SequenceNumber == other.SequenceNumber
        && BranchStepId == other.BranchStepId
        && BranchCaseIndex == other.BranchCaseIndex;
}