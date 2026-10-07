using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal sealed class EntityRecognitionStage : IModelIntelligenceStage
{
    private readonly MultilingualEntityService service = new();

    public string Name => "ENTITY_RECOGNITION";

    public void Execute(ModelIntelligenceContext context)
    {
        context.Entity = service.Recognize(context.Model);

        if (context.Entity is not null)
        {
            context.Model.SuggestedTags = AddTag(context.Model.SuggestedTags, "entity:" + context.Entity.EntityName);
            context.Model.SuggestedTags = AddTag(context.Model.SuggestedTags, "entity-domain:" + context.Entity.Domain);
            context.Model.Tags = AddTag(context.Model.Tags, context.Entity.Category);
        }

        context.StageEvidence.Add(context.Entity is null
            ? "ENTITY:NONE"
            : $"ENTITY:{context.Entity.EntityName}:{context.Entity.Confidence}");
    }

    private static string AddTag(string existing, string tag)
    {
        var values = (existing ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (!values.Contains(tag, StringComparer.OrdinalIgnoreCase))
            values.Add(tag);

        return string.Join(", ", values);
    }
}
