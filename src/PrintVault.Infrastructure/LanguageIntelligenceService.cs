using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Deterministic, offline language intelligence for catalog metadata.
///
/// Responsibilities:
/// 1. Unicode/casing/punctuation normalization.
/// 2. Script-aware language detection.
/// 3. Conservative glossary translation for high-value catalog terms.
/// 4. Alias/canonical phrase normalization without changing the source filename.
/// 5. Confidence/evidence reporting for downstream semantic intelligence.
///
/// This layer deliberately does not classify categories and does not call a network
/// service. It produces normalized language evidence for the later intelligence stages.
/// </summary>
public sealed class LanguageIntelligenceService
{
    public sealed record LanguageResult(
        string Language,
        string NormalizedText,
        string TranslatedText,
        int Confidence,
        string Evidence,
        IReadOnlyList<string> Terms,
        bool MixedLanguage);

    private static readonly Dictionary<string, string[]> ScriptHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chinese"] = new[] { "的", "和", "打印", "模型", "比例", "整体", "上传", "收纳", "工具", "装饰", "汽车", "飞机", "火箭", "吊车", "叉车", "霍格沃茨", "蝙蝠车", "千年隼" },
        ["Japanese"] = new[] { "の", "と", "モデル", "車", "飛行機", "収納", "工具", "建物", "ドラゴン", "フィギュア" },
        ["Korean"] = new[] { "의", "및", "모델", "자동차", "비행기", "수납", "도구", "건물", "드래곤", "피규어" }
    };

    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["吊车"]="crane", ["起重机"]="crane", ["移动吊车"]="mobile crane",
        ["叉车"]="forklift", ["挖掘机"]="excavator", ["推土机"]="bulldozer",
        ["卡车"]="truck", ["货车"]="truck", ["汽车"]="car", ["轿车"]="car",
        ["摩托车"]="motorcycle", ["飞机"]="airplane", ["战斗机"]="fighter jet",
        ["直升机"]="helicopter", ["无人机"]="drone", ["火箭"]="rocket",
        ["坦克"]="tank", ["船"]="ship", ["潜艇"]="submarine",
        ["房子"]="house", ["房屋"]="building", ["建筑"]="building", ["城堡"]="castle",
        ["塔"]="tower", ["桥"]="bridge", ["树"]="tree", ["岩石"]="rock", ["山"]="mountain",
        ["龙"]="dragon", ["恐龙"]="dinosaur", ["人物"]="character", ["角色"]="character",
        ["模型"]="model", ["文件"]="file", ["打印件"]="printed model", ["上传文件"]="uploaded file",
        ["整体"]="whole", ["比例"]="scale", ["套件"]="kit", ["支架"]="stand", ["底座"]="base",
        ["展示"]="display", ["收纳"]="storage", ["盒"]="box", ["架"]="rack", ["灯"]="lamp",
        ["钥匙扣"]="keychain", ["工具"]="tool", ["玩具"]="toy", ["装饰"]="decor",
        ["桌面收纳"]="desk organizer", ["理线器"]="cable organizer",
        ["蝙蝠车"]="batmobile", ["千年隼"]="millennium falcon", ["霍格沃茨"]="hogwarts",
        ["蝙蝠俠"]="batman", ["蜘蛛人"]="spider-man", ["鋼鐵人"]="iron man",
        ["打印模型"]="3d printed model", ["3D打印"]="3d printing", ["模型打印"]="model printing"
    };

    private static readonly Dictionary<string, string> Japanese = new(StringComparer.Ordinal)
    {
        ["車"]="car", ["自動車"]="automobile", ["飛行機"]="airplane", ["戦闘機"]="fighter jet",
        ["ヘリコプター"]="helicopter", ["ロケット"]="rocket", ["船"]="ship", ["城"]="castle",
        ["家"]="house", ["建物"]="building", ["塔"]="tower", ["龍"]="dragon", ["ドラゴン"]="dragon",
        ["恐竜"]="dinosaur", ["工具"]="tool", ["収納"]="storage", ["人形"]="figure",
        ["フィギュア"]="figure", ["オーガナイザー"]="organizer"
    };

    private static readonly Dictionary<string, string> Korean = new(StringComparer.Ordinal)
    {
        ["자동차"]="car", ["차"]="car", ["비행기"]="airplane", ["전투기"]="fighter jet",
        ["헬리콥터"]="helicopter", ["로켓"]="rocket", ["배"]="ship", ["성"]="castle",
        ["집"]="house", ["건물"]="building", ["탑"]="tower", ["용"]="dragon", ["공룡"]="dinosaur",
        ["도구"]="tool", ["수납"]="storage", ["인형"]="figure", ["피규어"]="figure",
        ["정리함"]="organizer"
    };

    public LanguageResult Analyze(string? text)
    {
        var source = text ?? string.Empty;
        var normalized = Normalize(source);
        if (normalized.Length == 0)
            return new("Unknown", string.Empty, string.Empty, 0, "LANGUAGE:EMPTY", Array.Empty<string>(), false);

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ScriptHints)
        {
            var score = pair.Value.Count(term => source.Contains(term, StringComparison.Ordinal));
            if (score > 0) scores[pair.Key] = score;
        }

        // Script alone cannot distinguish Japanese Kanji from Chinese Han characters.
        // Score language-specific glossary evidence first, then use script as a secondary signal.
        var glossaryScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chinese"] = Chinese.Keys.Count(term => source.Contains(term, StringComparison.Ordinal)),
            ["Japanese"] = Japanese.Keys.Count(term => source.Contains(term, StringComparison.Ordinal)),
            ["Korean"] = Korean.Keys.Count(term => source.Contains(term, StringComparison.Ordinal))
        };
        foreach (var pair in glossaryScores)
        {
            if (pair.Value > 0)
                scores[pair.Key] = scores.GetValueOrDefault(pair.Key) + pair.Value * 4;
        }

        var script = DetectScript(source);
        if (script is not null)
            scores[script] = scores.GetValueOrDefault(script) + 2;

        // If substantive glossary evidence exists, it outranks the script heuristic.
        // This is critical for mixed CJK text: script identifies only one character
        // system, while the glossary can prove that multiple languages are present.
        var glossaryEvidence = glossaryScores
            .Where(x => x.Value > 0)
            .Select(x => new
            {
                Language = x.Key,
                Score = x.Value,
                FirstEvidenceIndex = GetFirstGlossaryIndex(source, x.Key)
            })
            .ToArray();

        // When mixed-language glossary evidence ties, the first substantive
        // language evidence in the source text is the primary language. This
        // avoids an arbitrary alphabetical tie-break and preserves the user's
        // source ordering (for example, "汽车 자동차" => Chinese primary).
        var language = glossaryEvidence.Length > 0
            ? glossaryEvidence
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.FirstEvidenceIndex)
                .ThenBy(x => x.Language, StringComparer.Ordinal)
                .First().Language
            : scores.Count == 0
                ? "Unknown"
                : scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;

        var topScore = scores.GetValueOrDefault(language);
        var secondScore = scores.Where(x => !string.Equals(x.Key, language, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value).DefaultIfEmpty(0).Max();
        // Mixed-language evidence must come from two substantive language signals,
        // not from the CJK script fallback bonus.
        var substantive = glossaryScores.Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .ToArray();
        var mixed = substantive.Length >= 2 &&
                    Math.Abs(substantive[0].Value - substantive[1].Value) <= 1;

        var glossary = language switch
        {
            "Chinese" => Chinese,
            "Japanese" => Japanese,
            "Korean" => Korean,
            _ => null
        };

        var translated = normalized;
        var hits = new List<string>();
        if (glossary is not null)
        {
            foreach (var pair in glossary.OrderByDescending(x => x.Key.Length))
            {
                if (!translated.Contains(pair.Key, StringComparison.Ordinal)) continue;
                translated = translated.Replace(pair.Key, $" {pair.Value} ", StringComparison.Ordinal);
                hits.Add($"{pair.Key} → {pair.Value}");
            }
        }

        translated = Normalize(translated);
        var confidence = CalculateConfidence(language, topScore, secondScore, hits.Count, mixed);
        var evidence = BuildEvidence(language, script, scores, hits, mixed);

        return new LanguageResult(language, normalized, translated, confidence, evidence, hits, mixed);
    }

    public ModelRecord Apply(ModelRecord model)
    {
        var source = string.Join(" ", model.Name ?? string.Empty, Path.GetFileNameWithoutExtension(model.Path ?? string.Empty));
        var result = Analyze(source);

        model.OriginalLanguage = result.Language;
        model.TranslatedTitle = result.TranslatedText;
        model.TranslationConfidence = result.Confidence;
        model.TranslationEvidence = result.Evidence;

        return model;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var value = text.Normalize(NormalizationForm.FormKC).Trim();
        value = Regex.Replace(value, @"[\u200B-\u200D\uFEFF]", string.Empty);
        value = Regex.Replace(value, @"[+_\-]+", " ");
        value = Regex.Replace(value, @"\s+", " ");

        // Remove diacritics for comparison-friendly Latin text while preserving CJK.
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static string? DetectScript(string text)
    {
        if (text.Any(c => c >= '\uAC00' && c <= '\uD7AF')) return "Korean";
        if (text.Any(c => c >= '\u3040' && c <= '\u30FF')) return "Japanese";
        if (text.Any(c => c >= '\u4E00' && c <= '\u9FFF')) return "Chinese";
        return null;
    }

    private int GetFirstGlossaryIndex(string source, string language)
    {
        var glossary = language switch
        {
            "Chinese" => Chinese,
            "Japanese" => Japanese,
            "Korean" => Korean,
            _ => null
        };

        if (glossary is null) return int.MaxValue;

        var first = int.MaxValue;
        foreach (var term in glossary.Keys)
        {
            var index = source.IndexOf(term, StringComparison.Ordinal);
            if (index >= 0 && index < first) first = index;
        }
        return first;
    }

    private static int CalculateConfidence(string language, int top, int second, int hits, bool mixed)
    {
        if (language == "Unknown") return 0;
        var confidence = 62 + Math.Min(20, top * 4) + Math.Min(12, hits * 3);
        if (second > 0) confidence -= Math.Min(15, second * 3);
        if (mixed) confidence -= 15;
        return Math.Clamp(confidence, 0, 99);
    }

    private static string BuildEvidence(
        string language,
        string? script,
        IReadOnlyDictionary<string, int> scores,
        IReadOnlyList<string> hits,
        bool mixed)
    {
        var scoreText = scores.Count == 0
            ? "none"
            : string.Join(", ", scores.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}"));

        var evidence = $"Language script={script ?? "Latin/Other"}; language scores={scoreText}";
        if (hits.Count > 0) evidence += $"; glossary={string.Join("; ", hits.Take(12))}";
        if (mixed) evidence += "; mixed-language signal";
        if (language == "Unknown") evidence += "; no supported language signal";
        return evidence;
    }
}
