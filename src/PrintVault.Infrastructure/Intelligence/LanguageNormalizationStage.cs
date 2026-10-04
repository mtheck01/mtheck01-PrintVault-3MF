using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal sealed class LanguageNormalizationStage : IModelIntelligenceStage
{
    private readonly LanguageIntelligenceService service = new();
    public string Name => "LANGUAGE";

    public void Execute(ModelIntelligenceContext context)
    {
        service.Apply(context.Model);
        context.StageEvidence.Add(
            $"LANGUAGE:{context.Model.OriginalLanguage}:{context.Model.TranslationConfidence}:{(string.IsNullOrWhiteSpace(context.Model.TranslatedTitle) ? "EMPTY" : "READY")}");
    }
}
