using System.Text;
using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>Offline multilingual title normalizer. It never renames files and never calls a network service.</summary>
public sealed class MultilingualMetadataService
{
    private static readonly (string Language, string[] Terms)[] LanguageHints =
    {
        ("Chinese", new[] { "的", "和", "打印", "文件", "比例", "整体", "上传", "模型", "吊车", "叉车", "汽车", "飞机", "火箭", "城堡", "房子", "龙", "蝙蝠车", "千年隼", "霍格沃茨" }),
        ("Japanese", new[] { "印刷", "モデル", "車", "飛行機", "城", "ドラゴン", "建物", "収納", "工具", "恐竜" }),
        ("Korean", new[] { "인쇄", "모델", "자동차", "비행기", "로켓", "성", "용", "건물", "수납", "도구", "공룡" })
    };

    private static readonly Dictionary<string,string> Chinese = new(StringComparer.Ordinal)
    {
        ["吊车"]="crane", ["起重机"]="crane", ["叉车"]="forklift", ["挖掘机"]="excavator", ["推土机"]="bulldozer",
        ["卡车"]="truck", ["汽车"]="car", ["摩托车"]="motorcycle", ["飞机"]="airplane", ["直升机"]="helicopter",
        ["无人机"]="drone", ["火箭"]="rocket", ["坦克"]="tank", ["船"]="ship", ["潜艇"]="submarine",
        ["房子"]="house", ["房屋"]="building", ["建筑"]="building", ["城堡"]="castle", ["塔"]="tower",
        ["桥"]="bridge", ["树"]="tree", ["岩石"]="rock", ["山"]="mountain", ["龙"]="dragon",
        ["打印件"]="printed model", ["模型"]="model", ["文件"]="file", ["上传文件"]="uploaded file",
        ["整体"]="whole", ["比例"]="scale", ["套件"]="kit", ["支架"]="stand", ["底座"]="base", ["展示"]="display",
        ["收纳"]="storage", ["盒"]="box", ["架"]="rack", ["灯"]="lamp", ["钥匙扣"]="keychain",
        ["工具"]="tool", ["玩具"]="toy", ["装饰"]="decor", ["人物"]="character", ["角色"]="character"
    };

    private static readonly Dictionary<string,string> Japanese = new(StringComparer.Ordinal)
    {
        ["車"]="car", ["自動車"]="automobile", ["飛行機"]="airplane", ["戦闘機"]="fighter jet", ["ヘリコプター"]="helicopter",
        ["ロケット"]="rocket", ["船"]="ship", ["城"]="castle", ["家"]="house", ["建物"]="building", ["塔"]="tower",
        ["龍"]="dragon", ["ドラゴン"]="dragon", ["恐竜"]="dinosaur", ["工具"]="tool", ["収納"]="storage",
        ["人形"]="figure", ["フィギュア"]="figure", ["オーガナイザー"]="organizer"
    };

    private static readonly Dictionary<string,string> Korean = new(StringComparer.Ordinal)
    {
        ["자동차"]="car", ["차"]="car", ["비행기"]="airplane", ["전투기"]="fighter jet", ["헬리콥터"]="helicopter",
        ["로켓"]="rocket", ["배"]="ship", ["성"]="castle", ["집"]="house", ["건물"]="building", ["탑"]="tower",
        ["용"]="dragon", ["공룡"]="dinosaur", ["도구"]="tool", ["수납"]="storage", ["인형"]="figure", ["피규어"]="figure",
        ["정리함"]="organizer"
    };

    private readonly MultilingualEntityService entities = new();

    public ModelRecord Apply(ModelRecord model)
    {
        var result = Translate(model.Name);
        model.OriginalLanguage = result.Language;
        model.TranslatedTitle = result.Title;
        model.TranslationConfidence = result.Confidence;
        model.TranslationEvidence = result.Evidence;

        // Entity intelligence is additive metadata. It does not overwrite literal
        // translation confidence and it never changes the physical filename.
        var entity = entities.Recognize(model);
        if (entity is not null)
        {
            if (!string.IsNullOrWhiteSpace(model.TranslationEvidence)) model.TranslationEvidence += "; ";
            model.TranslationEvidence += entity.Evidence + $"; entity confidence: {entity.Confidence}%";
        }
        return model;
    }

    public (string Language, string Title, int Confidence, string Evidence) Translate(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "");
        var language = DetectLanguage(stem);
        if (language == "Unknown") return (language, stem, 0, "No multilingual terms detected");

        var translated = stem;
        var hits = new List<string>();
        var glossary = language switch
        {
            "Chinese" => Chinese,
            "Japanese" => Japanese,
            "Korean" => Korean,
            _ => new Dictionary<string,string>(StringComparer.Ordinal)
        };
        foreach (var kv in glossary.OrderByDescending(x => x.Key.Length))
        {
            if (translated.Contains(kv.Key, StringComparison.Ordinal))
            {
                translated = translated.Replace(kv.Key, $" {kv.Value} ", StringComparison.Ordinal);
                hits.Add($"{kv.Key} → {kv.Value}");
            }
        }
        translated = Regex.Replace(translated, @"[+_\-]+", " ");
        translated = Regex.Replace(translated, @"\s+", " ").Trim();
        translated = Regex.Replace(translated, @"\(\s*\)", "");
        var confidence = hits.Count >= 3 ? 94 : hits.Count == 2 ? 90 : hits.Count == 1 ? 82 : 55;
        var evidence = hits.Count == 0 ? "Language detected, but no offline glossary phrase matched" : "Offline glossary: " + string.Join("; ", hits.Take(8));
        return (language, translated, confidence, evidence);
    }

    private static string DetectLanguage(string text)
    {
        if (text.Any(c => c >= '\u4E00' && c <= '\u9FFF')) return "Chinese";
        if (text.Any(c => c >= '\u3040' && c <= '\u30FF')) return "Japanese";
        if (text.Any(c => c >= '\uAC00' && c <= '\uD7AF')) return "Korean";
        return "Unknown";
    }
}
