using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Applies high-confidence named-entity intelligence to the production model record.
/// Raw analyzer metadata is retained in IntelligenceReason; entity evidence is added as
/// a higher-priority semantic layer. Manual/custom categories remain authoritative.
/// This service only changes database metadata; it never renames or moves the physical .3mf file.
/// </summary>
public sealed class EntityClassificationService
{
    private readonly MultilingualEntityService entities = new();

    public bool Apply(ModelRecord model)
    {
        if (model is null || model.CategoryOverride) return false;

        var entity = entities.Recognize(model);
        if (entity is null || entity.Confidence < 95) return false;

        // A deliberate custom category is user intent even when CategoryOverride was not
        // explicitly set (for example, a category created in an earlier build). Built-in
        // categories remain eligible for semantic correction; custom categories do not.
        if (!BuiltInCategories.All.Contains(entity.Category, StringComparer.OrdinalIgnoreCase))
            return false;
        if (!BuiltInCategories.All.Contains(model.Category, StringComparer.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(model.Category))
            return false;

        var previousReason = model.IntelligenceReason ?? "";
        var entityEvidence = $"Named entity: {entity.EntityName} ({entity.Domain}); matched entity phrase; entity confidence: {entity.Confidence}%";

        // Entity recognition answers "what is depicted?" It does not, by itself, answer
        // "what kind of printable artifact is this?" Preserve an already-established
        // built-in artifact category such as HueForge, Keychains, Soap Holders, or
        // Functional. SemanticEvidenceFusionService performs the final dimensional
        // arbitration when the artifact is still Uncategorized.
        var categoryWasUnresolved = string.IsNullOrWhiteSpace(model.Category) ||
                                    string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase);
        if (categoryWasUnresolved)
        {
            model.Category = entity.Category;
            ApplySemanticDomain(model, entity);
            model.Subtype = entity.Subtype;
        }
        else
        {
            model.IntelligenceReason = string.IsNullOrWhiteSpace(previousReason)
                ? entityEvidence
                : $"{previousReason}; {entityEvidence}";
        }
        model.IntelligenceScore = Math.Max(model.IntelligenceScore, entity.Confidence / 100d);
        model.IntelligenceReason = string.IsNullOrWhiteSpace(previousReason)
            ? entityEvidence
            : $"{entityEvidence}; analyzer baseline: {previousReason}";
        model.SuggestedTags = AddTag(model.SuggestedTags, "entity:" + entity.EntityName);
        model.SuggestedTags = AddTag(model.SuggestedTags, "entity-domain:" + entity.Domain);
        model.Tags = AddTag(model.Tags, entity.Category);
        return true;
    }

    private static void ApplySemanticDomain(ModelRecord model, MultilingualEntityMatch entity)
    {
        switch (entity.Category)
        {
            case "Vehicles":
                model.Family = "Vehicle";
                model.SemanticType = "Vehicle";
                break;
            case "Buildings":
                model.Family = "Building";
                model.SemanticType = "Building";
                break;
            case "Figures & Characters":
                model.Family = "Figure";
                model.SemanticType = "Character";
                break;
            case "Nature & Scenery":
                model.Family = "Scenery";
                model.SemanticType = "Scenery";
                break;
            case "Tabletop Terrain":
                model.Family = "Terrain";
                model.SemanticType = "Terrain";
                break;
            case "Props & Accessories":
                model.Family = "Prop";
                model.SemanticType = "Prop";
                break;
            case "Functional":
                model.Family = "Functional";
                model.SemanticType = "Functional";
                break;
            case "Tools & Workshop":
                model.Family = "Tool";
                model.SemanticType = "Tool";
                break;
            case "Toys & Games":
                model.Family = "Toy";
                model.SemanticType = "Toy";
                break;
            case "Art & Decor":
                model.Family = "Art";
                model.SemanticType = "Art";
                break;
        }
    }

    private static string AddTag(string existing, string tag)
    {
        var values = (existing ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (!values.Contains(tag, StringComparer.OrdinalIgnoreCase)) values.Add(tag);
        return string.Join(", ", values);
    }
}
