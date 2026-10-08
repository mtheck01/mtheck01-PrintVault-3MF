using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal sealed class EvidenceArbitrationStage : IModelIntelligenceStage
{
    public string Name => "EVIDENCE_ARBITRATION";

    public void Execute(ModelIntelligenceContext context)
    {
        var model = context.Model;
        var result = context.Fusion;
        if (result is null) { context.StageEvidence.Add("EVIDENCE:NONE"); return; }

        // A manual category override protects only the category decision. Language,
        // entity and semantic evidence stages still run so metadata remains current.
        if (model.CategoryOverride)
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:CATEGORY_OVERRIDE_PROTECTED");
            return;
        }

        var unresolved = string.IsNullOrWhiteSpace(model.Category) ||
                         string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(model.Category, "Unknown", StringComparison.OrdinalIgnoreCase);

        if (!unresolved && !BuiltInCategories.All.Contains(model.Category, StringComparer.OrdinalIgnoreCase))
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:CUSTOM_CATEGORY_PROTECTED");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Category) ||
            string.Equals(result.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:NO_FUSED_CATEGORY");
            return;
        }

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

        if (!actionable) { context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:REVIEW"); return; }

        model.Category = result.Category;
        model.Family = result.Family;
        model.SemanticType = result.SemanticType;
        model.Subtype = result.Subtype;
        model.IntelligenceScore = Math.Max(model.IntelligenceScore, result.ClassificationConfidence / 100d);
        var evidence = $"Semantic fusion: {result.Basis}; confidence {result.ClassificationConfidence}%";
        model.IntelligenceReason = string.IsNullOrWhiteSpace(model.IntelligenceReason)
            ? evidence : $"{model.IntelligenceReason}; {evidence}";
        context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:ACTIONABLE");
    }
}
