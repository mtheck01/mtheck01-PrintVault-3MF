using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Deterministic, offline recognition of named entities that are commonly represented
/// by translated model names. Entity recognition is intentionally separate from literal
/// translation: a phrase can identify a known fictional vehicle even when the literal
/// translation confidence is low. It never renames or modifies the physical .3mf file.
/// </summary>
public sealed record MultilingualEntityMatch(
    string EntityName,
    string Domain,
    string Category,
    string Subtype,
    int Confidence,
    string Evidence);

public sealed class MultilingualEntityService
{
    private sealed record EntityRule(
        string EntityName,
        string Domain,
        string Category,
        string Subtype,
        int Confidence,
        string[] Phrases);

    private static readonly EntityRule[] Rules =
    {
        new("Colonial Viper", "Battlestar Galactica", "Vehicles", "Spacecraft / Fighter", 98,
            new[] { "殖民地毒蛇号", "殖民地毒蛇", "殖民地毒蛇战机", "colonial viper", "colonial viper mk ii", "colonial viper mk 2", "colonial viper mk iii", "colonial viper mk 3" }),
        new("Batmobile", "DC", "Vehicles", "Fictional Vehicle", 98,
            new[] { "batmobile", "蝙蝠车", "蝙蝠車" }),
        new("DeLorean", "Back to the Future", "Vehicles", "Car", 98,
            new[] { "delorean", "de lorean", "德罗宁", "德羅寧" }),
        new("Millennium Falcon", "Star Wars", "Vehicles", "Spacecraft", 98,
            new[] { "millennium falcon", "千年隼", "千年鷹" }),
        new("X-wing", "Star Wars", "Vehicles", "Spacecraft / Fighter", 98,
            new[] { "x-wing", "xwing", "x 翼", "x翼战机", "x翼戰機" }),
        new("TIE Fighter", "Star Wars", "Vehicles", "Spacecraft / Fighter", 98,
            new[] { "tie fighter", "tie-fighter", "钛战机", "鈦戰機" }),
        new("Star Destroyer", "Star Wars", "Vehicles", "Spacecraft / Capital Ship", 98,
            new[] { "star destroyer", "帝国歼星舰", "帝國殲星艦", "歼星舰", "殲星艦" }),
        new("AT-AT", "Star Wars", "Vehicles", "Walker", 98,
            new[] { "at-at", "atat", "at at", "帝国步行机", "帝國步行機" }),
        new("AT-ST", "Star Wars", "Vehicles", "Walker", 98,
            new[] { "at-st", "atst", "at st", "侦察步行机", "偵察步行機" }),
        new("Klingon Bird-of-Prey", "Star Trek", "Vehicles", "Spacecraft / Warbird", 97,
            new[] { "klingon bird of prey", "bird of prey klingon" }),
        new("USS Enterprise", "Star Trek", "Vehicles", "Spacecraft / Starship", 97,
            new[] { "uss enterprise", "uss enterprise ncc-1701", "starship enterprise" }),
        new("Battlestar Galactica", "Battlestar Galactica", "Vehicles", "Spacecraft / Battlestar", 98,
            new[] { "battlestar galactica", "战星卡拉狄加", "戰星卡拉狄加" }),
        new("Serenity", "Firefly", "Vehicles", "Spacecraft / Transport", 97,
            new[] { "serenity firefly", "firefly serenity" }),
        new("Rocinante", "The Expanse", "Vehicles", "Spacecraft / Corvette", 97,
            new[] { "rocinante expanse", "rocinante ship" }),
        new("TARDIS", "Doctor Who", "Vehicles", "Time Machine / Spacecraft", 97,
            new[] { "tardis", "t.a.r.d.i.s" }),
        new("Ecto-1", "Ghostbusters", "Vehicles", "Car", 97,
            new[] { "ecto-1", "ecto 1", "ecto1" }),
        new("KITT", "Knight Rider", "Vehicles", "Car", 97,
            new[] { "kitt knight rider", "knight rider kitt" }),
        new("Mystery Machine", "Scooby-Doo", "Vehicles", "Van", 97,
            new[] { "mystery machine", "mysterymachine" }),
        // Whole-library coverage anchors: these are intentionally distinctive model
        // identifiers rather than broad aircraft/vehicle vocabulary. Variant suffixes,
        // printer names and dimensions remain context and do not change identity.
        new("A-10 Thunderbolt II", "Aviation", "Vehicles", "Aircraft / Attack Aircraft", 98,
            new[] { "a-10 thunderbolt", "a10 thunderbolt", "a-10 thunderbolt ii", "a10 thunderbolt ii", "a-10 warthog", "a10 warthog" }),
        new("Airbus A400M", "Aviation", "Vehicles", "Aircraft / Transport Aircraft", 98,
            new[] { "airbus a400m", "a400m atlas", "a400 m atlas", "a400m", "a400 m" }),
        new("AH-64 Apache", "Aviation", "Vehicles", "Aircraft / Attack Helicopter", 98,
            new[] { "ah-64 apache", "ah64 apache", "apache-ah64", "apache ah64", "apache ah-64" }),
        new("AH-6 Little Bird", "Aviation", "Vehicles", "Aircraft / Light Helicopter", 98,
            new[] { "ah-6 little bird", "ah6 little bird", "ah-6 littlebird" }),
        new("MH-6 Little Bird", "Aviation", "Vehicles", "Aircraft / Light Helicopter", 98,
            new[] { "mh-6 little bird", "mh6 little bird", "mh-6 littlebird" }),
        new("AV-8B Harrier", "Aviation", "Vehicles", "Aircraft / Attack Aircraft", 98,
            new[] { "av-8b harrier", "av8b harrier", "harrier ii" }),
        new("F-16 Fighting Falcon", "Aviation", "Vehicles", "Aircraft / Fighter", 98,
            new[] { "f-16 fighting falcon", "f16 fighting falcon", "f-16 falcon", "f16 falcon" }),
        new("P-51 Mustang", "Aviation", "Vehicles", "Aircraft / Fighter", 98,
            new[] { "p-51 mustang", "p51 mustang", "p-51d mustang", "p51d mustang", "p-51" }),
        new("PBY Catalina", "Aviation", "Vehicles", "Aircraft / Flying Boat", 98,
            new[] { "pby-5a", "pby 5a", "pby catalina", "pby-6a" }),
        new("P-38 Lightning", "Aviation", "Vehicles", "Aircraft / Fighter", 98,
            new[] { "p-38 lightning", "p38 lightning", "p-38 final", "p-38" }),
        new("T-6 Texan", "Aviation", "Vehicles", "Aircraft / Trainer", 98,
            new[] { "t-6 texan", "t6 texan", "north american t-6" }),
        new("Boeing 747", "Aviation", "Vehicles", "Aircraft / Airliner", 98,
            new[] { "boeing 747", "747-8", "747 8", "747-8f" }),
        new("Airbus A380", "Aviation", "Vehicles", "Aircraft / Airliner", 98,
            new[] { "airbus a380", "a380" }),
        new("Airbus A318", "Aviation", "Vehicles", "Aircraft / Airliner", 98,
            new[] { "airbus a318", "a318" }),
        new("Antonov An-225", "Aviation", "Vehicles", "Aircraft / Heavy Transport", 98,
            new[] { "an-225", "an 225", "antonov an-225", "antonov an 225" }),
        new("F-111 Aardvark", "Aviation", "Vehicles", "Aircraft / Strike Aircraft", 98,
            new[] { "f-111 aardvark", "f111 aardvark", "f-111" }),
        new("Supermarine Spitfire", "Aviation", "Vehicles", "Aircraft / Fighter", 98,
            new[] { "supermarine spitfire", "spitfire mk", "spitfire mk.9" }),
        new("Hawker Sea Fury", "Aviation", "Vehicles", "Aircraft / Fighter", 98,
            new[] { "hawker sea fury", "sea fury", "seafury" }),
        new("M3A3 Bradley", "Military Vehicles", "Vehicles", "Armored Vehicle / Infantry Fighting Vehicle", 98,
            new[] { "m3a3 bradley", "m3a3_bradley", "m3a3+bradley" }),
        new("TIE Advanced", "Star Wars", "Vehicles", "Spacecraft / Fighter", 98,
            new[] { "tie advanced", "tie_advanced", "tie-advanced", "t i e advanced" }),
        new("Wall-E", "WALL-E", "Figures & Characters", "Character / Robot", 98,
            new[] { "wall-e", "wall e", "walle articulated" }),
        new("Wile E. Coyote", "Looney Tunes", "Figures & Characters", "Character", 98,
            new[] { "wile e. coyote", "wile e coyote", "wile e.+coyote", "wile+coyote" }),
        new("Pac-Man", "Pac-Man", "Figures & Characters", "Character / Game", 98,
            new[] { "pac-man", "pac man", "pacman" }),
        new("Zelda Master Sword", "The Legend of Zelda", "Props & Accessories", "Prop / Sword", 98,
            new[] { "zelda master sword", "zelda mastersword", "master sword zelda", "zelda+mastersword" }),
        new("Luffy", "One Piece", "Figures & Characters", "Character", 98,
            new[] { "luffy", "monkey d luffy", "monkey+d+luffy" }),
        new("Nami", "One Piece", "Figures & Characters", "Character", 98,
            new[] { "nami one piece", "nami serie one piece", "nami+serie+one+piece" }),
        new("Sanji", "One Piece", "Figures & Characters", "Character", 98,
            new[] { "sanji one piece", "sanji+4c+collection" }),
        new("Cinderwing3D Tiny Horse", "Cinderwing3D", "Figures & Characters", "Animal / Articulated Horse", 97,
            new[] { "cinderwing3d tiny horse", "cinderwing3d+tiny+horse", "cinderwing3d tiny horse skeleton" }),
        new("A-4 Skyhawk", "Aviation", "Vehicles", "Aircraft / Attack Aircraft", 98,
            new[] { "a-4 skyhawk", "a4 skyhawk", "a-4e skyhawk", "a4e skyhawk", "a4 blue angels", "skyhawk" }),
        new("General Lee", "The Dukes of Hazzard", "Vehicles", "Car", 97,
            new[] { "general lee charger", "general lee car" }),
        new("Colonial Raptor", "Battlestar Galactica", "Vehicles", "Spacecraft / Shuttle", 96,
            new[] { "殖民地猛禽", "殖民地猛禽号", "colonial raptor" }),
        // Generic multilingual vehicle/entity vocabulary is intentionally part of the
        // entity layer, not only the Smart Category keyword layer. This lets translated
        // titles such as "11200吊车64比例" flow through the same production pipeline as
        // named fictional entities. The original filename is never modified.
        new("Crane", "Industrial Equipment", "Vehicles", "Heavy Equipment / Crane", 96,
            new[] { "吊车", "起重机", "移动吊车", "汽车吊", "crane", "mobile crane", "truck crane" }),
        new("Forklift", "Industrial Equipment", "Vehicles", "Heavy Equipment / Forklift", 96,
            new[] { "叉车", "电叉车", "堆高车", "forklift", "fork lift" }),
        new("Excavator", "Industrial Equipment", "Vehicles", "Heavy Equipment / Excavator", 96,
            new[] { "挖掘机", "挖土机", "excavator", "digger" }),
        new("Bulldozer", "Industrial Equipment", "Vehicles", "Heavy Equipment / Bulldozer", 96,
            new[] { "推土机", "铲土机", "bulldozer", "dozer" }),
        new("Truck", "Vehicles", "Vehicles", "Truck", 96,
            new[] { "卡车", "货车", "载货车", "truck", "lorry" }),
        new("Automobile", "Vehicles", "Vehicles", "Car", 95,
            new[] { "汽车", "轿车", "小汽车", "automobile", "car" }),
        new("Motorcycle", "Vehicles", "Vehicles", "Motorcycle", 96,
            new[] { "摩托车", "摩托", "motorcycle", "motorbike" }),
        new("Airplane", "Aviation", "Vehicles", "Aircraft / Airplane", 96,
            new[] { "飞机", "客机", "战斗机", "airplane", "aircraft", "fighter jet" }),
        new("Helicopter", "Aviation", "Vehicles", "Aircraft / Helicopter", 96,
            new[] { "直升机", "直升機", "helicopter", "chopper" }),
        new("Drone", "Aviation", "Vehicles", "Aircraft / Drone", 96,
            new[] { "无人机", "無人機", "drone", "uav" }),
        new("Rocket", "Aerospace", "Vehicles", "Spacecraft / Rocket", 96,
            new[] { "火箭", "火箭模型", "rocket" }),
        // Cross-domain semantic entities. These intentionally use distinctive phrases
        // or known named entities so the entity layer can exercise the shared
        // semantic-domain -> category architecture without broad single-word triggers.
        new("Hogwarts", "Harry Potter", "Buildings", "Fantasy Building / Castle", 97,
            new[] { "hogwarts", "hoghwarts", "霍格沃茨", "霍格華茲" }),
        new("Eiffel Tower", "Landmark", "Buildings", "Landmark / Tower", 97,
            new[] { "eiffel tower", "tour eiffel", "埃菲尔铁塔", "艾菲爾鐵塔" }),
        new("Empire State Building", "Landmark", "Buildings", "Landmark / Skyscraper", 97,
            new[] { "empire state building", "帝国大厦", "帝國大廈" }),
        new("Pikachu", "Pokémon", "Figures & Characters", "Character / Creature", 97,
            new[] { "pikachu", "皮卡丘" }),
        new("Mario", "Super Mario", "Figures & Characters", "Character", 97,
            new[] { "super mario", "mario figure", "mario statue", "超级马里奥", "超級瑪利歐" }),
        new("Batman", "DC", "Figures & Characters", "Character / Superhero", 97,
            new[] { "batman figure", "batman bust", "蝙蝠侠模型", "蝙蝠俠模型" }),
        new("Mount Fuji", "Landmark / Nature", "Nature & Scenery", "Natural Landmark / Mountain", 97,
            new[] { "mount fuji", "fuji mountain", "富士山" }),
        new("Great Tree", "Nature", "Nature & Scenery", "Scenery / Tree", 95,
            new[] { "great tree scenery", "fantasy tree scenery", "大树景观", "大樹景觀" }),
        new("Dungeon Terrain", "Tabletop", "Tabletop Terrain", "Dungeon Terrain", 96,
            new[] { "dungeon terrain", "dungeon tile", "dungeon tiles", "地牢地形", "地牢地砖", "地牢地磚" }),
        new("Wargaming Terrain", "Tabletop", "Tabletop Terrain", "Wargaming Terrain", 96,
            new[] { "wargaming terrain", "war gaming terrain", "tabletop terrain", "战棋地形", "戰棋地形" }),
        new("One Ring", "The Lord of the Rings", "Props & Accessories", "Prop / Ring", 97,
            new[] { "one ring", "one ring prop", "至尊魔戒", "至尊魔戒道具" }),
        new("Portal Gun", "Portal", "Props & Accessories", "Prop / Fictional Device", 97,
            new[] { "portal gun prop", "portal gun replica", "传送门枪", "傳送門槍" }),
        new("Desk Organizer", "Household", "Functional", "Organizer", 96,
            new[] { "desk organizer", "desktop organizer", "desk storage organizer", "桌面收纳", "桌面收納" }),
        new("Cable Management", "Household", "Functional", "Cable Management", 96,
            new[] { "cable management", "cable organizer", "cable holder", "理线器", "理線器" }),
        new("Wrench", "Workshop", "Tools & Workshop", "Tool / Wrench", 96,
            new[] { "wrench organizer", "wrench holder", "wrench rack", "扳手收纳", "扳手收納" }),
        new("Screwdriver", "Workshop", "Tools & Workshop", "Tool / Screwdriver", 96,
            new[] { "screwdriver organizer", "screwdriver holder", "螺丝刀收纳", "螺絲刀收納" }),
        new("Dice Tower", "Tabletop Games", "Toys & Games", "Game Accessory / Dice Tower", 97,
            new[] { "dice tower", "dice tower game", "骰塔", "骰子塔" }),
        new("Fidget Toy", "Toys", "Toys & Games", "Toy / Fidget", 96,
            new[] { "fidget toy", "fidget spinner toy", "解压玩具", "解壓玩具" }),
        new("Sculpture", "Art", "Art & Decor", "Art / Sculpture", 95,
            new[] { "sculpture art", "art sculpture", "decorative sculpture", "雕塑艺术", "雕塑藝術" }),
        new("Decorative Vase", "Art & Decor", "Art & Decor", "Decor / Vase", 95,
            new[] { "decorative vase", "ornamental vase", "装饰花瓶", "裝飾花瓶" }),
    };

    public MultilingualEntityMatch? Recognize(ModelRecord model)
        => Recognize(string.Join(" ", model.Name ?? "", Path.GetFileNameWithoutExtension(model.Path ?? ""), model.TranslatedTitle ?? ""));

    public MultilingualEntityMatch? Recognize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = TranslationAliasNormalizationModule.CanonicalizeAlias(text) ?? string.Empty;
        var matches = Rules
            .Select(rule => (rule, phrase: rule.Phrases.FirstOrDefault(p => ContainsPhrase(normalized, p))))
            .Where(x => x.phrase is not null)
            .OrderByDescending(x => x.phrase!.Length)
            .ThenByDescending(x => x.rule.Confidence)
            .ToList();
        if (matches.Count == 0) return null;

        var hit = matches[0];
        return new MultilingualEntityMatch(
            hit.rule.EntityName,
            hit.rule.Domain,
            hit.rule.Category,
            hit.rule.Subtype,
            hit.rule.Confidence,
            $"Named entity: {hit.rule.EntityName} ({hit.rule.Domain}); matched phrase: {hit.phrase}");
    }

    public string? BuildEvidence(ModelRecord model)
    {
        var match = Recognize(model);
        return match is null ? null : $"{match.Evidence}; entity confidence: {match.Confidence}%";
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var p = phrase.ToLowerInvariant().Trim();
        if (p.Any(IsCjkLike) || p.Any(c => c >= '\u3040' && c <= '\u30FF') || p.Any(c => c >= '\uAC00' && c <= '\uD7AF'))
            return text.Contains(p, StringComparison.OrdinalIgnoreCase);

        var normalizedText = new string(text.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var normalizedPhrase = new string(p.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var textTokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var phraseTokens = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (phraseTokens.Length == 0 || textTokens.Length < phraseTokens.Length) return false;
        for (var i = 0; i <= textTokens.Length - phraseTokens.Length; i++)
        {
            var match = true;
            for (var j = 0; j < phraseTokens.Length; j++)
            {
                if (!string.Equals(textTokens[i + j], phraseTokens[j], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }

        // Some real-world catalog filenames prepend a numeric SKU/index directly to
        // a distinctive entity name (for example, "89batmobile_wcad_edition").
        // Treat a single-token entity phrase as a match when the only extra prefix
        // is numeric. This is intentionally narrower than arbitrary substring
        // matching so generic vocabulary cannot manufacture entity identities.
        if (phraseTokens.Length == 1)
        {
            var phraseToken = phraseTokens[0];
            foreach (var token in textTokens)
            {
                if (token.Length <= phraseToken.Length ||
                    !token.EndsWith(phraseToken, StringComparison.OrdinalIgnoreCase)) continue;
                var prefix = token[..^phraseToken.Length];
                if (prefix.Length > 0 && prefix.All(char.IsDigit)) return true;
            }
        }

        return false;
    }

    private static bool IsCjkLike(char c) =>
        (c >= '\u4E00' && c <= '\u9FFF') ||
        (c >= '\u3400' && c <= '\u4DBF');
}
