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
            $"LANGUAGE:{context.Model.OriginalLanguage}:language_confidence={context.Model.LanguageConfidence}:translation_coverage={context.Model.TranslationConfidence}:{(string.IsNullOrWhiteSpace(context.Model.TranslatedTitle) ? "EMPTY" : "READY")}");
        if (context.CaptureDiagnostics)
        {
            var translationEvidence = context.Model.TranslationEvidence ?? string.Empty;
            context.StageDiagnostics.Add($"input_chars={(context.Model.Name ?? string.Empty).Length}; source_semantic_chars={(context.Model.SourceSemanticEvidence ?? string.Empty).Length}; translated_title_chars={(context.Model.TranslatedTitle ?? string.Empty).Length}; language={context.Model.OriginalLanguage}; language_confidence={context.Model.LanguageConfidence}; translation_coverage={context.Model.TranslationConfidence}; evidence={(translationEvidence.Length > 300 ? translationEvidence[..300] : translationEvidence)}");
        }
    }
}
