using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record SemanticEvidenceFusionResult(
    string Category,
    string SemanticType,
    string Subtype,
    string Family,
    int IdentityConfidence,
    int ClassificationConfidence,
    int EvidenceQuality,
    string Basis,
    bool ReviewRequired,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Deterministic, read-only evidence fusion. It does not invent named entities or
/// write classifications. Instead it combines independent evidence already available
/// in a ModelRecord with conservative lexical domain cues and context roles. Context
/// such as HueForge size and printer names can explain a model, but cannot establish
/// model identity by themselves.
/// </summary>
public sealed class SemanticEvidenceFusionService
{
    // Analyzer semantics are trusted only when they are backed by the rebuilt analyzer's
    // source-derived semantic corpus. The analyzer no longer scans arbitrary XML/config text
    // for model identity (9.0.31), so its explicit semantic result can be used as a separate
    // classification channel without re-feeding classifier tags/reasons into lexical matching.
    private static readonly HashSet<string> AnalyzerCategories = new(BuiltInCategories.All, StringComparer.OrdinalIgnoreCase);

    private static bool TryGetSourceDerivedAnalyzerEvidence(ModelRecord model, out string category)
    {
        category = "";
        if (string.IsNullOrWhiteSpace(model.IntelligenceReason) ||
            model.IntelligenceScore < 0.35 ||
            string.IsNullOrWhiteSpace(model.SemanticType)) return false;

        // ThreeMfAnalyzer writes semantic reasons as "Category: ...". Only accept an
        // explicit built-in category prefix; free-form reasons never establish identity.
        var reason = model.IntelligenceReason.Trim();
        var colon = reason.IndexOf(':');
        if (colon <= 0) return false;
        var candidate = reason[..colon].Trim();
        if (!AnalyzerCategories.Contains(candidate)) return false;
        category = candidate;
        return true;
    }

    private static readonly (string[] Terms, string Category, string Type, string Subtype, string Family, int Weight, string Label)[] Cues =
    {
        (new[]{"a-10 thunderbolt","a10 thunderbolt","a-10","a10","a-4 skyhawk","skyhawk","airbus","aircraft","airplane","fighter jet","fighter","little bird","ah-64","apache","harrier","p-38","p-51","pby-5a","f-111","an-225","a400m"}, "Vehicles", "Vehicle", "Aircraft", "Vehicle", 24, "aviation terminology"),
        (new[]{"helicopter","chopper","drone","uav"}, "Vehicles", "Vehicle", "Aircraft / Rotorcraft", "Vehicle", 20, "rotorcraft terminology"),
        (new[]{"truck","lorry","semi","pickup"}, "Vehicles", "Vehicle", "Truck", "Vehicle", 16, "truck terminology"),
        (new[]{"car","automobile","sedan","coupe","suv"}, "Vehicles", "Vehicle", "Car", "Vehicle", 14, "automotive terminology"),
        (new[]{"motorcycle","motorbike"}, "Vehicles", "Vehicle", "Motorcycle", "Vehicle", 16, "motorcycle terminology"),
        (new[]{"wrench","screwdriver","pliers","tool organizer","socket tray"}, "Tools & Workshop", "Tool", "Workshop Tool", "Tool", 18, "workshop terminology"),
        (new[]{"shelf","shelving","organizer","holder","mount"}, "Functional", "Functional", "Organizer / Holder", "Functional", 10, "functional terminology"),
        (new[]{"dragon","dog","cat","horse","goose","sheep","raccoon","snail","octopus","axolotl"}, "Figures & Characters", "Figure", "Creature", "Figure", 14, "creature terminology"),
        (new[]{"castle","house","building","tower","booknook"}, "Buildings", "Building", "Structure", "Building", 14, "building terminology"),
        (new[]{"terrain","dungeon terrain","dungeon tile","wargaming terrain"}, "Tabletop Terrain", "Terrain", "Tabletop Terrain", "Terrain", 18, "terrain terminology")
    };

    private static readonly string[] ContextTerms = { "hueforge", "hue forge", "200x200", "200x200mm", "front", "back", "x1c", "p1s", "p1p", "kobra", "ams", "bambu" };

    // Identity-bearing lexical patterns are intentionally broader than the named-entity
    // dictionary. They let model/designation terminology reinforce domain evidence without
    // inventing a named entity. Context terms remain isolated from identity scoring.
    private static readonly string[] AviationIdentityTerms =
    {
        "a-10", "a10", "a-10 thunderbolt", "a10 thunderbolt", "a-4", "a4", "a-4 skyhawk",
        "a400m", "airbus a400m", "ah-6", "ah-64", "apache-ah64", "av-8b", "f-111", "f-16",
        "f-18", "f-22", "f-35", "p-38", "p-51", "pby-5a", "an-225", "little bird", "harrier", "skyhawk"
    };

    private static readonly string[] AviationDomainTerms =
    {
        "aircraft", "airplane", "aviation", "fighter jet", "fighter", "airbus", "helicopter", "chopper", "drone", "uav"
    };

    // Model-name descriptors can provide the second aviation dimension when a filename
    // contains a designation plus its well-known aircraft name but omits a generic
    // domain word (for example "A-10 Thunderbolt" or "F-16 Fighting Falcon").
    // These descriptors are only treated as domain corroboration when an aviation
    // designation is already present; they cannot establish aviation identity alone.
    private static readonly string[] AviationModelDescriptorTerms =
    {
        "thunderbolt", "fighting falcon", "skyhawk", "apache", "hornet", "viper",
        "raptor", "lightning", "harrier", "tomcat", "eagle", "mustang", "corsair",
        "spitfire", "warhawk", "airacobra", "cobra", "hind", "hawk", "falcon"
    };

    // Role-aware object phrases: the noun after a purpose/storage construction is
    // often a referenced object, not the identity of the printed model itself.
    private static readonly string[] OrganizerRoleTerms =
    {
        "tool organizer", "socket organizer", "socket tray", "wrench organizer",
        "wrench holder", "wrench rack", "socket holder", "socket rack",
        "screwdriver organizer", "screwdriver holder", "pliers organizer", "tool tray"
    };

    private static readonly string[] ReferencedToolTerms =
    {
        "wrench", "socket", "screwdriver", "pliers"
    };

    public SemanticEvidenceFusionResult Fuse(ModelRecord model, MultilingualEntityMatch? entity = null)
    {
        entity ??= new MultilingualEntityService().Recognize(model);
        // Only physical/source-derived identity text may drive lexical inference. Tags,
        // suggested tags, and IntelligenceReason are derived outputs of the classifier and
        // must never be fed back as evidence; doing so creates circular classification.
        var text = Normalize(string.Join(" ", model.Name ?? "", model.TranslatedTitle ?? ""));
        var evidence = new List<string>();
        var organizerRoleHits = OrganizerRoleTerms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var referencedToolHits = ReferencedToolTerms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var isToolOrganizer = organizerRoleHits.Length > 0;

        if (entity is not null)
            evidence.Add($"Named entity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
        if (!string.IsNullOrWhiteSpace(model.TranslationEvidence) && model.TranslationConfidence > 0)
            evidence.Add($"Translation evidence: {model.TranslationConfidence}%");
        if (!string.IsNullOrWhiteSpace(model.IntelligenceReason))
            evidence.Add("Analyzer evidence present");
        if (!string.IsNullOrWhiteSpace(model.SemanticType) || !string.IsNullOrWhiteSpace(model.Subtype))
            evidence.Add($"Stored classification: {model.Category} / {model.SemanticType} / {model.Subtype}");

        var cueHits = Cues
            .Select(c => (Cue: c, Hits: c.Terms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(x => x.Hits.Length > 0)
            .OrderByDescending(x => x.Cue.Weight * Math.Min(2, x.Hits.Length))
            .ToList();

        var best = cueHits.FirstOrDefault();
        var aviationIdentityHits = AviationIdentityTerms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var aviationDomainHits = AviationDomainTerms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var aviationDescriptorHits = aviationIdentityHits.Length > 0
            ? AviationModelDescriptorTerms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (aviationDomainHits.Length == 0 && aviationDescriptorHits.Length > 0)
            aviationDomainHits = aviationDescriptorHits;
        var hasTranslation = model.TranslationConfidence >= 70 && !string.IsNullOrWhiteSpace(model.TranslatedTitle);
        var hasSourceDerivedAnalyzer = TryGetSourceDerivedAnalyzerEvidence(model, out var analyzerCategory);
        if (hasSourceDerivedAnalyzer)
            evidence.Add($"Source-derived analyzer semantics: {analyzerCategory} / {model.SemanticType} / {model.Subtype} ({model.IntelligenceScore:P0})");

        string category = model.Category ?? "";
        string type = model.SemanticType ?? "";
        string subtype = model.Subtype ?? "";
        string family = model.Family ?? "";
        var classification = Clamp(model.IntelligenceScore);
        var identity = entity?.Confidence ?? (hasTranslation ? model.TranslationConfidence : classification);
        var basis = "Stored classification";

        if (isToolOrganizer)
        {
            // Organizer phrases describe the model's primary role. A matched tool
            // entity (for example Wrench) is retained as referenced-object evidence,
            // but it must not replace the organizer as the model identity.
            category = "Tools & Workshop";
            type = "Organizer";
            subtype = "Tool Organizer";
            family = "Functional";
            identity = Math.Max(identity, 88);
            classification = Math.Max(classification, 94);
            basis = "Object role + referenced tool evidence";
            evidence.Add($"Organizer role: {string.Join(", ", organizerRoleHits)}");
            if (referencedToolHits.Length > 0)
                evidence.Add($"Referenced tool: {string.Join(", ", referencedToolHits)}");
            if (entity is not null)
                evidence.Add($"Referenced entity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
        }
        else if (entity is not null)
        {
            category = entity.Category; subtype = entity.Subtype; type = InferType(entity.Category, entity.Subtype); family = InferFamily(entity.Category);
            identity = entity.Confidence;
            classification = Math.Max(classification, entity.Confidence);
            basis = "Named entity + classification mapping";
        }
        else if (cueHits.Count > 0 && best.Hits.Length > 0)
        {
            var cue = best.Cue;
            category = cue.Category; type = cue.Type; subtype = cue.Subtype; family = cue.Family;
            var cueConfidence = Math.Min(92, 50 + cue.Weight + Math.Min(10, (best.Hits.Length - 1) * 5));
            classification = Math.Max(classification, cueConfidence);
            identity = Math.Max(identity, Math.Min(82, cueConfidence - 8));
            basis = $"Lexical evidence: {cue.Label}";
            evidence.Add($"Lexical cue: {string.Join(", ", best.Hits)}");
        }
        else if (hasSourceDerivedAnalyzer)
        {
            // The analyzer result is now a clean source-derived semantic channel. Reuse it
            // only as a fallback when the independent lexical/entity layers have no stronger
            // inference. Never use Tags, SuggestedTags, or this fusion result as evidence.
            category = analyzerCategory;
            type = string.IsNullOrWhiteSpace(model.SemanticType) ? type : model.SemanticType;
            subtype = string.IsNullOrWhiteSpace(model.Subtype) ? subtype : model.Subtype;
            family = string.IsNullOrWhiteSpace(model.Family) ? family : model.Family;
            var analyzerConfidence = Math.Clamp(70 + (int)Math.Round(model.IntelligenceScore * 25), 70, 95);
            classification = Math.Max(classification, analyzerConfidence);
            identity = Math.Max(identity, Math.Clamp(analyzerConfidence - 4, 66, 91));
            basis = "Source-derived analyzer semantics";
        }

        // Evidence convergence: combine distinct semantic dimensions rather than requiring
        // a named entity. These dimensions are still conservative; context-only terms never
        // contribute to identity or classification confidence.
        var convergence = 0;
        var convergenceLabels = new List<string>();
        if (aviationIdentityHits.Length > 0)
        {
            convergence += 32;
            convergenceLabels.Add($"identity designation: {string.Join(", ", aviationIdentityHits)}");
        }
        if (aviationDomainHits.Length > 0)
        {
            convergence += 20;
            convergenceLabels.Add($"domain terminology: {string.Join(", ", aviationDomainHits)}");
        }
        // Analyzer output and stored classification are derived from the same underlying
        // file evidence. They are recorded for diagnostics, but never add independent
        // convergence points.
        if (hasTranslation)
        {
            convergence += 18;
            convergenceLabels.Add("translation evidence");
        }
        if (hasSourceDerivedAnalyzer)
        {
            // This is not a lexical feedback loop: it is the explicit result of the
            // source-derived analyzer channel. Give it bounded weight and only one dimension.
            convergence += 24;
            convergenceLabels.Add("source-derived analyzer semantics");
            classification = Math.Max(classification, Math.Clamp(70 + (int)Math.Round(model.IntelligenceScore * 25), 70, 95));
            identity = Math.Max(identity, Math.Clamp(66 + (int)Math.Round(model.IntelligenceScore * 25), 66, 91));
        }
        if (entity is not null)
            convergence += Math.Max(0, Math.Min(35, entity.Confidence - 60));

        var aviationConvergence = aviationIdentityHits.Length > 0 && aviationDomainHits.Length > 0;

        // A recognized aviation designation plus an independent model/domain descriptor
        // is sufficient to establish the broad aircraft classification even when the
        // lexical cue table does not contain that exact model name (for example F-16).
        // This is a generalized classification rule, not a per-model exception.
        if (aviationConvergence && entity is null)
        {
            category = "Vehicles";
            type = "Vehicle";
            subtype = "Aircraft";
            family = "Vehicle";
            identity = Math.Max(identity, 88);
            classification = Math.Max(classification, 90);
            basis = "Lexical identity + domain convergence";
        }

        var quality = Math.Clamp(25 + convergence, 0, 100);
        if (hasSourceDerivedAnalyzer)
            quality = Math.Max(quality, Math.Clamp(55 + (int)Math.Round(model.IntelligenceScore * 35), 55, 90));
        if (entity is not null)
            quality = Math.Max(quality, Math.Min(100, entity.Confidence));

        // General convergence gate: a model/designation that carries a known aviation
        // identity-bearing term AND an independent aviation-domain term is strong semantic
        // evidence even when the named-entity dictionary does not recognize the exact model.
        // This is intentionally domain-general within the aviation evidence family; it is
        // not a special case for A-10. Context-only signals still contribute nothing here.
        if (aviationConvergence)
        {
            identity = Math.Max(identity, 88);
            classification = Math.Max(classification, 90);
            quality = Math.Max(quality, 85);
            basis = entity is null ? "Lexical identity + domain convergence" : basis;
        }

        if (convergenceLabels.Count >= 2)
            evidence.Add($"Evidence convergence: {string.Join(" + ", convergenceLabels)}");

        var context = ContextTerms.Count(t => ContainsPhrase(text, t));
        if (context > 0)
            evidence.Add($"Context-only signals: {context} (not identity evidence)");

        // A conflicting stored category keeps a lexical inference in REVIEW. Named entities
        // remain authoritative because they already carry domain-specific identity confidence.
        var storedConflict = entity is null && !string.IsNullOrWhiteSpace(model.Category) &&
                             !string.IsNullOrWhiteSpace(category) &&
                             !string.Equals(model.Category, category, StringComparison.OrdinalIgnoreCase);
        // A strong lexical/domain convergence is independently corroborated unless it
        // conflicts with an existing stored category. This removes the prior dependence
        // on the named-entity dictionary while retaining the conflict safety gate.
        var analyzerConvergenceClearsReview = hasSourceDerivedAnalyzer && !storedConflict &&
                                              classification >= 85 && quality >= 75;
        var convergenceClearsReview = aviationConvergence && !storedConflict;
        var review = storedConflict || (!convergenceClearsReview && !analyzerConvergenceClearsReview && entity is null && (identity < 85 || classification < 85));
        if (review) evidence.Add(storedConflict
            ? "Review recommended: inferred classification conflicts with stored category"
            : "Review recommended: evidence is incomplete or not independently corroborated");
        else evidence.Add("Evidence is independently corroborated");

        return new SemanticEvidenceFusionResult(category, type, subtype, family,
            Math.Clamp(identity, 0, 100), Math.Clamp(classification, 0, 100), quality, basis, review,
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string InferType(string category, string subtype)
        => category switch
        {
            "Vehicles" => "Vehicle",
            "Buildings" => "Building",
            "Figures & Characters" => "Character",
            "Tools & Workshop" => "Tool",
            "Tabletop Terrain" => "Terrain",
            _ => subtype.Contains("Tool", StringComparison.OrdinalIgnoreCase) ? "Tool" : ""
        };

    private static string InferFamily(string category)
        => category switch
        {
            "Vehicles" => "Vehicle",
            "Buildings" => "Building",
            "Figures & Characters" => "Figure",
            "Tools & Workshop" => "Tool",
            "Tabletop Terrain" => "Terrain",
            "Functional" => "Functional",
            _ => ""
        };

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (phrase.Any(char.IsLetterOrDigit) && phrase.All(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '-' or '_'))
        {
            var pattern = $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(phrase.Replace('_', ' '))}(?![\\p{{L}}\\p{{N}}])";
            return Regex.IsMatch(text.Replace('_', ' '), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
        => Regex.Replace(value.Replace('+', ' ').Replace('_', ' '), @"\s+", " ").Trim().ToLowerInvariant();

    private static int Clamp(double value) => (int)Math.Clamp(Math.Round(value), 0, 100);
}
