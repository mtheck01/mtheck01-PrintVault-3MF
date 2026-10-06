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

    private static readonly IReadOnlyDictionary<string, string> Chinese = TranslationAliasNormalizationModule.ChineseGlossary;
    private static readonly IReadOnlyDictionary<string, string> Japanese = TranslationAliasNormalizationModule.JapaneseGlossary;
    private static readonly IReadOnlyDictionary<string, string> Korean = TranslationAliasNormalizationModule.KoreanGlossary;

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

        // Mixed-language evidence must come from two substantive language signals,
        // not from the CJK script fallback bonus. When mixed evidence is present,
        // the first substantive glossary language in the source text is authoritative.
        // This prevents shorter nested glossary terms (for example Korean "차" inside
        // "자동차") from overpowering an earlier Chinese term such as "汽车".
        var substantive = glossaryScores.Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .ToArray();
        var mixed = substantive.Length >= 2 &&
                    Math.Abs(substantive[0].Value - substantive[1].Value) <= 1;

        var language = glossaryEvidence.Length > 0
            ? (mixed
                ? glossaryEvidence
                    .OrderBy(x => x.FirstEvidenceIndex)
                    .ThenByDescending(x => x.Score)
                    .ThenBy(x => x.Language, StringComparer.Ordinal)
                    .First().Language
                : glossaryEvidence
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.FirstEvidenceIndex)
                    .ThenBy(x => x.Language, StringComparer.Ordinal)
                    .First().Language)
            : scores.Count == 0
                ? "Unknown"
                : scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;

        var topScore = scores.GetValueOrDefault(language);
        var secondScore = scores.Where(x => !string.Equals(x.Key, language, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value).DefaultIfEmpty(0).Max();

        var glossary = language switch
        {
            "Chinese" => Chinese,
            "Japanese" => Japanese,
            "Korean" => Korean,
            _ => null
        };

        var translation = TranslationAliasNormalizationModule.Translate(source, language);
        var translated = translation.TranslatedText;
        var hits = translation.Terms.ToList();

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
        => TranslationAliasNormalizationModule.Normalize(text);

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
