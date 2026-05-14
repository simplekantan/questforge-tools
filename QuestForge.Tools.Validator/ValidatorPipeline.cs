using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

public sealed class ValidatorPipeline(IEnumerable<IValidator> validators)
{
    public IEnumerable<ValidationError> Validate(QuestDefinition quest, ValidationContext ctx)
        => validators.SelectMany(v => v.Validate(quest, ctx));
}