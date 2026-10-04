using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Explicitly compartmentalizes model intelligence into independent stages.
/// Each stage consumes the previous stage's output but does not mutate the catalog.
/// This prevents translation, entity identity, semantic classification, and arbitration
/// from becoming one undifferentiated evidence stream.
/// </summary>
public sealed record ModelIntelligenceStageResult(
    ModelRecord Model,
    MultilingualEntityMatch? Entity,
    SemanticEvidenceFusionResult Fusion,
    IReadOnlyList<string> StageEvidence);

public sealed class ModelIntelligencePipeline
{
    private readonly MultilingualMetadataService language = new();
    private readonly MultilingualEntityService entities = new();
    private readonly EntityClassificationService entityClassification = new();
    private readonly SemanticEvidenceFusionService fusion = new();

    /// <summary>
    /// Production application of the modular intelligence stages.
    /// Language normalization is always performed before entity/semantic arbitration.
    /// Custom/user categories remain authoritative.
    /// </summary>
    public bool Apply(ModelRecord model)
    {
        if (model is null || model.CategoryOverride) return false;

        language.Apply(model);
        entityClassification.Apply(model);

        var entity = entities.Recognize(model);
        var result = fusion.Fuse(model, entity);

        var unresolved = string.IsNullOrWhiteSpace(model.Category) ||
                         string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(model.Category, "Unknown", StringComparison.OrdinalIgnoreCase);

        if (!unresolved && !BuiltInCategories.All.Contains(model.Category, StringComparer.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(result.Category) ||
            string.Equals(result.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
            return entity is not null;

        var structuralBasis = result.Basis.Contains("Named entity", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Object role", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Organizer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Source-derived analyzer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Lexical identity + domain convergence", StringComparison.OrdinalIgnoreCase);
        var lexicalBasis = result.Basis.Contains("Lexical evidence", StringComparison.OrdinalIgnoreCase);
        var lexicalHits = result.Evidence.Count(e => e.StartsWith("Lexical cue:", StringComparison.OrdinalIgnoreCase));
        var actionable = (structuralBasis && result.ClassificationConfidence >= 85 && result.EvidenceQuality >= 75) ||
                         (lexicalBasis && result.ClassificationConfidence >= 70 && result.EvidenceQuality >= 40 && lexicalHits >= 1) ||
                         (unresolved && lexicalBasis && result.ClassificationConfidence >= 60 && result.EvidenceQuality >= 25 && lexicalHits >= 1);

        if (!actionable) return entity is not null;

        model.Category = result.Category;
        model.Family = result.Family;
        model.SemanticType = result.SemanticType;
        model.Subtype = result.Subtype;
        model.IntelligenceScore = Math.Max(model.IntelligenceScore, result.ClassificationConfidence / 100d);
        var evidence = $"Semantic fusion: {result.Basis}; confidence {result.ClassificationConfidence}%";
        model.IntelligenceReason = string.IsNullOrWhiteSpace(model.IntelligenceReason)
            ? evidence
            : $"{model.IntelligenceReason}; {evidence}";
        return true;
    }

    public ModelIntelligenceStageResult Analyze(ModelRecord source)
    {
        // Work on an isolated copy so forensic analysis remains read-only.
        var model = Clone(source);
        var stages = new List<string>();

        // SECTION 1 — language normalization.
        language.Apply(model);
        stages.Add($"LANGUAGE:{model.OriginalLanguage}:{model.TranslationConfidence}");

        // SECTION 2 — entity/identity recognition.
        var entity = entities.Recognize(model);
        stages.Add(entity is null
            ? "ENTITY:NONE"
            : $"ENTITY:{entity.EntityName}:{entity.Confidence}");

        // SECTION 3 — semantic/category arbitration.
        var result = fusion.Fuse(model, entity);
        stages.Add($"SEMANTIC:{result.Category}:{result.ClassificationConfidence}");

        // SECTION 4 — evidence boundary.
        // Translation and entity evidence are attached as provenance only. The fusion
        // result remains the authoritative classification channel for the caller.
        stages.Add($"EVIDENCE:{result.EvidenceQuality}:{(result.ReviewRequired ? "REVIEW" : "STABLE")}");

        return new ModelIntelligenceStageResult(model, entity, result, stages);
    }

    private static ModelRecord Clone(ModelRecord source) => new()
    {
        Id = source.Id, Path = source.Path, Name = source.Name, Category = source.Category,
        Size = source.Size, ModifiedUtc = source.ModifiedUtc, ThumbnailPath = source.ThumbnailPath,
        Favorite = source.Favorite, Tags = source.Tags, Family = source.Family, Slicer = source.Slicer,
        Materials = source.Materials, ObjectCount = source.ObjectCount, Dimensions = source.Dimensions,
        IntelligenceScore = source.IntelligenceScore, PrintReady = source.PrintReady, DuplicateGroup = source.DuplicateGroup,
        Hash = source.Hash, SemanticType = source.SemanticType, Subtype = source.Subtype,
        SuggestedTags = source.SuggestedTags, IntelligenceReason = source.IntelligenceReason,
        RiskFlags = source.RiskFlags, PrintMethod = source.PrintMethod, PrintMethodConfidence = source.PrintMethodConfidence,
        PrintMethodEvidence = source.PrintMethodEvidence, SpecialType = source.SpecialType,
        CategoryOverride = source.CategoryOverride, PrintMethodOverride = source.PrintMethodOverride,
        OriginalLanguage = source.OriginalLanguage, TranslatedTitle = source.TranslatedTitle,
        TranslationConfidence = source.TranslationConfidence, TranslationEvidence = source.TranslationEvidence
    };
}
