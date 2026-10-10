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
/// Deterministic semantic arbitration across three distinct dimensions:
/// subject identity (what the model depicts), artifact/function (what printable object it is),
/// and catalog category (where PrintVault organizes that artifact). Subject identity must not
/// overwrite a stronger artifact classification; a Pikachu HueForge tile is still HueForge,
/// and an AT-AT keychain is still a Keychain.
/// </summary>
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

    // Broad, high-precision lexical evidence recovered from the whole-library forensic
    // pass. These signals use model title/translated title only; folders, tags and stored
    // classifications are never used as lexical evidence.
    // Broad, high-precision lexical evidence recovered from the whole-library forensic
    // pass. These signals use model title/translated title only; folders, tags and stored
    // classifications are never used as lexical evidence.
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
        (new[]{"terrain","dungeon terrain","dungeon tile","wargaming terrain"}, "Tabletop Terrain", "Terrain", "Tabletop Terrain", "Terrain", 18, "terrain terminology"),
        (new[]{"figurine","miniature","minifig","character","superhero","villain","soldier","robot","dragon","dinosaur","horse","dog","cat","cow","seal","snake","snail","otter","manatee","hedgehog","skunk","lizard","bird","elephant","bulldog","falkor"}, "Figures & Characters", "Figure", "Character", "Figure", 17, "character/creature terminology"),
        (new[]{"articulated dragon","articulated horse","articulated dog","articulated cat","articulated snake","articulated otter","articulated manatee","articulated hedgehog","articulated skunk","articulated lizard","articulated bird","articulated cow","articulated seal"}, "Figures & Characters", "Figure", "Articulated Creature", "Figure", 22, "articulated creature terminology"),
        (new[]{"aircraft","airplane","helicopter","fighter jet","glider","drone","uav","tank","tractor","truck","pickup truck","semi truck","car","motorcycle","motorbike","bicycle","scooter","ship","boat","train","locomotive","rocket","rover","excavator","bulldozer","forklift","crane","b-wing","snowspeeder","sea fury","fokker"}, "Vehicles", "Vehicle", "Vehicle", "Vehicle", 18, "vehicle terminology"),
        (new[]{"bracket","adapter","mount","hinge","enclosure","replacement","connector","spacer","gear","bearing","fixture","socket tray","tool tray","holder","rack"}, "Functional", "Functional", "Functional Part", "Functional", 18, "functional terminology"),
        (new[]{"house","building","castle","tavern","church","temple","tower","fort","ruin","cabin","barn","shed"}, "Buildings", "Building", "Structure", "Building", 18, "building terminology"),
        (new[]{"tree","forest","rock","boulder","mountain","cliff","hill","grass","bush","plant","foliage","mushroom","waterfall"}, "Nature & Scenery", "Scenery", "Natural Scenery", "Nature", 17, "scenery terminology"),
        (new[]{"terrain","dungeon","dungeon tile","battlemap","battle map","wargame","tabletop","hex terrain","scatter terrain"}, "Tabletop Terrain", "Terrain", "Tabletop Terrain", "Terrain", 19, "tabletop terrain terminology"),
        (new[]{"crate","barrel","chair","table","bed","chest","lamp","lantern","sign","fence","bench","bucket","bookshelf","shelf","shadowbox"}, "Props & Accessories", "Prop", "Accessory / Prop", "Prop", 15, "prop/accessory terminology"),
        (new[]{"lithophane","sculpture","vase","ornament","statue","shadowbox"}, "Art & Decor", "Decor", "Art / Decor", "Decor", 18, "art/decor terminology"),
        (new[]{"dice","dnd","chess","puzzle","toy","game","fidget","pokeball"}, "Toys & Games", "Game/Toy", "Game / Toy", "Game", 18, "game/toy terminology"),
        (new[]{"organizer","storage","hook","hanger","drawer","container","soap dispenser","spool"}, "Household", "Functional", "Organization / Storage", "Household", 15, "household terminology")
    };

    // Whole-library 9.0.39 analysis exposed a second large population: titles with clear
    // semantic nouns/proper names that were never represented in the fusion lexicon. Keep
    // these as high-precision title evidence only. They are deliberately grouped by the
    // physical object represented by the filename, not by stored folders/categories.
    private static readonly (string[] Terms, string Category, string Type, string Subtype, string Family, int Weight, string Label)[] ExpandedCues =
    {
        (new[]{"bear","elephant","turtle","tortoise","snake","shark","whale","orca","fish","bee","beehive","butterfly","bird","bluejay","chicken","duck","goose","rabbit","bunny","frog","crab","crocodile","lizard","gecko","chameleon","capybara","otter","seal","snail","octopus","axolotl","squirrel","fox","wolf","lion","tiger","monkey","panda","penguin","dolphin","dinosaur","wyvern","frost wyvern","dragon"}, "Figures & Characters", "Figure", "Creature", "Figure", 17, "creature terminology"),
        (new[]{"batman","superman","wolverine","yoda","stormtrooper","stormtrooper","c3po","c-3po","bowser","pokemon","pikachu","charmander","applejack","bluey","mario","sonic","transformer","optimus prime","bumblebee","ahsoka","darth","jedi","sith","tf2","anime","cartoon"}, "Figures & Characters", "Figure", "Character", "Figure", 18, "character terminology"),
        (new[]{"peterbilt","semi truck","dump truck","fire truck","pickup truck","ambulance","bus","bulldozer","excavator","forklift","tractor","tank","abrams","aeroplane","airplane","aircraft","bomber","b-2","b1 lancer","c130","c-130","ac130","ah64","ah-64","v-22","f-18","f-16","f-4 phantom","f4u corsair","f-86","p-51","p51","pby","747","a380","a400m","concorde","constellation","corsair","sea fury","valkyrie","airbus","helicopter","warship","war ship","aircraft carrier","spaceshuttle","space shuttle","shuttle","starship","x-wing","y-wing","tie fighter","cr90","corvette"}, "Vehicles", "Vehicle", "Vehicle", "Vehicle", 19, "vehicle terminology"),
        (new[]{"lightsaber","light saber","saber","sword","scabbard","blaster","phaser","revolver","pistol","rifle","gun","helmet","armor","cosplay","prop","mask"}, "Props & Accessories", "Prop", "Cosplay / Prop", "Prop", 18, "prop/cosplay terminology"),
        (new[]{"tray","sockettray","socket tray","card holder","business card","phone holder","phone stand","book holder","book stand","key holder","keychain holder","soap holder","soap dispenser","toolbox","tool box","tool organizer","organizer","storage box","storage","drawer","shelf","shelving","rack","hook","hanger","mount","bracket","stand","display stand","display base","phone jail","feeder","caddy","coaster","coasters"}, "Functional", "Functional", "Organizer / Holder", "Functional", 18, "functional role terminology"),
        (new[]{"flag","sign","logo","jersey","home plate","badge","medal","trophy","ornament","rose","watercolor","portrait","sculpture","art","artwork","lithophane","shadowbox"}, "Art & Decor", "Decor", "Art / Display", "Decor", 16, "art/decor terminology"),
        (new[]{"castle","house","building","lighthouse","barn","shed","cabin","church","temple","tavern","booknook","book nook","diorama","zen garden","garden","hive"}, "Buildings", "Building", "Structure / Diorama", "Building", 16, "building/diorama terminology"),
        (new[]{"terrain","dungeon","battlemap","battle map","wargame","tabletop","dnd","cave","caves of chaos","ruin"}, "Tabletop Terrain", "Terrain", "Tabletop Terrain", "Terrain", 18, "tabletop/terrain terminology"),
        (new[]{"dice","chess","puzzle","fidget","toy","game","pokeball","pokemon ball"}, "Toys & Games", "Game/Toy", "Game / Toy", "Game", 18, "game/toy terminology"),
        (new[]{"打印","航空母舰","航空","战机","飞机","轰炸机","舰","军舰","医疗舰","坦克","龙","蛇","熊","狗","猫","鸟","蜥蜴","剑","雕塑","挂钩","收纳","支架","举升机","龙门"}, "Vehicles", "Vehicle", "Vehicle / Aircraft", "Vehicle", 18, "multilingual semantic terminology"),
        (new[]{"mummy","mumia","statue","stone","rock","mountain","mountains","tree","forest","flower","plant","foliage","mushroom"}, "Nature & Scenery", "Scenery", "Natural / Scenic", "Nature", 16, "scenery terminology"),
    };

    private static readonly (string[] Terms, string Category, string Type, string Subtype, string Family, int Weight, string Label)[] ExplicitRoleCues =
    {
        (new[]{"soap holder","soap dispenser holder","key holder","phone holder","book holder","business card holder","card holder","tool holder","socket holder","wrench holder","screwdriver holder","holder"}, "Functional", "Functional", "Holder", "Functional", 24, "explicit holder role"),
        (new[]{"display stand","phone stand","book stand","lightsaber stand","saber stand","sword stand","display base","support stand","stand rack","stand"}, "Functional", "Functional", "Stand / Display", "Functional", 22, "explicit stand role"),
        (new[]{"tool organizer","socket organizer","wrench organizer","screwdriver organizer","tool tray","socket tray","organizer","storage box","rack","shelf","hook","hanger","mount","bracket"}, "Functional", "Functional", "Organizer / Mount", "Functional", 20, "explicit functional role"),
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

    // Generalized artifact-role grammar for the large lexical-conflict population.
    // A model title often contains a subject noun plus a role suffix (for example
    // "A-10 stand", "dragon holder", "car display base", or "Pikachu phone mount").
    // The subject is what the object depicts/references; the role suffix identifies what
    // the printable artifact actually is. Treat these high-precision role constructions
    // as Functional before generic subject-domain lexical cues are allowed to classify.
    // This is intentionally a bounded suffix list, not a free-form "contains X" rule.
    private static readonly string[] ArtifactRoleSuffixes =
    {
        "stand", "holder", "mount", "bracket", "tray", "rack", "organizer",
        "adapter", "dock", "cradle", "spacer", "hook", "hanger", "clip",
        "fixture", "enclosure"
    };

    // These suffixes are intentionally ambiguous in filenames. Words such as "base",
    // "cover", "support", and "case" can be part of a color name, artwork title,
    // assembly label, or model description. They require an explicit functional
    // context before they may promote the whole artifact to Functional.
    private static readonly string[] ContextualArtifactRoleSuffixes =
    {
        "case", "cover", "base", "support"
    };

    private static bool HasContextualArtifactRole(string subject, string role)
    {
        var s = $" {subject} ".ToLowerInvariant();
        return role switch
        {
            "case" => Regex.IsMatch(s, @"\b(phone|tool|storage|protective|display|transport|carrying)\s+case\b", RegexOptions.CultureInvariant),
            "cover" => Regex.IsMatch(s, @"\b(phone|tool|protective|display|printer|machine)\s+cover\b", RegexOptions.CultureInvariant),
            "base" => Regex.IsMatch(s, @"\b(display|mounting|stand|diorama|model|printer|machine)\s+base\b", RegexOptions.CultureInvariant),
            "support" => Regex.IsMatch(s, @"\b(support\s+(bracket|stand|mount|rack)|bracket\s+support|stand\s+support)\b", RegexOptions.CultureInvariant),
            _ => false
        };
    }

    private static bool TryGetFunctionalArtifactRole(string text, out string phrase)
    {
        phrase = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var role in ArtifactRoleSuffixes.Concat(ContextualArtifactRoleSuffixes))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}])(?<subject>[\p{{L}}\p{{N}}][\p{{L}}\p{{N}}\s\-']{{0,59}}?)\s+(?<role>{Regex.Escape(role)})(?![\p{{L}}\p{{N}}])";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;

            var subject = match.Groups["subject"].Value.Trim();
            if (subject.Length == 0) continue;

            // Keep this grammar conservative: require a real subject token and reject
            // phrases that are effectively just the role word repeated by punctuation.
            var subjectTokens = subject.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (subjectTokens.Length == 0 || subjectTokens.Length > 6) continue;
            if (ContextualArtifactRoleSuffixes.Contains(role, StringComparer.OrdinalIgnoreCase) &&
                !HasContextualArtifactRole(subject, role)) continue;

            // HueForge titles frequently contain component words such as "spacer" or
            // "clip" while the printable artifact remains the HueForge frame/tile.
            // Explicit role phrases still win above; generalized component suffixes do not.
            if (text.Contains("hueforge", StringComparison.OrdinalIgnoreCase) &&
                role is "spacer" or "clip") continue;

            phrase = $"{subject} {role}";
            return true;
        }

        return false;
    }

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
        var hasGeneralizedArtifactRole = TryGetFunctionalArtifactRole(text, out var generalizedArtifactRole);

        if (entity is not null)
            evidence.Add($"Named entity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
        if (!string.IsNullOrWhiteSpace(model.TranslationEvidence) && model.TranslationConfidence > 0)
            evidence.Add($"Translation evidence: {model.TranslationConfidence}%");
        if (!string.IsNullOrWhiteSpace(model.IntelligenceReason))
            evidence.Add("Analyzer evidence present");
        if (!string.IsNullOrWhiteSpace(model.SemanticType) || !string.IsNullOrWhiteSpace(model.Subtype))
            evidence.Add($"Stored classification: {model.Category} / {model.SemanticType} / {model.Subtype}");

        var allCues = Cues.Concat(ExpandedCues).ToArray();
        var cueHits = allCues
            .Select(c => (Cue: c, Hits: c.Terms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(x => x.Hits.Length > 0)
            .OrderByDescending(x => x.Cue.Weight * Math.Min(2, x.Hits.Length))
            .ToList();
        var roleHits = ExplicitRoleCues
            .Select(c => (Cue: c, Hits: c.Terms.Where(t => ContainsPhrase(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(x => x.Hits.Length > 0)
            .OrderByDescending(x => x.Cue.Weight * Math.Min(2, x.Hits.Length))
            .ToList();
        var explicitRole = roleHits.FirstOrDefault();

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
        // ThreeMfAnalyzer only emits a source-derived category when it crossed its own
        // semantic threshold (or a deterministic special-format rule). Therefore the
        // presence of category + semantic type + subtype is materially stronger than the
        // overall IntelligenceScore, which also includes geometry/readiness/metadata.
        var strongSourceDerivedAnalyzer = hasSourceDerivedAnalyzer &&
                                          !string.IsNullOrWhiteSpace(model.SemanticType) &&
                                          !string.IsNullOrWhiteSpace(model.Subtype) &&
                                          !string.Equals(analyzerCategory, "Uncategorized", StringComparison.OrdinalIgnoreCase);
        if (hasSourceDerivedAnalyzer)
            evidence.Add($"Source-derived analyzer semantics: {analyzerCategory} / {model.SemanticType} / {model.Subtype} ({model.IntelligenceScore:P0})");
        if (strongSourceDerivedAnalyzer)
            evidence.Add("Analyzer semantic threshold crossed independently of overall structural score");

        string category = model.Category ?? "";
        string type = model.SemanticType ?? "";
        string subtype = model.Subtype ?? "";
        string family = model.Family ?? "";
        // ThreeMfAnalyzer stores IntelligenceScore on a 0..1 scale. The fusion result
        // is a 0..100 public confidence scale. The previous build passed the raw decimal
        // through Clamp(), turning 0.99 into confidence 1 and materially understating
        // analyzer-backed models across the whole library.
        var classification = ClampScore(model.IntelligenceScore);
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
        else if ((explicitRole.Hits?.Length ?? 0) > 0)
        {
            var role = explicitRole.Cue;
            category = role.Category; type = role.Type; subtype = role.Subtype; family = role.Family;
            var roleConfidence = Math.Min(96, 66 + role.Weight + Math.Min(8, ((explicitRole.Hits?.Length ?? 0) - 1) * 4));
            identity = Math.Max(identity, Math.Min(90, roleConfidence - 5));
            classification = Math.Max(classification, roleConfidence);
            basis = $"Object role evidence: {role.Label}";
            evidence.Add($"Explicit object role: {string.Join(", ", explicitRole.Hits ?? Array.Empty<string>())}");
        }
        else if (hasGeneralizedArtifactRole)
        {
            // General role grammar is deliberately below the hand-curated role phrases
            // but above analyzer/lexical subject classification. This prevents broad words
            // such as "dragon", "aircraft", "car", or "Pikachu" from winning when the title
            // explicitly says the printable artifact is a stand, holder, mount, case, etc.
            category = "Functional";
            type = "Functional";
            subtype = "Functional Artifact / Role";
            family = "Functional";
            identity = Math.Max(identity, 78);
            classification = Math.Max(classification, 94);
            basis = "Generalized artifact role evidence";
            evidence.Add($"Role-aware artifact arbitration: {generalizedArtifactRole}");
        }
        else if (strongSourceDerivedAnalyzer)
        {
            // Artifact classification outranks subject identity. A file can depict Pikachu
            // while still being a HueForge tile, or depict an AT-AT while still being a
            // keychain. The source-derived analyzer describes the printable artifact; the
            // named entity describes the depicted subject.
            category = analyzerCategory;
            type = model.SemanticType ?? string.Empty;
            subtype = model.Subtype ?? string.Empty;
            family = string.IsNullOrWhiteSpace(model.Family) ? InferFamily(analyzerCategory) : model.Family;
            var analyzerConfidence = Math.Clamp(82 + (int)Math.Round(model.IntelligenceScore * 13), 82, 95);
            classification = Math.Max(classification, analyzerConfidence);
            identity = Math.Max(identity, Math.Clamp(analyzerConfidence - 4, 78, 91));
            if (entity is not null)
            {
                evidence.Add($"Subject identity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
                basis = "Source-derived artifact semantics + named subject identity";
            }
            else if (cueHits.Count > 0 && best.Hits.Length > 0)
            {
                var lexicalCategory = best.Cue.Category;
                if (string.Equals(lexicalCategory, analyzerCategory, StringComparison.OrdinalIgnoreCase))
                {
                    basis = "Source-derived analyzer + lexical corroboration";
                    evidence.Add($"Lexical corroboration: {string.Join(", ", best.Hits)}");
                }
                else
                {
                    basis = "Source-derived analyzer arbitration";
                    evidence.Add($"Lexical cue retained as non-promoting evidence: {string.Join(", ", best.Hits)}");
                    evidence.Add($"Analyzer arbitration: {analyzerCategory} outranks lexical category {lexicalCategory}");
                }
            }
            else
            {
                basis = "Source-derived analyzer semantics";
            }
        }
        else if (entity is not null && IsContextualEntityWithArtifactCue(entity, best))
        {
            // A setting/landmark entity can identify the world or location referenced by
            // the model without identifying the printable artifact itself. For example,
            // "Hogwarts dragon" contains a real named entity (Hogwarts -> Buildings), but
            // the printed object is the dragon figure. In this case the high-precision
            // artifact cue is the correct catalog category and the named entity is retained
            // as contextual evidence rather than allowed to promote its domain category.
            var cue = best.Cue;
            category = cue.Category;
            type = cue.Type;
            subtype = cue.Subtype;
            family = cue.Family;
            var cueConfidence = Math.Min(92, 58 + cue.Weight + Math.Min(10, (best.Hits.Length - 1) * 5));
            classification = Math.Max(classification, cueConfidence);
            identity = Math.Max(identity, Math.Min(88, cueConfidence));
            basis = "Artifact lexical evidence + contextual named entity";
            evidence.Add($"Contextual named entity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
            evidence.Add($"Artifact cue outranks contextual entity category: {string.Join(", ", best.Hits)} -> {cue.Category}");
        }
        else if (entity is not null)
        {
            // A recognized named entity is an independent identity/classification channel.
            // When no source-derived artifact analyzer result exists, the entity category
            // must remain authoritative rather than being replaced by the empty
            // analyzerCategory out-parameter. This preserves entity classifications such
            // as Pikachu -> Figures & Characters and DeLorean -> Vehicles.
            category = entity.Category;
            type = string.IsNullOrWhiteSpace(model.SemanticType)
                ? InferType(entity.Category, entity.Subtype)
                : model.SemanticType;
            subtype = string.IsNullOrWhiteSpace(model.Subtype) ? entity.Subtype : model.Subtype;
            family = string.IsNullOrWhiteSpace(model.Family) ? InferFamily(entity.Category) : model.Family;
            classification = Math.Max(classification, entity.Confidence);
            identity = Math.Max(identity, entity.Confidence);
            basis = "Named entity evidence";
            evidence.Add($"Subject identity: {entity.EntityName} ({entity.Domain}) at {entity.Confidence}%");
            if (cueHits.Count > 0 && best.Hits.Length > 0)
            {
                var lexicalCategory = best.Cue.Category;
                if (string.Equals(lexicalCategory, entity.Category, StringComparison.OrdinalIgnoreCase))
                {
                    basis = "Named entity + lexical corroboration";
                    evidence.Add($"Lexical corroboration: {string.Join(", ", best.Hits)}");
                }
                else
                {
                    evidence.Add($"Lexical cue retained as non-promoting evidence: {string.Join(", ", best.Hits)}");
                    evidence.Add($"Entity arbitration: {entity.Category} outranks lexical category {lexicalCategory}");
                }
            }
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
            // when no stronger entity, role, or lexical inference is available. Never use
            // Tags, SuggestedTags, or this fusion result as evidence.
            category = analyzerCategory;
            type = string.IsNullOrWhiteSpace(model.SemanticType) ? type : model.SemanticType;
            subtype = string.IsNullOrWhiteSpace(model.Subtype) ? subtype : model.Subtype;
            family = string.IsNullOrWhiteSpace(model.Family) ? InferFamily(analyzerCategory) : model.Family;
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
        if (aviationConvergence && entity is null && !strongSourceDerivedAnalyzer)
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
        {
            // The analyzer's semantic threshold is a category-bearing source signal. Do not
            // incorrectly demote a semantically valid model merely because geometry/readiness
            // lowered the aggregate IntelligenceScore. Keep it bounded and still require
            // either a second channel or the explicit analyzer threshold for review clearance.
            if (strongSourceDerivedAnalyzer)
            {
                quality = Math.Max(quality, 78);
                classification = Math.Max(classification, 88);
                identity = Math.Max(identity, 82);
            }
            else
            {
                quality = Math.Max(quality, Math.Clamp(55 + (int)Math.Round(model.IntelligenceScore * 35), 55, 90));
            }
            if (hasTranslation)
                quality = Math.Max(quality, Math.Clamp(63 + (int)Math.Round(model.IntelligenceScore * 25), 63, 88));
        }
        if (entity is not null)
            quality = Math.Max(quality, Math.Min(100, entity.Confidence));

        // General convergence gate: a model/designation that carries a known aviation
        // identity-bearing term AND an independent aviation-domain term is strong semantic
        // evidence even when the named-entity dictionary does not recognize the exact model.
        // This is intentionally domain-general within the aviation evidence family; it is
        // not a special case for A-10. Context-only signals still contribute nothing here.
        if (aviationConvergence && !strongSourceDerivedAnalyzer)
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

        // Separate actionable contradiction from absence of evidence.
        //
        // The previous policy turned every low-confidence record into a review item, even
        // when the inferred category was identical to the stored category. That inflated
        // the whole-library "insufficient evidence" population without producing an action
        // a reviewer could take.
        //
        // Review now means there is an actionable semantic disagreement. Lack of evidence
        // is represented explicitly as unresolved, not as a false conflict. Stored
        // classification never contributes confidence or corroboration.
        var storedCategory = model.Category?.Trim() ?? "";
        var hasStoredCategory = !string.IsNullOrWhiteSpace(storedCategory);
        var hasInferredCategory = !string.IsNullOrWhiteSpace(category);

        // "Uncategorized" is an absence of a prior classification, not a competing
        // classification. Treating it as a conflict inflated the whole-library conflict
        // population whenever the classifier successfully found a category (for example
        // Bear -> Figures & Characters or Peterbilt -> Vehicles). It is therefore eligible
        // for a resolution candidate when evidence is strong, but it must not be reported
        // as a semantic contradiction.
        var storedCategoryIsUnresolved = !hasStoredCategory ||
                                         string.Equals(storedCategory, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(storedCategory, "Unknown", StringComparison.OrdinalIgnoreCase);
        var categoriesDiffer = hasStoredCategory && hasInferredCategory &&
                               !string.Equals(storedCategory, category, StringComparison.OrdinalIgnoreCase);
        var actionableConflict = categoriesDiffer &&
                                 !storedCategoryIsUnresolved &&
                                 !string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase);
        // Retain the bounded convergence diagnostics used by the historical regression
        // suite, but do not let them independently convert an unresolved record into a
        // review item. Review state is now driven by actionable category disagreement.
        var analyzerTranslationConvergenceClearsReview = strongSourceDerivedAnalyzer && hasTranslation &&
                                                         classification >= 82 && quality >= 75;
        var analyzerConvergenceClearsReview = strongSourceDerivedAnalyzer &&
                                              classification >= 85 && quality >= 75;
        var convergenceClearsReview = aviationConvergence;
        var semanticChannelsAgree = false;
        if (entity is not null && !string.IsNullOrWhiteSpace(category))
        {
            // Subject identity and artifact category are complementary dimensions. An
            // entity/category mismatch is not actionable when a stronger artifact channel
            // (role or source-derived analyzer) established the printable object's category.
            semanticChannelsAgree = strongSourceDerivedAnalyzer || string.Equals(entity.Category, category, StringComparison.OrdinalIgnoreCase);
        }
        var strongAlternative = strongSourceDerivedAnalyzer ||
                                aviationConvergence ||
                                (cueHits.Count > 0 && best.Hits.Length > 0 && classification >= 85);
        var review = actionableConflict ||
                     (strongAlternative && categoriesDiffer && !storedCategoryIsUnresolved && !semanticChannelsAgree);
        if (review)
        {
            evidence.Add("Review recommended: independent evidence conflicts with stored classification");
        }
        else if (!hasInferredCategory || string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add("Unresolved: no sufficiently specific semantic classification was established");
        }
        else if (hasStoredCategory && string.Equals(storedCategory, category, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add("Classification agrees with stored category; no actionable conflict");
        }
        else
        {
            evidence.Add("Classification established without a stored-category conflict");
        }

        return new SemanticEvidenceFusionResult(category, type, subtype, family,
            Math.Clamp(identity, 0, 100), Math.Clamp(classification, 0, 100), quality, basis, review,
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool IsContextualEntityWithArtifactCue(
        MultilingualEntityMatch entity,
        ( (string[] Terms, string Category, string Type, string Subtype, string Family, int Weight, string Label) Cue,
          string[] Hits) best)
    {
        if (best.Hits.Length == 0) return false;
        if (!string.Equals(best.Cue.Category, "Figures & Characters", StringComparison.OrdinalIgnoreCase)) return false;

        // Building/landmark entities such as Hogwarts or the Eiffel Tower can be referenced
        // by a model whose actual printable subject is a figure. Only treat the entity as
        // contextual when its own subtype identifies a setting/structure/landmark; a normal
        // building model still wins through the building lexical cue.
        if (!string.Equals(entity.Category, "Buildings", StringComparison.OrdinalIgnoreCase)) return false;

        // Entity records may establish an entity/domain without a subtype.
        // Missing subtype is not exceptional; it simply provides no contextual cue.
        var subtype = entity.Subtype ?? string.Empty;
        return subtype.Contains("Building", StringComparison.OrdinalIgnoreCase) ||
               subtype.Contains("Landmark", StringComparison.OrdinalIgnoreCase) ||
               subtype.Contains("Castle", StringComparison.OrdinalIgnoreCase);
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

    private static int ClampScore(double value)
    {
        // IntelligenceScore is normalized 0..1 in ThreeMfAnalyzer. Accept an already
        // percentage-scaled value defensively so future analyzer contracts remain stable.
        var percent = value <= 1.0 ? value * 100.0 : value;
        return (int)Math.Clamp(Math.Round(percent), 0, 100);
    }
}
