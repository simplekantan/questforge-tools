namespace QuestForge.Tools.Validator;

/// <summary>
/// Immutable metadata about the file being validated.
/// Scope tracking (sequence, branch depth) lives privately inside each validator.
/// </summary>
public record ValidationContext(string FilePath, uint QuestId);