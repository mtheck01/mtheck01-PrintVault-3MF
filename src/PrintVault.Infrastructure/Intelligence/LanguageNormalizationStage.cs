using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal sealed class LanguageNormalizationStage : IModelIntelligenceStage
{
    private readonly MultilingualMetadataService service = new();
    public string Name => "LANGUAGE";

    public void Execute(ModelIntelligenceContext context)
    {
        service.Apply(context.Model);
        context.StageEvidence.Add($"LANGUAGE:{context.Model.OriginalLanguage}:{context.Model.TranslationConfidence}");
    }
}
