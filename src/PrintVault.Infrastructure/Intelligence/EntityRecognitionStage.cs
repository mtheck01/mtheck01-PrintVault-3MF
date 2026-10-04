namespace PrintVault.Infrastructure;

internal sealed class EntityRecognitionStage : IModelIntelligenceStage
{
    private readonly MultilingualEntityService service = new();
    public string Name => "ENTITY_RECOGNITION";

    public void Execute(ModelIntelligenceContext context)
    {
        context.Entity = service.Recognize(context.Model);
        context.StageEvidence.Add(context.Entity is null
            ? "ENTITY:NONE"
            : $"ENTITY:{context.Entity.EntityName}:{context.Entity.Confidence}");
    }
}
