using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

public interface IValidator
{
    IEnumerable<ValidationError> Validate(QuestDefinition quest, ValidationContext ctx);
}