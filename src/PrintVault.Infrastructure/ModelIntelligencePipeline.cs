using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Orchestrates independent intelligence sections. Each stage has one responsibility
/// and communicates through ModelIntelligenceContext rather than embedding unrelated
/// language, identity, fusion, and arbitration logic in this class.
/// </summary>
public sealed record ModelIntelligenceStageResult(
    ModelRecord Model,
    MultilingualEntityMatch? Entity,
    SemanticEvidenceFusionResult Fusion,
    IReadOnlyList<string> StageEvidence);

public sealed class ModelIntelligencePipeline
{
    private readonly IModelIntelligenceStage[] stages =
    {
        new LanguageNormalizationStage(),
        new EntityClassificationStage(),
        new EntityRecognitionStage(),
        new SemanticFusionStage(),
        new EvidenceArbitrationStage()
    };

    public bool Apply(ModelRecord model)
    {
        if (model is null || model.CategoryOverride) return false;
        var context = new ModelIntelligenceContext(model);
        foreach (var stage in stages) stage.Execute(context);
        return context.StageEvidence.Any(e =>
            e.StartsWith("EVIDENCE:", StringComparison.OrdinalIgnoreCase) &&
            e.EndsWith(":ACTIONABLE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Forensic analysis is read-only: it uses the same canonical stage graph as
    /// production, while explicitly excluding only the stage whose responsibility
    /// is production classification mutation.
    /// </summary>
    public ModelIntelligenceStageResult Analyze(ModelRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var model = Clone(source);
        var context = new ModelIntelligenceContext(model);

        foreach (var stage in stages)
        {
            if (stage is EntityClassificationStage)
                continue;

            stage.Execute(context);
        }

        context.StageEvidence.Add(context.Fusion is null
            ? "EVIDENCE:NONE"
            : $"EVIDENCE:{context.Fusion.EvidenceQuality}:{(context.Fusion.ReviewRequired ? "REVIEW" : "STABLE")}");

        return new ModelIntelligenceStageResult(
            model, context.Entity,
            context.Fusion ?? throw new InvalidOperationException("Semantic fusion stage did not produce a result."),
            context.StageEvidence);
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
