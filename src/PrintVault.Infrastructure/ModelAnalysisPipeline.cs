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

    public IntelligenceResult Analyze(string path, string name)
        => analyzer.Analyze(path, name);

    public void Apply(ModelRecord model)
    {

        // Analyzer fields are populated by the caller so custom-category preservation remains explicit.
        if (string.IsNullOrWhiteSpace(model.Category)) model.Category = "Uncategorized";
        entityClassification.Apply(model);
        ApplyFusion(model);
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
