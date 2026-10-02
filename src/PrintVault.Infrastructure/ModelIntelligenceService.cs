using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Builds a unified, read-only intelligence profile and discovers meaningful
/// relationships. Context signals such as HueForge canvas size, printer names and
/// variant markers are treated as metadata roles rather than model identity.
/// </summary>
public sealed class ModelIntelligenceService
{
    private readonly MultilingualEntityService entities = new();

    public ModelIntelligenceProfile BuildProfile(ModelRecord model, IReadOnlyList<ModelRecord> catalog)
    {
        var entity = entities.Recognize(model);
        var relationshipSet = FindRelatedInternal(model, catalog, 20);
        var duplicate = relationshipSet.Results.Any(x => x.Relationship == "Possible duplicate");

        var identityConfidence = entity?.Confidence ??
            (!string.Equals(model.OriginalLanguage, "Unknown", StringComparison.OrdinalIgnoreCase) && model.TranslationConfidence > 0
                ? model.TranslationConfidence
                : ClampPercent(model.IntelligenceScore));
        var classificationConfidence = entity?.Confidence ?? ClampPercent(model.IntelligenceScore);

        var tags = SplitTags(model.Tags)
            .Concat(SplitTags(model.SuggestedTags))
            .Append(model.Category)
            .Append(model.SemanticType)
            .Append(model.Subtype)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        var evidence = new List<string>();
        if (entity is not null)
        {
            evidence.Add($"Named entity: {entity.EntityName} ({entity.Domain}) — {entity.Confidence}%");
            if (!string.IsNullOrWhiteSpace(entity.Evidence)) evidence.Add(entity.Evidence);
        }
        if (!string.IsNullOrWhiteSpace(model.TranslationEvidence)) evidence.Add("Translation: " + model.TranslationEvidence);
        if (!string.IsNullOrWhiteSpace(model.IntelligenceReason)) evidence.Add("Analyzer: " + model.IntelligenceReason);
        if (!string.IsNullOrWhiteSpace(model.DuplicateGroup)) evidence.Add("Duplicate group: " + model.DuplicateGroup);

        var context = AnalyzeContext(model);
        if (context.IsHueForge) evidence.Add($"Context: HueForge{(context.CanvasSize is null ? "" : $" {context.CanvasSize}" )}");
        if (context.PrinterSignals.Count > 0) evidence.Add("Context: printer/device metadata — " + string.Join(", ", context.PrinterSignals));

        return new ModelIntelligenceProfile(
            model.Id, model.Path, model.Name, model.TranslatedTitle ?? "",
            entity?.EntityName ?? "", entity?.Domain ?? "",
            model.Category, model.SemanticType ?? "", model.Subtype ?? "", model.Family ?? "",
            identityConfidence, classificationConfidence, tags,
            string.Join("\n", evidence.Distinct(StringComparer.OrdinalIgnoreCase)),
            relationshipSet.TotalCandidates, relationshipSet.Results.Count, duplicate);
    }

    public IReadOnlyList<ModelRelationship> FindRelated(ModelRecord target, IReadOnlyList<ModelRecord> catalog, int maxResults = 12)
        => FindRelatedInternal(target, catalog, maxResults).Results;

    /// <summary>Analyzes one pair without mutating either record. Used by the explainability challenge.</summary>
    public ModelRelationship? AnalyzeRelationship(ModelRecord target, ModelRecord candidate)
    {
        if (ReferenceEquals(target, candidate) || string.Equals(target.Path, candidate.Path, StringComparison.OrdinalIgnoreCase)) return null;
        return ScoreRelationship(target, candidate);
    }

    public ModelContextSignals AnalyzeContext(ModelRecord model)
    {
        var text = string.Join(" ", model.Name ?? "", model.TranslatedTitle ?? "", model.Tags ?? "", model.SuggestedTags ?? "", model.Category ?? "", model.SemanticType ?? "");
        var lower = text.ToLowerInvariant();
        var hueForge = string.Equals(model.Category, "HueForge", StringComparison.OrdinalIgnoreCase) ||
                       ContainsAny(lower, "hueforge", "hue forge");
        var size = ExtractCanvasSize(text);
        var printers = PrinterSignals(lower);
        var variants = VariantSignals(lower);
        var identityTokens = BuildIdentityTokens(model);
        var contextTokens = BuildContextTokens(model);
        return new ModelContextSignals(hueForge, size, printers, variants, identityTokens, contextTokens);
    }

    private RelationshipIndex? relationshipIndex;
    private IReadOnlyList<ModelRecord>? indexedCatalog;

    private (IReadOnlyList<ModelRelationship> Results, int TotalCandidates) FindRelatedInternal(ModelRecord target, IReadOnlyList<ModelRecord> catalog, int maxResults)
    {
        EnsureRelationshipIndex(catalog);
        var candidateIds = GetCandidateIds(target, catalog);
        if (candidateIds.Count == 0)
            return (Array.Empty<ModelRelationship>(), 0);

        var results = new List<ModelRelationship>();
        foreach (var candidate in catalog)
        {
            if (candidate.Id == target.Id || string.Equals(candidate.Path, target.Path, StringComparison.OrdinalIgnoreCase)) continue;
            if (!candidateIds.Contains(candidate.Id)) continue;
            var relationship = ScoreRelationship(target, candidate);
            if (relationship is not null) results.Add(relationship);
        }

        var total = results.Count;
        var ordered = results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Relationship, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Model.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();
        return (ordered, total);
    }


    public int GetRelationshipCandidateCount(ModelRecord target, IReadOnlyList<ModelRecord> catalog)
        => GetCandidateIds(target, catalog).Count;

    private HashSet<long> GetCandidateIds(ModelRecord target, IReadOnlyList<ModelRecord> catalog)
    {
        EnsureRelationshipIndex(catalog);
        var candidateIds = new HashSet<long>();
        if (relationshipIndex is null) return candidateIds;
        if (relationshipIndex.EntityIds.TryGetValue(EntityKey(target), out var entityIds))
            candidateIds.UnionWith(entityIds);
        foreach (var token in BuildIdentityTokens(target))
            if (relationshipIndex.TokenIds.TryGetValue(token, out var ids))
                candidateIds.UnionWith(ids);
        if (!string.IsNullOrWhiteSpace(target.Hash) && relationshipIndex.HashIds.TryGetValue(target.Hash, out var hashIds))
            candidateIds.UnionWith(hashIds);
        candidateIds.Remove(target.Id);
        return candidateIds;
    }

    private void EnsureRelationshipIndex(IReadOnlyList<ModelRecord> catalog)
    {
        if (ReferenceEquals(indexedCatalog, catalog) && relationshipIndex is not null) return;
        var entityIds = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        var tokenIds = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        var hashIds = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in catalog)
        {
            var entity = entities.Recognize(model);
            if (entity is not null)
                AddIndex(entityIds, EntityKey(model), model.Id);
            foreach (var token in BuildIdentityTokens(model))
                AddIndex(tokenIds, token, model.Id);
            if (!string.IsNullOrWhiteSpace(model.Hash))
                AddIndex(hashIds, model.Hash, model.Id);
        }

        // High-frequency identity words are not useful retrieval keys. Keeping them
        // out of the index is what turns the full-library scan from near-exhaustive
        // comparison into selective candidate generation.
        foreach (var key in tokenIds.Where(x => x.Value.Count > 24).Select(x => x.Key).ToArray())
            tokenIds.Remove(key);

        indexedCatalog = catalog;
        relationshipIndex = new RelationshipIndex(entityIds, tokenIds, hashIds);
    }

    private static void AddIndex(Dictionary<string, HashSet<long>> index, string key, long id)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!index.TryGetValue(key, out var ids)) index[key] = ids = new HashSet<long>();
        ids.Add(id);
    }

    private string EntityKey(ModelRecord model)
    {
        var entity = entities.Recognize(model);
        return entity is null ? "" : entity.EntityName;
    }

    private ModelRelationship? ScoreRelationship(ModelRecord target, ModelRecord candidate)
    {
        var targetEntity = entities.Recognize(target);
        var candidateEntity = entities.Recognize(candidate);
        var targetContext = AnalyzeContext(target);
        var candidateContext = AnalyzeContext(candidate);

        var score = 0;
        var relationship = "Related model";
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(target.Hash) && !string.IsNullOrWhiteSpace(candidate.Hash) &&
            string.Equals(target.Hash, candidate.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return new ModelRelationship(candidate, 100, "Possible duplicate", "Exact content hash match");
        }

        if (targetEntity is not null && candidateEntity is not null &&
            string.Equals(targetEntity.EntityName, candidateEntity.EntityName, StringComparison.OrdinalIgnoreCase))
        {
            score = 98;
            relationship = "Same entity";
            reasons.Add($"Both identify as {targetEntity.EntityName}");
            if (targetContext.IsHueForge && candidateContext.IsHueForge && targetContext.CanvasSize is not null && candidateContext.CanvasSize is not null &&
                !string.Equals(targetContext.CanvasSize, candidateContext.CanvasSize, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"HueForge size variant: {targetContext.CanvasSize} vs {candidateContext.CanvasSize}");
            return new ModelRelationship(candidate, score, relationship, string.Join("; ", reasons));
        }

        // Once both models have confident, different named entities, do not allow
        // generic shared vocabulary (spacecraft/fighter/domain/type terms, etc.) to
        // manufacture a relationship. Distinct named entities require an explicit
        // same-entity match; broader relationships remain available for models that
        // do not have conflicting named-entity identities.
        if (targetEntity is not null && candidateEntity is not null &&
            !string.Equals(targetEntity.EntityName, candidateEntity.EntityName, StringComparison.OrdinalIgnoreCase))
            return null;

        var identityOverlap = IdentityOverlap(targetContext.IdentityTokens, candidateContext.IdentityTokens);
        var identityIntersection = IdentityIntersectionCount(targetContext.IdentityTokens, candidateContext.IdentityTokens);
        var rareIntersection = DistinctiveIntersectionCount(targetContext.IdentityTokens, candidateContext.IdentityTokens);
        var printerOverlapSignals = targetContext.PrinterSignals.Intersect(candidateContext.PrinterSignals, StringComparer.OrdinalIgnoreCase).ToArray();
        var printerOnlyContext = printerOverlapSignals.Length > 0 && identityIntersection == 0;

        // Identity gate: context, category, franchise/domain, size, printer and variant
        // markers cannot establish a relationship. At least one strong identity anchor
        // is required; ambiguous shared vocabulary needs two independent anchors.
        var sameDomain = targetEntity is not null && candidateEntity is not null &&
            string.Equals(targetEntity.Domain, candidateEntity.Domain, StringComparison.OrdinalIgnoreCase);

        if (targetEntity is not null && candidateEntity is not null && sameDomain && rareIntersection >= 2 && identityOverlap >= 0.50)
        {
            score = 76;
            relationship = "Related model";
            reasons.Add($"Same named-entity domain: {targetEntity.Domain}");
            reasons.Add($"Distinctive identity overlap ({identityOverlap:P0}); {rareIntersection} shared anchors");
        }

        if (rareIntersection >= 2 && identityOverlap >= 0.50)
        {
            score = Math.Max(score, 90);
            relationship = relationship == "Related model" ? "Likely variant" : relationship;
            reasons.Add($"Strong identity-token overlap ({identityOverlap:P0}); {rareIntersection} shared identity anchors");
        }
        else if (rareIntersection >= 1 && identityIntersection >= 2 && identityOverlap >= 0.45 && !sameDomain)
        {
            // One distinctive anchor plus another supporting token can be enough for
            // ordinary non-entity variants, but only when context is not doing the work.
            score = Math.Max(score, 78);
            relationship = "Likely variant";
            reasons.Add($"Distinctive identity anchor + supporting token overlap ({identityOverlap:P0})");
        }

        if (targetContext.IsHueForge && candidateContext.IsHueForge)
        {
            if (rareIntersection >= 1 && identityIntersection >= 2)
            {
                score = Math.Max(score, targetContext.CanvasSize == candidateContext.CanvasSize ? 88 : 82);
                relationship = "Likely variant";
                reasons.Add("Shared HueForge context");
                if (targetContext.CanvasSize is not null && candidateContext.CanvasSize is not null)
                    reasons.Add($"HueForge canvas: {targetContext.CanvasSize} vs {candidateContext.CanvasSize}");
            }
            else
            {
                return null;
            }
        }

        // Printer/device metadata is never identity evidence. A printer-only overlap,
        // or generic vocabulary amplified by a printer profile, is explicitly rejected.
        if (printerOnlyContext) return null;
        if (printerOverlapSignals.Length > 0 && targetEntity is not null && candidateEntity is not null &&
            !string.Equals(targetEntity.EntityName, candidateEntity.EntityName, StringComparison.OrdinalIgnoreCase) &&
            relationship == "Likely variant")
            return null;

        if (score < 68) return null;
        return new ModelRelationship(candidate, score, relationship, string.Join("; ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static int DistinctiveIntersectionCount(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        return a.Count(x => b.Contains(x) && IsDistinctiveIdentityToken(x));
    }

    private static bool IsDistinctiveIdentityToken(string token)
    {
        var t = token.ToLowerInvariant();
        if (t.Length < 4 || IsGenericToken(t) || IsContextToken(t) || IsVariantToken(t)) return false;
        // Very short/common construction words are weak identity anchors.
        return t switch
        {
            "holder" or "tower" or "front" or "back" or "base" or "part" or "parts" or "template" or "plate" or "stand" or "mount" => false,
            _ => true
        };
    }

    private sealed record RelationshipIndex(
        Dictionary<string, HashSet<long>> EntityIds,
        Dictionary<string, HashSet<long>> TokenIds,
        Dictionary<string, HashSet<long>> HashIds);

    private static HashSet<string> BuildIdentityTokens(ModelRecord model)
    {
        var text = string.Join(' ', model.Name ?? "", model.TranslatedTitle ?? "", model.Tags ?? "", model.SuggestedTags ?? "");
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in SplitRawTokens(text))
        {
            var token = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (token.Length < 3 || IsGenericToken(token) || IsContextToken(token) || IsVariantToken(token)) continue;
            tokens.Add(token.ToLowerInvariant());
        }
        return tokens;
    }

    private static HashSet<string> BuildContextTokens(ModelRecord model)
    {
        var text = string.Join(' ', model.Name ?? "", model.TranslatedTitle ?? "", model.Tags ?? "", model.SuggestedTags ?? "", model.Category ?? "");
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in SplitRawTokens(text))
        {
            var token = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (IsContextToken(token)) tokens.Add(token.ToLowerInvariant());
        }
        return tokens;
    }

    private static IEnumerable<string> SplitRawTokens(string text)
        => text.Split(new[] { ' ', '_', '-', '.', '+', '(', ')', '[', ']', ',', ';', ':', '/', '\\', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static double IdentityOverlap(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(x => b.Contains(x));
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static int IdentityIntersectionCount(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        return a.Count(x => b.Contains(x));
    }

    private static bool IsGenericToken(string token)
    {
        var t = token.ToLowerInvariant();
        return GenericTokens.Contains(t) || t.Length <= 2;
    }

    private static bool IsContextToken(string token)
    {
        var t = token.ToLowerInvariant();
        if (ContextTokens.Contains(t)) return true;
        return Regex.IsMatch(t, @"^\d{2,4}x\d{2,4}$", RegexOptions.CultureInvariant);
    }

    private static bool IsVariantToken(string token)
    {
        var t = token.ToLowerInvariant();
        if (VariantTokens.Contains(t)) return true;
        return Regex.IsMatch(t, @"^(v|ver|version|mk|rev|r)\d+[a-z]*$", RegexOptions.CultureInvariant);
    }

    private static string? ExtractCanvasSize(string text)
    {
        var match = Regex.Match(text, @"(?<!\d)(\d{2,4})\s*[x×]\s*(\d{2,4})(?:\s*mm)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? $"{match.Groups[1].Value}x{match.Groups[2].Value}" : null;
    }

    private static IReadOnlyList<string> PrinterSignals(string lower)
    {
        var list = new List<string>();
        foreach (var signal in new[] { "x1c", "x1 carbon", "bambu", "p1s", "p1p", "a1", "a1 mini", "kobra", "kobra 3", "kobra3", "ender", "prusa", "ams" })
            if (lower.Contains(signal, StringComparison.OrdinalIgnoreCase)) list.Add(signal);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> VariantSignals(string lower)
        => VariantTokens.Where(v => lower.Contains(v, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "model","print","printed","printing","file","files","modelos","stl","3mf","entity","domain",
        "front","back","left","right","top","bottom","large","small","mini","full","part","parts",
        "profile","test","layer","layers","quality","good","final","new","old","copy","upload","uploaded",
        "color","colour","black","white","blue","red","green","yellow","pink","orange","purple","gray","grey",
        "bambu","printer","printers","kobra","prusa","ender","ams"
    };

    private static readonly HashSet<string> ContextTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "hueforge","hue","forge","x1c","p1s","p1p","a1","kobra","kobra3","ender","prusa","ams","bambu"
    };

    private static readonly HashSet<string> VariantTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "v1","v2","v3","v4","v5","ver1","ver2","version1","version2","mk1","mk2","mk3","rev1","rev2","r1","r2",
        "front","back","left","right","large","small","mini","full","part","parts","color","colour","black","white","blue","red"
    };

    private static IEnumerable<string> SplitTags(string? text)
        => (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int ClampPercent(double value) => (int)Math.Clamp(Math.Round(value * 100d), 0d, 100d);
}

public sealed record ModelContextSignals(
    bool IsHueForge,
    string? CanvasSize,
    IReadOnlyList<string> PrinterSignals,
    IReadOnlyList<string> VariantSignals,
    IReadOnlySet<string> IdentityTokens,
    IReadOnlySet<string> ContextTokens);
