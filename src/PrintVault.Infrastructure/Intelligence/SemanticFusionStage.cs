namespace PrintVault.Infrastructure;

internal sealed class SemanticFusionStage : IModelIntelligenceStage
{
    private readonly SemanticEvidenceFusionService service = new();
    public string Name => "SEMANTIC_FUSION";

    public void Execute(ModelIntelligenceContext context)
    {
        context.Fusion = service.Fuse(context.Model, context.Entity, context.CaptureDiagnostics);
        context.StageEvidence.Add($"SEMANTIC:{context.Fusion.Category}:{context.Fusion.ClassificationConfidence}");
        context.StageEvidence.Add($"FACETS:subject={(string.IsNullOrWhiteSpace(context.Fusion.SubjectIdentity) ? "none" : context.Fusion.SubjectIdentity)}; artifact_role={(string.IsNullOrWhiteSpace(context.Fusion.ArtifactRole) ? "none" : context.Fusion.ArtifactRole)}; contexts={context.Fusion.ContextSignals.Count}");
        if (context.CaptureDiagnostics) context.StageDiagnostics.AddRange(context.Fusion.Diagnostics);
    }
}
