namespace PrintVault.Infrastructure;

internal sealed class SemanticFusionStage : IModelIntelligenceStage
{
    private readonly SemanticEvidenceFusionService service = new();
    public string Name => "SEMANTIC_FUSION";

    public void Execute(ModelIntelligenceContext context)
    {
        context.Fusion = service.Fuse(context.Model, context.Entity);
        context.StageEvidence.Add($"SEMANTIC:{context.Fusion.Category}:{context.Fusion.ClassificationConfidence}");
    }
}
