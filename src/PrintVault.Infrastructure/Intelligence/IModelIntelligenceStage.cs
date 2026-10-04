using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal interface IModelIntelligenceStage
{
    string Name { get; }
    void Execute(ModelIntelligenceContext context);
}

internal sealed class ModelIntelligenceContext
{
    public ModelIntelligenceContext(ModelRecord model) => Model = model;
    public ModelRecord Model { get; }
    public MultilingualEntityMatch? Entity { get; set; }
    public SemanticEvidenceFusionResult? Fusion { get; set; }
    public List<string> StageEvidence { get; } = new();
    public bool ClassificationApplied { get; set; }
}
