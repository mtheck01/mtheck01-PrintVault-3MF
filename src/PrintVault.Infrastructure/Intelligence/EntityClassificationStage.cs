namespace PrintVault.Infrastructure;

internal sealed class EntityClassificationStage : IModelIntelligenceStage
{
    private readonly EntityClassificationService service = new();
    public string Name => "ENTITY_CLASSIFICATION";

    public void Execute(ModelIntelligenceContext context)
    {
        context.ClassificationApplied = service.Apply(context.Model);
        context.StageEvidence.Add($"ENTITY_CLASSIFICATION:{(context.ClassificationApplied ? "APPLIED" : "UNCHANGED")}");
    }
}
