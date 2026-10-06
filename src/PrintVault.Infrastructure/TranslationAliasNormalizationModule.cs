using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintVault.Infrastructure;

/// <summary>
/// Deterministic, offline boundary for multilingual translation and canonical alias normalization.
///
/// This module owns only language-text normalization, glossary translation, and canonical
/// alias matching. It does not classify categories, move files, or access the network.
/// </summary>
public sealed class TranslationAliasNormalizationModule
{
    public sealed record Result(
        string NormalizedText,
        string TranslatedText,
        IReadOnlyList<string> Terms);

    internal static readonly IReadOnlyDictionary<string, string> ChineseGlossary =
        new Dictionary<string, string>(StringComparer.Ordinal)
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

    internal static readonly IReadOnlyDictionary<string, string> JapaneseGlossary =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["車"]="car", ["自動車"]="automobile", ["飛行機"]="airplane", ["戦闘機"]="fighter jet",
            ["ヘリコプター"]="helicopter", ["ロケット"]="rocket", ["船"]="ship", ["城"]="castle",
            ["家"]="house", ["建物"]="building", ["塔"]="tower", ["龍"]="dragon", ["ドラゴン"]="dragon",
            ["恐竜"]="dinosaur", ["工具"]="tool", ["収納"]="storage", ["人形"]="figure",
            ["フィギュア"]="figure", ["オーガナイザー"]="organizer"
        };

    internal static readonly IReadOnlyDictionary<string, string> KoreanGlossary =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["자동차"]="car", ["차"]="car", ["비행기"]="airplane", ["전투기"]="fighter jet",
            ["헬리콥터"]="helicopter", ["로켓"]="rocket", ["배"]="ship", ["성"]="castle",
            ["집"]="house", ["건물"]="building", ["탑"]="tower", ["용"]="dragon", ["공룡"]="dinosaur",
            ["도구"]="tool", ["수납"]="storage", ["인형"]="figure", ["피규어"]="figure",
            ["정리함"]="organizer"
        };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var value = text.Normalize(NormalizationForm.FormKC).Trim();
        value = Regex.Replace(value, @"[\u200B-\u200D\uFEFF]", string.Empty);
        value = Regex.Replace(value, @"[+_\-]+", " ");
        value = Regex.Replace(value, @"\s+", " ");

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

    public static Result Translate(string? text, string language)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return new Result(string.Empty, string.Empty, Array.Empty<string>());

        var glossary = GetGlossary(language);
        var translated = normalized;
        var hits = new List<string>();

        foreach (var pair in glossary.OrderByDescending(x => x.Key.Length))
        {
            if (!translated.Contains(pair.Key, StringComparison.Ordinal)) continue;
            translated = translated.Replace(pair.Key, $" {pair.Value} ", StringComparison.Ordinal);
            hits.Add($"{pair.Key} → {pair.Value}");
        }

        return new Result(normalized, Normalize(translated), hits);
    }

    public static string? CanonicalizeAlias(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Normalize(text).ToLowerInvariant();
    }

    internal static IReadOnlyDictionary<string, string> GetGlossary(string language)
        => language switch
        {
            "Chinese" => ChineseGlossary,
            "Japanese" => JapaneseGlossary,
            "Korean" => KoreanGlossary,
            _ => new Dictionary<string, string>(StringComparer.Ordinal)
        };
}
