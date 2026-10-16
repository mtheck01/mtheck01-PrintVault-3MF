using PrintVault.Core;

namespace PrintVault.Infrastructure;

internal sealed class EvidenceArbitrationStage : IModelIntelligenceStage
{
    public string Name => "EVIDENCE_ARBITRATION";

    public void Execute(ModelIntelligenceContext context)
    {
        var model = context.Model;
        var storedCategoryBeforeArbitration = model.Category;
        var result = context.Fusion;
        if (result is null) { context.StageEvidence.Add("EVIDENCE:NONE"); AddDiagnostic(context, "decision=NO_FUSION_RESULT"); return; }

        // Keep semantic dimensions searchable without conflating them with the primary
        // category: entity recognition supplies the depicted subject, while fusion may
        // independently establish the printable artifact's role and contextual signals.
        if (!string.IsNullOrWhiteSpace(result.SubjectIdentity))
            model.SuggestedTags = AddSuggestedTag(model.SuggestedTags, "subject:" + result.SubjectIdentity);
        if (!string.IsNullOrWhiteSpace(result.ArtifactRole))
            model.SuggestedTags = AddSuggestedTag(model.SuggestedTags, "artifact-role:" + result.ArtifactRole);
        foreach (var signal in result.ContextSignals.Take(4))
            model.SuggestedTags = AddSuggestedTag(model.SuggestedTags, "context:" + signal);

        // A manual category override protects only the category decision. Language,
        // entity and semantic evidence stages still run so metadata remains current.
        if (model.CategoryOverride)
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:CATEGORY_OVERRIDE_PROTECTED");
            AddDiagnostic(context, "decision=CATEGORY_OVERRIDE_PROTECTED");
            return;
        }

        var unresolved = string.IsNullOrWhiteSpace(model.Category) ||
                         string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(model.Category, "Unknown", StringComparison.OrdinalIgnoreCase);

        if (!unresolved && !BuiltInCategories.All.Contains(model.Category, StringComparer.OrdinalIgnoreCase))
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:CUSTOM_CATEGORY_PROTECTED");
            AddDiagnostic(context, "decision=CUSTOM_CATEGORY_PROTECTED");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Category) ||
            string.Equals(result.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:NO_FUSED_CATEGORY");
            AddDiagnostic(context, $"decision=NO_FUSED_CATEGORY; stored_category={model.Category}; fused_category={result.Category}; evidence_quality={result.EvidenceQuality}; confidence={result.ClassificationConfidence}");
            return;
        }

        var structuralBasis = result.Basis.Contains("Named entity", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Object role", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("artifact role evidence", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Organizer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Source-derived analyzer", StringComparison.OrdinalIgnoreCase) ||
                              result.Basis.Contains("Lexical identity + domain convergence", StringComparison.OrdinalIgnoreCase);
        var lexicalBasis = result.Basis.Contains("Lexical evidence", StringComparison.OrdinalIgnoreCase);
        var lexicalHits = result.Evidence.Count(e => e.StartsWith("Lexical cue:", StringComparison.OrdinalIgnoreCase));
        var actionable = (structuralBasis && result.ClassificationConfidence >= 85 && result.EvidenceQuality >= 75) ||
                         (lexicalBasis && result.ClassificationConfidence >= 70 && result.EvidenceQuality >= 40 && lexicalHits >= 1) ||
                         (unresolved && lexicalBasis && result.ClassificationConfidence >= 60 && result.EvidenceQuality >= 25 && lexicalHits >= 1);

        if (!actionable)
        {
            context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:REVIEW");
            AddDiagnostic(context, $"decision=REVIEW; stored_category={model.Category}; fused_category={result.Category}; unresolved={unresolved}; structural_basis={structuralBasis}; lexical_basis={lexicalBasis}; lexical_hits={lexicalHits}; identity={result.IdentityConfidence}; classification={result.ClassificationConfidence}; evidence_quality={result.EvidenceQuality}; gates=structural(confidence>=85,evidence>=75)|lexical(confidence>=70,evidence>=40,hits>=1)|unresolved_lexical(confidence>=60,evidence>=25,hits>=1)");
            return;
        }

        model.Category = result.Category;
        model.Family = result.Family;
        model.SemanticType = result.SemanticType;
        model.Subtype = result.Subtype;
        model.IntelligenceScore = Math.Max(model.IntelligenceScore, result.ClassificationConfidence / 100d);
        if (structuralBasis && result.ClassificationConfidence >= 85 && result.EvidenceQuality >= 75)
            model.RiskFlags = RemoveRiskFlag(model.RiskFlags, "Low semantic confidence");
        var evidence = $"Semantic fusion: {result.Basis}; confidence {result.ClassificationConfidence}%";
        model.IntelligenceReason = string.IsNullOrWhiteSpace(model.IntelligenceReason)
            ? evidence : $"{model.IntelligenceReason}; {evidence}";
        context.StageEvidence.Add($"EVIDENCE:{result.EvidenceQuality}:ACTIONABLE");
        AddDiagnostic(context, $"decision=ACTIONABLE; category={result.Category}; stored_category={storedCategoryBeforeArbitration}; structural_basis={structuralBasis}; lexical_basis={lexicalBasis}; lexical_hits={lexicalHits}; identity={result.IdentityConfidence}; classification={result.ClassificationConfidence}; evidence_quality={result.EvidenceQuality}");
    }

    private static void AddDiagnostic(ModelIntelligenceContext context, string diagnostic)
    {
        if (context.CaptureDiagnostics) context.StageDiagnostics.Add(diagnostic);
    }

    private static string RemoveRiskFlag(string riskFlags, string flag)
        => string.Join(", ", (riskFlags ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.Equals(value, flag, StringComparison.OrdinalIgnoreCase)));

    private static string AddSuggestedTag(string tags, string tag)
    {
        var values = (tags ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (!values.Contains(tag, StringComparer.OrdinalIgnoreCase)) values.Add(tag);
        return string.Join(", ", values);
    }
}
