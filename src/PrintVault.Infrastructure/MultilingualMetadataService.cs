using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Compatibility facade for the UI's multilingual metadata actions. The production
/// intelligence pipeline and manual UI action now share one language normalizer, so
/// they cannot assign different confidence meanings or glossary translations.
/// </summary>
public sealed class MultilingualMetadataService
{
    private readonly LanguageIntelligenceService language = new();
    private readonly MultilingualEntityService entities = new();

    public ModelRecord Apply(ModelRecord model)
    {
        language.Apply(model);

        // Entity recognition is additive metadata. It does not overwrite the source
        // language confidence or the measured glossary coverage.
        var entity = entities.Recognize(model);
        if (entity is not null)
        {
            if (!string.IsNullOrWhiteSpace(model.TranslationEvidence)) model.TranslationEvidence += "; ";
            model.TranslationEvidence += entity.Evidence + $"; entity confidence: {entity.Confidence}%";
        }

        return model;
    }

    public (string Language, string Title, int Confidence, string Evidence) Translate(string fileName)
    {
        var result = language.Analyze(fileName);
        return (result.Language, result.TranslatedText, result.TranslationCoverage, result.Evidence);
    }
}
