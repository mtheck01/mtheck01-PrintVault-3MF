using System.Text.Json;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record SmartCategorySuggestion(
    string Path,
    string Name,
    string CurrentCategory,
    string SuggestedCategory,
    int ConfidencePercent,
    string Evidence,
    string Reason,
    bool Protected,
    string Decision);

public sealed record SmartCategoryReviewPlan(
    IReadOnlyList<SmartCategorySuggestion> Suggestions,
    int Scanned,
    int Candidates,
    int Protected,
    int HighConfidence);

public sealed record SmartCategoryReviewResult(int Applied, int ProtectedSkipped, IReadOnlyList<string> Warnings);

public sealed record SmartCategoryUndoEntry(string Path, string PreviousCategory, string PreviousTags, bool PreviousCategoryOverride);
public sealed record SmartCategoryUndoManifest(DateTime CreatedUtc, IReadOnlyList<SmartCategoryUndoEntry> Entries);

/// <summary>
/// Metadata-only category review. It never moves files or changes their folders.
/// It uses existing semantic analysis fields plus deterministic filename evidence to
/// identify likely misclassified records, with manual overrides treated as protected.
/// </summary>
public sealed class SmartCategoryReconciliationService
{
    private static readonly string[] ReviewRoots = { "Terrain & Props", "Terrain", "Props", "Prop" };
    private static readonly string[] CanonicalReviewCategories =
    {
        "Buildings", "Nature & Scenery", "Tabletop Terrain", "Props & Accessories"
    };

    private readonly LibraryRepository repo;
    private readonly string undoPath;

    public SmartCategoryReconciliationService(LibraryRepository repository, string? stateDirectory = null)
    {
        repo = repository ?? throw new ArgumentNullException(nameof(repository));
        var app = string.IsNullOrWhiteSpace(stateDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault")
            : Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(app);
        undoPath = Path.Combine(app, "last_smart_category_review.json");
    }

    public SmartCategoryReviewPlan Preview()
    {
        var all = repo.GetAll();
        var candidates = new List<SmartCategorySuggestion>();
        var protectedCount = 0;

        foreach (var model in all)
        {
            if (!ReviewRoots.Contains(model.Category, StringComparer.OrdinalIgnoreCase)) continue;

            // A manual category is visible to the review as a protected record, but it is
            // never offered as an actionable correction. This makes the protection state
            // auditable instead of silently dropping the record from the preview.
            if (model.CategoryOverride)
            {
                protectedCount++;
                candidates.Add(new SmartCategorySuggestion(
                    model.Path, model.Name, model.Category, "", 0,
                    "Manual category override — automatic reassignment blocked",
                    model.IntelligenceReason, true, "PROTECTED"));
                continue;
            }

            var suggestion = Suggest(model);
            if (suggestion is null) continue;
            candidates.Add(suggestion);
        }

        return new SmartCategoryReviewPlan(
            candidates.OrderByDescending(x => x.ConfidencePercent).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            all.Count,
            candidates.Count,
            protectedCount,
            candidates.Count(x => x.ConfidencePercent >= 95 && !x.Protected));
    }

    public SmartCategoryReviewResult Apply(IReadOnlyCollection<SmartCategorySuggestion> selected, Action<string>? ensureCategory = null, Action<string, string>? learn = null)
    {
        var warnings = new List<string>();
        var undo = new List<SmartCategoryUndoEntry>();
        var applied = 0;
        var protectedSkipped = 0;

        foreach (var item in selected)
        {
            var model = repo.Get(item.Path);
            if (model is null) { warnings.Add($"Model no longer exists: {item.Path}"); continue; }
            if (model.CategoryOverride) { protectedSkipped++; continue; }
            if (!string.Equals(model.Category, item.CurrentCategory, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipped changed model: {item.Name}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.SuggestedCategory)) continue;

            undo.Add(new SmartCategoryUndoEntry(model.Path, model.Category, model.Tags, model.CategoryOverride));
            var previous = model.Category;
            model.Category = item.SuggestedCategory;
            model.Tags = ReplaceCategoryTag(model.Tags, previous, item.SuggestedCategory);
            model.CategoryOverride = true;
            repo.Upsert(model);
            ensureCategory?.Invoke(item.SuggestedCategory);
            learn?.Invoke(model.Name, item.SuggestedCategory);
            applied++;
        }

        if (undo.Count > 0) SaveManifest(new SmartCategoryUndoManifest(DateTime.UtcNow, undo));
        return new SmartCategoryReviewResult(applied, protectedSkipped, warnings);
    }

    public bool CanUndo => File.Exists(undoPath);

    public SmartCategoryReviewResult UndoLast()
    {
        var warnings = new List<string>();
        SmartCategoryUndoManifest? manifest;
        try { manifest = File.Exists(undoPath) ? JsonSerializer.Deserialize<SmartCategoryUndoManifest>(File.ReadAllText(undoPath)) : null; }
        catch (Exception ex) { return new(0, 0, new[] { $"Undo record could not be read: {ex.Message}" }); }
        if (manifest is null || manifest.Entries.Count == 0) return new(0, 0, new[] { "No smart category review is available to undo." });

        var changed = 0;
        foreach (var entry in manifest.Entries.Reverse())
        {
            var model = repo.Get(entry.Path);
            if (model is null) { warnings.Add($"Model no longer exists: {entry.Path}"); continue; }
            model.Category = entry.PreviousCategory;
            model.Tags = entry.PreviousTags;
            model.CategoryOverride = entry.PreviousCategoryOverride;
            repo.Upsert(model);
            changed++;
        }
        if (warnings.Count == 0) TryDeleteManifest(warnings);
        return new(changed, 0, warnings);
    }

    public static IReadOnlyList<string> CanonicalCategories => CanonicalReviewCategories;

    private static SmartCategorySuggestion? Suggest(ModelRecord model)
    {
        // CategoryOverride is a hard protection boundary. The preview layer also marks
        // protected suggestions, but the classifier itself should never propose a new
        // category for a deliberate manual decision.
        if (model.CategoryOverride) return null;

        var name = model.Name ?? "";
        var semantic = model.SemanticType ?? "";
        var subtype = model.Subtype ?? "";
        var tags = model.SuggestedTags ?? "";
        var reasonText = model.IntelligenceReason ?? "";
        var translatedTitle = model.TranslatedTitle ?? "";
        var entity = new MultilingualEntityService().Recognize(model);
        // Use every stable text surface available to the classifier. Some library records
        // have a normalized/empty display name while the actual filename remains highly
        // informative (for example "Rapido+y+Furioso"). Path/filename evidence is metadata
        // only; it never changes the physical file.
        var fileName = Path.GetFileNameWithoutExtension(model.Path ?? "");
        var text = string.Join(" ", name, fileName, model.Path ?? "", translatedTitle, semantic, subtype, tags, reasonText).ToLowerInvariant();

        var scores = new Dictionary<string, (double Score, List<string> Evidence, int StrongSignals)>(StringComparer.OrdinalIgnoreCase);
        void Add(string category, double score, string evidence, bool strong = false)
        {
            if (!scores.TryGetValue(category, out var v)) v = (0, new List<string>(), 0);
            v.Score += score;
            if (!v.Evidence.Contains(evidence, StringComparer.OrdinalIgnoreCase)) v.Evidence.Add(evidence);
            if (strong) v.StrongSignals++;
            scores[category] = v;
        }

        void ExactSemantic(string semanticType, string category, double score, string evidence)
        {
            if (string.Equals(semantic, semanticType, StringComparison.OrdinalIgnoreCase))
                Add(category, score, evidence, strong: true);
        }

        // Named entities are the strongest lexical evidence. This deliberately outranks
        // misleading geometry metadata such as Scenery/Tree/Prop. Translation confidence
        // is separate from entity confidence: an exact known phrase can be recognized even
        // when the offline literal translation is only moderately confident.
        if (entity is not null)
            Add(entity.Category, 70, $"{entity.Evidence}; entity confidence: {entity.Confidence}%", strong: true);

        // Strong semantic classifications outrank generic Family=Prop / scenery evidence.
        ExactSemantic("Vehicle", "Vehicles", 28, "Analyzer semantic type: Vehicle");
        ExactSemantic("Character", "Figures & Characters", 26, "Analyzer semantic type: Character");
        ExactSemantic("Figure", "Figures & Characters", 26, "Analyzer semantic type: Figure");
        ExactSemantic("Tool", "Tools & Workshop", 24, "Analyzer semantic type: Tool");
        ExactSemantic("Functional", "Functional", 24, "Analyzer semantic type: Functional");
        ExactSemantic("Building", "Buildings", 22, "Analyzer semantic type: Building");
        ExactSemantic("Terrain", "Tabletop Terrain", 22, "Analyzer semantic type: Terrain");

        // Vehicle/aircraft/watercraft names are strong lexical evidence. These must beat
        // weak generic signals such as Scenery, Tree, or Family=Prop.
        var vehicleTerms = new[]
        {
            "batmobile", "batwing", "batboat", "batpod", "delorean", "de lorean",
            "x-wing", "xwing", "tie fighter", "tie-fighter", "millennium falcon",
            "star destroyer", "speeder", "at-at", "at-at walker", "spaceship", "space ship",
            "spacecraft", "starship", "shuttle", "rocket", "rover", "buggy", "tractor",
            "excavator", "bulldozer", "forklift", "ambulance", "police car", "fire truck",
            "race car", "formula 1", "formula one", "f1 car", "pickup truck", "pickup",
            "semi truck", "semi-truck", "suv", "sedan", "coupe", "convertible", "van", "bus",
            "truck", "car", "motorcycle", "motorbike", "bicycle", "scooter", "aircraft",
            "airplane", "helicopter", "jet", "glider", "drone", "plane", "tank", "ship",
            "boat", "yacht", "submarine", "canoe", "kayak", "ferry", "train", "locomotive",
            "tram", "trolley", "vehicle", "crane",
            // Chinese vehicle/entity vocabulary. These are kept as explicit aliases so
            // multilingual filenames can participate in the same evidence model.
            "吊车", "起重机", "叉车", "挖掘机", "推土机", "卡车", "货车", "汽车", "轿车",
            "摩托车", "自行车", "电动车", "火车", "机车", "飞机", "直升机", "无人机", "战斗机",
            "船", "轮船", "快艇", "游艇", "潜水艇", "坦克", "火箭", "航天飞机",
            "rapido y furioso", "fast and furious"
        };
        var vehicleHits = vehicleTerms.Where(t => ContainsToken(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Context beats isolated vehicle nouns. A "plane stand" is an accessory, and a
        // "rocket launch pad" is scenery/infrastructure, not a vehicle. This prevents
        // keyword matching from promoting holders, mounts, display stands, and launch
        // infrastructure into Vehicles. Explicit multi-word entity phrases such as
        // "Rapido y Furioso" remain positive vehicle evidence even when analyzer metadata
        // happens to describe the object as a Building/Prop.
        var explicitVehiclePhrases = new[] {
            "batmobile", "batwing", "batboat", "delorean", "de lorean",
            "x-wing", "xwing", "tie fighter", "millennium falcon", "star destroyer",
            "at-at walker", "rapido y furioso", "fast and furious"
        };
        var explicitVehicleHits = explicitVehiclePhrases.Where(t => ContainsToken(text, t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var vehicleAccessoryContexts = new[]
        {
            "plane stand", "plane vertical stand", "airplane stand", "aircraft stand",
            "plane holder", "airplane holder", "vehicle stand", "vehicle holder",
            "vehicle mount", "vehicle display", "vehicle display stand", "car holder",
            "car mount", "car display", "car display stand", "motorcycle stand",
            "motorbike stand", "bike stand", "truck stand", "truck mount", "shuttle stand",
            "rocket launch pad", "rocket launch platform", "rocket launch base"
        };
        var contextExclusions = vehicleAccessoryContexts.Where(x => ContainsToken(text, x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (contextExclusions.Count > 0)
        {
            var launchContext = contextExclusions.Any(x => x.Contains("launch pad", StringComparison.OrdinalIgnoreCase)
                || x.Contains("launch platform", StringComparison.OrdinalIgnoreCase)
                || x.Contains("launch base", StringComparison.OrdinalIgnoreCase));
            if (launchContext)
                Add("Tabletop Terrain", 24, $"Vehicle noun suppressed by launch infrastructure context: {string.Join(", ", contextExclusions.Take(2))}", strong: true);
            else
                Add("Functional", 26, $"Vehicle noun suppressed by accessory context: {string.Join(", ", contextExclusions.Take(2))}", strong: true);
        }
        else if (vehicleHits.Count > 0)
        {
            var phraseBonus = explicitVehicleHits.Count > 0 ? 10 : 0;
            var evidenceLabel = explicitVehicleHits.Count > 0
                ? $"Explicit vehicle entity phrase: {string.Join(", ", explicitVehicleHits.Take(3))}"
                : $"Strong vehicle terminology: {string.Join(", ", vehicleHits.Take(4))}";
            Add("Vehicles", 32 + phraseBonus + Math.Min(16, (vehicleHits.Count - 1) * 5), evidenceLabel, strong: true);
        }

        // Strong figure/character and functional terms are also allowed to override generic
        // prop/scenery evidence when they are explicit in the filename/analysis text.
        var characterTerms = new[] { "action figure", "figurine", "miniature", "minifig", "character", "superhero", "villain", "soldier", "robot", "dragon", "dinosaur", "bust" };
        var characterHits = characterTerms.Where(t => ContainsToken(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (characterHits.Count > 0)
            Add("Figures & Characters", 24 + Math.Min(10, (characterHits.Count - 1) * 4), $"Strong character terminology: {string.Join(", ", characterHits.Take(4))}", strong: true);

        var functionalTerms = new[] { "bracket", "adapter", "mount", "hinge", "enclosure", "replacement", "connector", "spacer", "gear", "bearing", "fixture" };
        var functionalHits = functionalTerms.Where(t => ContainsToken(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (functionalHits.Count > 0)
            Add("Functional", 22 + Math.Min(10, (functionalHits.Count - 1) * 4), $"Strong functional terminology: {string.Join(", ", functionalHits.Take(4))}", strong: true);

        // Secondary evidence. Generic Prop/Scenery metadata is intentionally weak and can
        // never manufacture a high-confidence category on its own.
        if (string.Equals(semantic, "Scenery", StringComparison.OrdinalIgnoreCase)) Add("Nature & Scenery", 5, "Analyzer semantic type: Scenery");
        if (string.Equals(semantic, "Prop", StringComparison.OrdinalIgnoreCase)) Add("Props & Accessories", 4, "Analyzer semantic type: Prop");

        if (ContainsToken(subtype, "House") || ContainsToken(subtype, "Castle") || ContainsToken(subtype, "Ruin")) Add("Buildings", 7, $"Subtype: {subtype}");
        if (ContainsToken(subtype, "Tree") || ContainsToken(subtype, "Rock") || ContainsToken(subtype, "Diorama")) Add("Nature & Scenery", 5, $"Subtype: {subtype}");
        if (ContainsToken(subtype, "Aircraft") || ContainsToken(subtype, "Vehicle")) Add("Vehicles", 10, $"Vehicle subtype: {subtype}", strong: true);

        void Terms(string category, double weight, string evidence, IReadOnlyCollection<string> terms)
        {
            var hits = terms.Where(t => ContainsToken(text, t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hits.Count == 0) return;
            Add(category, weight * Math.Min(3, hits.Count), $"{evidence}: {string.Join(", ", hits.Take(4))}");
        }
        Terms("Buildings", 3.0, "Building terms", new[] {
            "house", "building", "castle", "tavern", "church", "temple", "tower", "fort", "ruin", "cabin", "barn", "shed",
            "房子", "房屋", "建筑", "建筑物", "城堡", "塔楼", "教堂", "寺庙", "堡垒", "废墟", "小屋", "谷仓"
        });
        Terms("Nature & Scenery", 2.5, "Scenery terms", new[] {
            "tree", "forest", "rock", "rocks", "boulder", "mountain", "cliff", "hill", "grass", "bush", "plant", "foliage", "mushroom", "waterfall",
            "树", "树林", "森林", "岩石", "石头", "巨石", "山", "悬崖", "山丘", "草", "灌木", "植物", "蘑菇", "瀑布"
        });
        Terms("Tabletop Terrain", 3.0, "Tabletop terrain terms", new[] {
            "terrain", "dungeon", "tile", "tiles", "battlemap", "battle map", "floor", "grid", "scatter", "wargame", "tabletop", "hex",
            "地形", "地下城", "地牢", "战棋", "桌游", "桌面游戏", "地砖", "地板", "网格", "散件", "六边形"
        });
        Terms("Props & Accessories", 2.2, "Prop terms", new[] {
            "prop", "crate", "barrel", "chair", "table", "bed", "chest", "lamp", "lantern", "sign", "fence", "bench", "bucket", "bookshelf", "shelf",
            "道具", "箱子", "木箱", "桶", "椅子", "桌子", "床", "宝箱", "灯", "灯笼", "标牌", "围栏", "长椅", "水桶", "书架", "架子"
        });

        if (scores.Count == 0) return null;
        var ranked = scores.OrderByDescending(x => x.Value.Score).ToList();
        var top = ranked[0];
        var second = ranked.Count > 1 ? ranked[1].Value.Score : 0;
        var margin = Math.Max(0, top.Value.Score - second);

        // Do not create a suggestion from weak or conflicting evidence. A high confidence
        // number is reserved for a strong semantic/lexical lead with a meaningful margin.
        if (top.Value.StrongSignals == 0 && top.Value.Score < 10) return null;
        if (top.Value.StrongSignals == 0 && margin < 3.5) return null;
        if (top.Value.StrongSignals > 0 && margin < 5.0) return null;

        var confidence = 55 + (top.Value.StrongSignals > 0 ? 18 : 0)
            + Math.Min(18, top.Value.Score * 0.45)
            + Math.Min(8, margin * 0.35);

        // Confidence is evidence-calibrated, not merely score-calibrated. One strong
        // signal can produce a solid suggestion, but it cannot claim near-certainty.
        // Reserve the upper band for two or more independent strong signals. Weak
        // generic evidence remains review-only.
        // Explicit named-entity phrases (for example Batmobile or DeLorean) are
        // materially stronger than a generic noun such as "rocket" or "shuttle".
        // Treat that exact-entity evidence as a second independent signal so the
        // review UI can safely place those deterministic cases in AUTO.
        var effectiveStrongSignals = top.Value.StrongSignals
            + (top.Key.Equals("Vehicles", StringComparison.OrdinalIgnoreCase) && explicitVehicleHits.Count > 0 ? 1 : 0)
            + (entity is not null && entity.Category.Equals(top.Key, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        var confidenceCap = effectiveStrongSignals >= 2 ? 97
            : effectiveStrongSignals == 1 ? 94
            : 82;
        confidence = Math.Clamp(Math.Min(confidence, confidenceCap), 60, 97);
        if (top.Value.StrongSignals == 0) confidence = Math.Min(confidence, 82);
        if (confidence < 70) return null;
        if (string.Equals(model.Category, top.Key, StringComparison.OrdinalIgnoreCase)) return null;

        var reason = string.Join("; ", top.Value.Evidence.Take(4));
        var finalConfidence = (int)Math.Round(confidence);
        var decision = finalConfidence >= 95 ? "AUTO" : "REVIEW";
        return new SmartCategorySuggestion(model.Path ?? string.Empty, model.Name ?? Path.GetFileName(model.Path ?? string.Empty), model.Category, top.Key,
            finalConfidence, reason, model.IntelligenceReason ?? string.Empty, false, decision);
    }

    private static bool ContainsToken(string text, string term)
    {
        // Latin-script terms use whole-token matching so "car" cannot match "card".
        // CJK terms do not use whitespace tokenization, so a Chinese/Japanese/Korean
        // alias is matched as a contiguous Unicode phrase instead. This preserves the
        // false-positive protection while allowing multilingual library filenames such
        // as "11200吊车64比例" to participate in classification.
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return false;
        if (term.All(IsCjkLike))
            return text.Contains(term, StringComparison.OrdinalIgnoreCase);

        var normalized = new string(text.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var needle = term.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (needle.Length == 0 || words.Length < needle.Length) return false;
        for (var i = 0; i <= words.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (!string.Equals(words[i + j], needle[j], StringComparison.OrdinalIgnoreCase)) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    private static bool IsCjkLike(char ch)
        => (ch >= '\u3400' && ch <= '\u4DBF') || (ch >= '\u4E00' && ch <= '\u9FFF')
            || (ch >= '\u3040' && ch <= '\u30FF') || (ch >= '\uAC00' && ch <= '\uD7AF');

    private static string ReplaceCategoryTag(string tags, string oldCategory, string newCategory)
    {
        var values = (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, oldCategory, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x, newCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        values.Insert(0, newCategory);
        return string.Join(", ", values.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void SaveManifest(SmartCategoryUndoManifest manifest)
    {
        var temp = undoPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, undoPath, true);
    }

    private static void TryDeleteManifest(List<string> warnings)
    {
        try { var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault"); var p = Path.Combine(app, "last_smart_category_review.json"); if (File.Exists(p)) File.Delete(p); }
        catch (Exception ex) { warnings.Add($"Could not clear smart category undo record: {ex.Message}"); }
    }
}
