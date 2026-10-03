using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Single deterministic production boundary for model semantic analysis.
/// The pipeline owns analyzer -> entity -> evidence-fusion ordering so incremental scans,
/// rebuilds, and reconciliation cannot silently diverge.
/// </summary>
public sealed class ModelAnalysisPipeline
{
    private readonly ThreeMfAnalyzer analyzer = new();
    private readonly EntityClassificationService entityClassification = new();
    private readonly SemanticEvidenceFusionService semanticFusion = new();

    public void AnalyzeAndApply(ModelRecord model)
    {
        var a = analyzer.Analyze(model.Path, model.Name);

        model.Category = a.Category;
        model.IntelligenceScore = a.Confidence;
        if (!model.PrintMethodOverride)
        {
            model.PrintMethod = a.PrintMethod;
            model.PrintMethodConfidence = a.PrintMethodConfidence;
            model.PrintMethodEvidence = a.PrintMethodEvidence;
        }

        model.SpecialType = a.SpecialType;
        model.Family = a.Family;
        model.ObjectCount = a.ObjectCount;
        model.Dimensions = a.Dimensions;
        model.Slicer = a.Slicer;
        model.Materials = a.Materials;
        model.PrintReady = a.PrintReady;
        model.SemanticType = a.SemanticType;
        model.Subtype = a.Subtype;
        model.SuggestedTags = a.SuggestedTags;
        model.IntelligenceReason = a.Reason;
        model.RiskFlags = a.RiskFlags;

        entityClassification.Apply(model);
        ApplyFusion(model);
    }

    public void Reconcile(ModelRecord model)
    {
        if (model.CategoryOverride || !IsBuiltInOrUnresolved(model.Category))
            return;

        AnalyzeAndApplyPreservingCategory(model);
    }

    private void AnalyzeAndApplyPreservingCategory(ModelRecord model)
    {
        var before = model.Category;
        AnalyzeAndApply(model);

        // Reconciliation may reclassify built-in/unresolved categories, but never a custom
        // category. CategoryOverride is handled by the caller before this method.
        _ = before;
    }

    private void ApplyFusion(ModelRecord model)
    {
        if (model.CategoryOverride) return;

        var unresolved = string.IsNullOrWhiteSpace(model.Category) ||
                         string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(model.Category, "Unknown", StringComparison.OrdinalIgnoreCase);
        if (!unresolved && !BuiltInCategories.All.Contains(model.Category, StringComparer.OrdinalIgnoreCase))
            return;

        var entity = new MultilingualEntityService().Recognize(model);
        var result = semanticFusion.Fuse(model, entity);
        if (string.IsNullOrWhiteSpace(result.Category) ||
            string.Equals(result.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
            return;

        var structuralBasis = result.Basis.Contains("Named entity", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Object role", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Organizer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Source-derived analyzer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Lexical identity + domain convergence", StringComparison.OrdinalIgnoreCase);
        var lexicalBasis = result.Basis.Contains("Lexical evidence", StringComparison.OrdinalIgnoreCase);
        var lexicalHits = result.Evidence.Count(e => e.StartsWith("Lexical cue:", StringComparison.OrdinalIgnoreCase));
        var lexicalActionable = lexicalBasis &&
                                result.ClassificationConfidence >= 74 &&
                                result.EvidenceQuality >= 50 &&
                                (result.ClassificationConfidence >= 82 || lexicalHits >= 1);
        var unresolvedLexicalActionable = unresolved &&
                                          lexicalBasis &&
                                          result.ClassificationConfidence >= 60 &&
                                          result.EvidenceQuality >= 25 &&
                                          lexicalHits >= 1;

        if ((!structuralBasis || result.ClassificationConfidence < 90) &&
            !lexicalActionable &&
            !unresolvedLexicalActionable)
            return;

        model.Category = result.Category;
        model.Family = result.Family;
        model.SemanticType = result.SemanticType;
        model.Subtype = result.Subtype;
        model.IntelligenceScore = Math.Max(model.IntelligenceScore, result.ClassificationConfidence / 100d);

        var fusionEvidence = $"Semantic fusion: {result.Basis}; confidence {result.ClassificationConfidence}%";
        model.IntelligenceReason = string.IsNullOrWhiteSpace(model.IntelligenceReason)
            ? fusionEvidence.TrimEnd('"')
            : $"{model.IntelligenceReason}; {fusionEvidence.TrimEnd('"')}";
    }

    private static bool IsBuiltInOrUnresolved(string? category)
        => string.IsNullOrWhiteSpace(category) ||
           string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
           BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);
}
