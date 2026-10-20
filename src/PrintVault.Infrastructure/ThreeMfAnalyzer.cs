using System.Collections.Concurrent;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Net;
using System.Xml.Linq;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record ThreeMfSignalCandidateTrace(string Category, double Score, IReadOnlyList<string> MatchedPhrases);

/// <summary>Bounded, metadata-only diagnostics for a 3MF source analysis.</summary>
public sealed class ThreeMfAnalysisTrace
{
    public bool ArchiveOpened { get; set; }
    public bool MainModelFound { get; set; }
    public int ArchiveEntryCount { get; set; }
    public IReadOnlyList<string> ModelPartEntries { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SemanticInputs { get; set; } = Array.Empty<string>();
    public bool SemanticInputsTruncated { get; set; }
    public int SemanticInputCharacters { get; set; }
    public IReadOnlyList<ThreeMfSignalCandidateTrace> TopCandidates { get; set; } = Array.Empty<ThreeMfSignalCandidateTrace>();
    public IReadOnlyList<string> SelectedSignals { get; set; } = Array.Empty<string>();
    public int ObjectCount { get; set; }
    public int VertexCount { get; set; }
    public int BuildItemCount { get; set; }
    public long TotalElapsedMilliseconds { get; set; }
    public long MainModelXmlLoadMilliseconds { get; set; }
    public long PackageMetadataReadMilliseconds { get; set; }
    public long SemanticScoringMilliseconds { get; set; }
    public long GeometryResolutionMilliseconds { get; set; }
    public int GeometryModelParts { get; set; }
    public int GeometryComponentReferences { get; set; }
    public int GeometryUnresolvedReferences { get; set; }
    public int GeometryCycles { get; set; }
    public long GeometryReachableVertices { get; set; }
    public long GeometryReachableTriangles { get; set; }
    public bool? ComponentGeometryCountsExact { get; set; }
    public string? FailureType { get; set; }
    public string? FailureMessage { get; set; }
}

/// <summary>Offline semantic 3MF intelligence engine. It combines filename semantics,
/// 3MF XML structure, embedded metadata, slicer/material evidence, geometry extents and weighted
/// intent signals. Folder names outside the 3MF container are deliberately excluded from
/// semantic classification so library category folders cannot contaminate the result. It is deterministic and requires no cloud service or API key.</summary>
public sealed class ThreeMfAnalyzer
{
    private readonly ComponentAwareGeometryResolver geometryResolver = new();
    // Signals are evaluated for every catalog record. Recompiling each phrase regex on
    // every call evicts patterns from .NET's small regex cache and causes repeated parsing
    // of the same expressions across the entire library scan.
    private static readonly ConcurrentDictionary<string, Regex> TokenRegexCache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Signal(string Category, string Phrase, double Weight, string Tag, string Family, string Type, string Subtype);
    private sealed record SpecialDetection(string Category, double Score, string Family, string Type, string Subtype, string Tag, string Reason);

    private static readonly string[] KeychainTerms =
    {
        "keychain", "key-chain", "key ring", "keyring", "key fob", "key-fob", "keytag", "key tag"
    };

    private static SpecialDetection? DetectSpecialType(string text, ZipArchive archive)
    {
        var hueHits = 0;
        if (Regex.IsMatch(text, @"(?<![a-z0-9])hueforge(?![a-z0-9])", RegexOptions.IgnoreCase)) hueHits += 4;
        if (archive.Entries.Any(e => e.FullName.Contains("hueforge", StringComparison.OrdinalIgnoreCase))) hueHits += 4;
        if (text.Contains("custom_gcode_per_layer", StringComparison.OrdinalIgnoreCase)) hueHits += 2;
        if (text.Contains("layer_config_ranges", StringComparison.OrdinalIgnoreCase)) hueHits += 2;
        if (text.Contains("color stack", StringComparison.OrdinalIgnoreCase) || text.Contains("colour stack", StringComparison.OrdinalIgnoreCase)) hueHits += 2;
        if (hueHits >= 4)
        {
            var subtype = text.Contains("tile", StringComparison.OrdinalIgnoreCase) ? "Tile" :
                          text.Contains("flatforge", StringComparison.OrdinalIgnoreCase) ? "FlatForge" :
                          text.Contains("colordrop", StringComparison.OrdinalIgnoreCase) ? "ColorDrop" : "Color Stack";
            return new SpecialDetection("HueForge", Math.Min(16, hueHits), "HueForge", "HueForge", subtype, "hueforge", $"HueForge evidence ({subtype})");
        }

        var keyHits = KeychainTerms.Count(t => Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(t)}(?![a-z0-9])", RegexOptions.IgnoreCase));
        if (keyHits > 0)
            return new SpecialDetection("Keychains", Math.Min(12, 5 + keyHits * 3), "Functional", "Keychain", "Keychain", "keychain", "Keychain terminology detected");
        return null;
    }

    private static (string Method, double Confidence, string Evidence) DetectPrintMethod(string text, ZipArchive archive, string slicer, string materials)
    {
        var fdm = 0.0; var resin = 0.0; var evidence = new List<string>();
        void Add(ref double score, double amount, string why) { score += amount; if (!evidence.Contains(why, StringComparer.OrdinalIgnoreCase)) evidence.Add(why); }
        if (Regex.IsMatch(text, @"\b(fdm|filament|fff|material extrusion|gcode|nozzle|extruder|hotend|layer height)\b", RegexOptions.IgnoreCase)) Add(ref fdm, 3, "FDM/filament configuration evidence");
        if (Regex.IsMatch(text, @"\b(pla|petg|abs|asa|tpu|pc|nylon|pa|pla-cf|petg-cf)\b", RegexOptions.IgnoreCase)) Add(ref fdm, 2.5, "Filament material evidence");
        if (Regex.IsMatch(text, @"\b(sla|msla|dlp|lcd|resin|photopolymer|uv resin|vat)\b", RegexOptions.IgnoreCase)) Add(ref resin, 3, "Resin technology evidence");
        if (Regex.IsMatch(text, @"\b(405nm|lift distance|bottom exposure|normal exposure|anti-aliasing|bottom layers)\b", RegexOptions.IgnoreCase)) Add(ref resin, 3, "Resin exposure/support evidence");
        if (Regex.IsMatch(text, @"\b(nozzle diameter|extrusion width|infill|perimeters|wall loops|retraction)\b", RegexOptions.IgnoreCase)) Add(ref fdm, 2, "FDM slicing settings");
        if (Regex.IsMatch(text, @"\b(layer height|first layer height)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(text, @"\b(resin|exposure)\b", RegexOptions.IgnoreCase)) Add(ref fdm, 1, "Layer-height slicing evidence");
        if (slicer.Length > 0) Add(ref fdm, 0.5, $"Slicer: {slicer}");
        if (!string.IsNullOrWhiteSpace(materials) && Regex.IsMatch(materials, @"\b(PLA|PETG|ABS|ASA|TPU|Nylon|PA)\b", RegexOptions.IgnoreCase)) Add(ref fdm, 2, "Detected filament material");
        if (!string.IsNullOrWhiteSpace(materials) && Regex.IsMatch(materials, @"\b(resin|photopolymer)\b", RegexOptions.IgnoreCase)) Add(ref resin, 2, "Detected resin material");
        var total = fdm + resin;
        if (total < 3) return ("Unknown", Math.Min(.45, total / 6.0), "Insufficient print-method evidence");
        if (Math.Abs(fdm - resin) < 2.0) return ("Both", Math.Min(.85, .50 + total * .025), string.Join("; ", evidence.Take(4)) + "; conflicting technology evidence");
        var method = fdm > resin ? "FDM" : "Resin";
        var lead = Math.Max(fdm, resin);
        var confidence = Math.Clamp(.45 + lead * .065 + Math.Abs(fdm-resin)*.035, .45, .99);
        return (method, confidence, string.Join("; ", evidence.Take(4)));
    }

    private static readonly Signal[] Signals =
    {
        // Figures / characters
        new("Figures & Characters","dragon",7,"dragon","Figure","Character","Dragon"), new("Figures & Characters","dinosaur",7,"dinosaur","Figure","Character","Dinosaur"),
        new("Figures & Characters","miniature",6,"miniature","Figure","Miniature","Miniature"), new("Figures & Characters","minifig",6,"minifig","Figure","Character","Minifigure"),
        new("Figures & Characters","figurine",6,"figurine","Figure","Figure","Figurine"), new("Figures & Characters","character",5,"character","Figure","Character","Character"),
        new("Figures & Characters","soldier",5,"soldier","Figure","Character","Soldier"), new("Figures & Characters","robot",5,"robot","Figure","Character","Robot"),
        new("Figures & Characters","bust",5,"bust","Figure","Figure","Bust"), new("Figures & Characters","skull",4,"skull","Figure","Figure","Skull"),
        // Vehicles
        new("Vehicles","a-10",10,"a-10","Vehicle","Vehicle","Aircraft"), new("Vehicles","a-10 thunderbolt",12,"a-10-thunderbolt","Vehicle","Vehicle","Aircraft"), new("Vehicles","ah-64",10,"ah-64","Vehicle","Vehicle","Helicopter"), new("Vehicles","ac130",10,"ac130","Vehicle","Vehicle","Aircraft"), new("Vehicles","b-1",10,"b-1","Vehicle","Vehicle","Aircraft"), new("Vehicles","bf-109",10,"bf-109","Vehicle","Vehicle","Aircraft"), new("Vehicles","f-16",10,"f-16","Vehicle","Vehicle","Aircraft"), new("Vehicles","p-51",10,"p-51","Vehicle","Vehicle","Aircraft"), new("Vehicles","pby",10,"pby","Vehicle","Vehicle","Aircraft"),
        new("Vehicles","batmobile",10,"batmobile","Vehicle","Vehicle","Car"), new("Vehicles","batwing",10,"batwing","Vehicle","Vehicle","Aircraft"),
        new("Vehicles","batboat",10,"batboat","Vehicle","Vehicle","Boat"), new("Vehicles","batpod",10,"batpod","Vehicle","Vehicle","Motorcycle"),
        new("Vehicles","delorean",10,"delorean","Vehicle","Vehicle","Car"), new("Vehicles","de lorean",10,"delorean","Vehicle","Vehicle","Car"),
        new("Vehicles","x-wing",10,"x-wing","Vehicle","Vehicle","Starfighter"), new("Vehicles","xwing",10,"x-wing","Vehicle","Vehicle","Starfighter"),
        new("Vehicles","tie fighter",10,"tie-fighter","Vehicle","Vehicle","Starfighter"), new("Vehicles","millennium falcon",10,"millennium-falcon","Vehicle","Vehicle","Spaceship"),
        new("Vehicles","star destroyer",10,"star-destroyer","Vehicle","Vehicle","Spaceship"), new("Vehicles","spaceship",9,"spaceship","Vehicle","Vehicle","Spaceship"),
        new("Vehicles","spacecraft",9,"spacecraft","Vehicle","Vehicle","Spaceship"), new("Vehicles","starship",9,"starship","Vehicle","Vehicle","Spaceship"),
        new("Vehicles","aircraft",8,"aircraft","Vehicle","Vehicle","Aircraft"), new("Vehicles","airplane",8,"aircraft","Vehicle","Vehicle","Aircraft"),
        new("Vehicles","plane",7,"aircraft","Vehicle","Vehicle","Aircraft"), new("Vehicles","helicopter",8,"helicopter","Vehicle","Vehicle","Helicopter"),
        new("Vehicles","jet",7,"jet","Vehicle","Vehicle","Jet"), new("Vehicles","car",7,"car","Vehicle","Vehicle","Car"), new("Vehicles","truck",7,"truck","Vehicle","Vehicle","Truck"),
        new("Vehicles","suv",7,"suv","Vehicle","Vehicle","SUV"), new("Vehicles","sedan",7,"sedan","Vehicle","Vehicle","Car"),
        new("Vehicles","motorcycle",7,"motorcycle","Vehicle","Vehicle","Motorcycle"), new("Vehicles","motorbike",7,"motorcycle","Vehicle","Vehicle","Motorcycle"),
        new("Vehicles","bicycle",7,"bicycle","Vehicle","Vehicle","Bicycle"), new("Vehicles","scooter",7,"scooter","Vehicle","Vehicle","Scooter"),
        new("Vehicles","tank",7,"tank","Vehicle","Vehicle","Tank"), new("Vehicles","rover",7,"rover","Vehicle","Vehicle","Rover"),
        new("Vehicles","ship",7,"ship","Vehicle","Vehicle","Ship"), new("Vehicles","boat",7,"boat","Vehicle","Vehicle","Boat"),
        new("Vehicles","submarine",7,"submarine","Vehicle","Vehicle","Submarine"), new("Vehicles","train",7,"train","Vehicle","Vehicle","Train"),
        new("Vehicles","locomotive",7,"train","Vehicle","Vehicle","Train"), new("Vehicles","tractor",7,"tractor","Vehicle","Vehicle","Tractor"),
        // Terrain / props / buildings
        new("Terrain & Props","terrain",7,"terrain","Prop","Terrain","Terrain"), new("Terrain & Props","castle",6,"castle","Prop","Building","Castle"),
        new("Terrain & Props","building",5,"building","Prop","Building","Building"), new("Terrain & Props","house",5,"house","Prop","Building","House"),
        new("Terrain & Props","tree",5,"tree","Prop","Scenery","Tree"), new("Terrain & Props","rock",4,"rock","Prop","Scenery","Rock"),
        new("Terrain & Props","diorama",6,"diorama","Prop","Scenery","Diorama"), new("Terrain & Props","ruin",5,"ruins","Prop","Building","Ruin"),
        // Tools / workshop
        new("Tools & Workshop","wrench",7,"wrench","Tool","Tool","Wrench"), new("Tools & Workshop","screwdriver",7,"screwdriver","Tool","Tool","Screwdriver"),
        new("Tools & Workshop","caliper",7,"caliper","Tool","Tool","Caliper"), new("Tools & Workshop","jig",7,"jig","Tool","Fixture","Jig"),
        new("Tools & Workshop","workshop",5,"workshop","Tool","Workshop","Workshop"), new("Tools & Workshop","drill",5,"drill","Tool","Tool","Drill"),
        // Functional
        new("Functional","bracket",6,"bracket","Functional","Functional Part","Bracket"), new("Functional","adapter",6,"adapter","Functional","Functional Part","Adapter"),
        new("Functional","mount",5,"mount","Functional","Functional Part","Mount"), new("Functional","hinge",6,"hinge","Functional","Mechanical","Hinge"),
        new("Functional","enclosure",6,"enclosure","Functional","Functional Part","Enclosure"), new("Functional","replacement",5,"replacement-part","Functional","Replacement Part","Replacement"),
        new("Functional","gear",5,"gear","Functional","Mechanical","Gear"), new("Functional","bearing",5,"bearing","Functional","Mechanical","Bearing"),
        new("Functional","connector",5,"connector","Functional","Functional Part","Connector"), new("Functional","spacer",4,"spacer","Functional","Functional Part","Spacer"),
        // Household / organization
        new("Household","organizer",7,"organizer","Household","Organization","Organizer"), new("Household","storage",5,"storage","Household","Organization","Storage"),
        new("Household","hook",5,"hook","Household","Utility","Hook"), new("Household","hanger",5,"hanger","Household","Utility","Hanger"),
        new("Household","drawer",5,"drawer","Household","Organization","Drawer"), new("Household","container",5,"container","Household","Container","Container"),
        // Toys / games
        new("Toys & Games","dice",7,"dice","Game/Toy","Game","Dice"), new("Toys & Games","dnd",6,"dnd","Game/Toy","Game","Tabletop"),
        new("Toys & Games","chess",7,"chess","Game/Toy","Game","Chess"), new("Toys & Games","puzzle",6,"puzzle","Game/Toy","Puzzle","Puzzle"),
        new("Toys & Games","toy",5,"toy","Game/Toy","Toy","Toy"), new("Toys & Games","game",4,"game","Game/Toy","Game","Game"),
        // Art / decor
        new("Art & Decor","vase",7,"vase","Decor","Decor","Vase"), new("Art & Decor","ornament",6,"ornament","Decor","Decor","Ornament"),
        new("Art & Decor","lamp",5,"lamp","Decor","Decor","Lamp"), new("Art & Decor","statue",5,"statue","Decor","Art","Statue"),
        new("Art & Decor","lithophane",7,"lithophane","Decor","Art","Lithophane"), new("Art & Decor","decor",4,"decor","Decor","Decor","Decor")
    };

    private static readonly Dictionary<string,string[]> SlicerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrusaSlicer"] = new[]{"prusaslicer","prusa_slicer","prusaslicer_profile"},
        ["Bambu Studio"] = new[]{"bambu studio","bambustudio","bambu_"},
        ["OrcaSlicer"] = new[]{"orcaslicer","orca_slicer"},
        ["Cura"] = new[]{"ultimaker cura","curaengine","cura"},
        ["SuperSlicer"] = new[]{"superslicer"},
        ["IdeaMaker"] = new[]{"ideamaker"}
    };

    public IntelligenceResult AnalyzeFilenameOnly(string path, string name)
    {
        var category = "Uncategorized";
        var reason = "Filename-only classification";
        var family = "";
        var type = "Unknown";
        var subtype = "";
        var score = 0.12;
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<string>();
        var risk = new List<string> { "Autonomous classification-only pass" };
        var semantic = NormalizeSemanticSeparators(name.ToLowerInvariant());
        var compactSemantic = CompactForTokenMatching(semantic);
        var scored = Signals
            .Select(s => (s, score: TokenScore(semantic, s.Phrase, s.Weight, compactSemantic)))
            .Where(x => x.score > 0)
            .GroupBy(x => x.s.Category)
            .Select(g => new { g.Key, Score = g.Sum(x => x.score), Hits = g.OrderByDescending(x => x.score).Take(4).ToList() })
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count > 0 && scored[0].Score >= 2.5)
        {
            category = scored[0].Key;
            var best = scored[0].Hits.First();
            family = best.s.Family;
            type = best.s.Type;
            subtype = best.s.Subtype;
            foreach (var h in scored[0].Hits) tags.Add(h.s.Tag);
            reason = string.Join(", ", scored[0].Hits.Select(h => h.s.Phrase).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
            evidence.Add($"{category}: {reason}");
            score = Math.Clamp(score + Math.Min(.65, scored[0].Score * .04), 0, .99);
        }
        else
        {
            risk.Add("Low filename semantic confidence");
        }

        tags.Add(category.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(family))
            tags.Add(family.ToLowerInvariant().Replace("/", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal));

        return new IntelligenceResult(
            category, score, string.Join(" | ", evidence.Take(4)), family, 0, "", "", "", false,
            type, subtype, string.Join(", ", tags.Take(12)), string.Join(", ", risk.Distinct(StringComparer.OrdinalIgnoreCase)),
            "Unknown", 0, "Autonomous classification-only pass", "");
    }

    public IntelligenceResult Analyze(string path, string name) => AnalyzeCore(path, name, false, false, out _);

    public IntelligenceResult Analyze(string path, string name, out ThreeMfAnalysisTrace trace)
        => AnalyzeCore(path, name, true, true, out trace);

    /// <summary>
    /// Produces the full semantic evidence trace while using the bounded component-geometry
    /// reachability probe. Whole-library diagnostics need semantic provenance, not exact
    /// vertex/triangle totals; exact geometry remains available through the trace overload.
    /// </summary>
    public IntelligenceResult AnalyzeForensicFastGeometry(string path, string name, out ThreeMfAnalysisTrace trace)
        => AnalyzeCore(path, name, true, false, out trace);

    private IntelligenceResult AnalyzeCore(
        string path, string name, bool captureTrace, bool exactComponentGeometry, out ThreeMfAnalysisTrace trace)
    {
        trace = captureTrace ? new ThreeMfAnalysisTrace() : null!;
        var capturedTrace = trace;
        var category="Uncategorized"; var reason="No strong semantic signal"; var family=""; var type="Unknown"; var subtype="";
        var slicer=""; var materials=""; var objects=0; var dimensions=""; var ready=false; var risk=new List<string>(); var printMethod="Unknown"; var printMethodConfidence=0d; var printMethodEvidence="Insufficient print-method evidence"; var specialType="";
        var score=0.12; var tags=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence=new List<string>();
        var semanticInputs = captureTrace ? new List<string>() : null;
        var sourceSemanticEvidence = new StringBuilder();
        var totalStarted = captureTrace ? Stopwatch.GetTimestamp() : 0L;
        void AppendSourceEvidence(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || sourceSemanticEvidence.Length >= 2000) return;
            var decoded = WebUtility.HtmlDecode(value);
            var normalized = Regex.Replace(Regex.Replace(decoded, @"<[^>]{1,200}>", " "), @"\s+", " ").Trim();
            if (normalized.Length == 0) return;
            if (sourceSemanticEvidence.Length > 0) sourceSemanticEvidence.Append(' ');
            var remaining = 2000 - sourceSemanticEvidence.Length;
            sourceSemanticEvidence.Append(normalized.AsSpan(0, Math.Min(normalized.Length, remaining)));
        }
        var retainedSemanticInputCharacters = 0;
        void RecordSemanticInput(string label, string? value)
        {
            if (!captureTrace || string.IsNullOrWhiteSpace(value)) return;
            if (semanticInputs!.Count >= 40 || retainedSemanticInputCharacters >= 4000)
            {
                capturedTrace.SemanticInputsTruncated = true;
                return;
            }
            const int maxEntryCharacters = 300;
            var remaining = 4000 - retainedSemanticInputCharacters;
            var prefix = label + ": ";
            var allowedValueLength = Math.Max(0, Math.Min(maxEntryCharacters - prefix.Length, remaining - prefix.Length));
            var boundedValue = value.Length > allowedValueLength ? value[..allowedValueLength] : value;
            semanticInputs.Add(prefix + boundedValue);
            retainedSemanticInputCharacters += prefix.Length + boundedValue.Length;
            if (boundedValue.Length < value.Length) capturedTrace.SemanticInputsTruncated = true;
        }
        if (captureTrace)
        {
            RecordSemanticInput("filename", name);
            trace.SemanticInputs = semanticInputs!;
            trace.SemanticInputCharacters = (name ?? string.Empty).Length;
        }
        XDocument? modelDocument = null;
        var low=(name ?? string.Empty).ToLowerInvariant();
        var compactName = CompactForTokenMatching(low);
        SpecialDetection? special=null;
        try
        {
            using var z=ZipFile.OpenRead(path);
            if (captureTrace)
            {
                trace.ArchiveOpened = true;
                trace.ArchiveEntryCount = z.Entries.Count;
                trace.ModelPartEntries = z.Entries
                    .Where(e => e.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.FullName).Take(20).ToArray();
            }
            var model=z.GetEntry("3D/3dmodel.model");
            var modelMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(model==null){risk.Add("Missing 3D/3dmodel.model");}
            else
            {
                if (captureTrace) trace.MainModelFound = true;
                var xmlLoadStarted = captureTrace ? Stopwatch.GetTimestamp() : 0L;
                using var st=model.Open(); var doc=XDocument.Load(st,LoadOptions.None);
                if (captureTrace) trace.MainModelXmlLoadMilliseconds += (long)Stopwatch.GetElapsedTime(xmlLoadStarted).TotalMilliseconds;
                modelDocument = doc;
                var allText=new StringBuilder();
                var semanticText=new StringBuilder();
                var vertexBounds = new VertexBounds();
                var buildItems = 0;
                // Keep two evidence streams. allText is retained for print-method and
                // special-format detection, where slicer/configuration metadata is useful.
                // Semantic classification is deliberately narrower: filename plus explicit
                // model/metadata names only. Generic XML element names, arbitrary attribute
                // values, and embedded slicer/config text are NOT semantic identity evidence.
                // Those fields routinely contain words such as "tree" or "terrain" that
                // describe the software/schema rather than the printed model.
                allText.Append(name).Append(' ');
                semanticText.Append(name).Append(' ');
                foreach(var e in doc.Descendants())
                {
                    var local=e.Name.LocalName;
                    allText.Append(' ').Append(local);
                    // XElement.Value on a container joins text from every descendant.
                    // Appending it for every nested model element repeatedly copies mesh
                    // coordinates and scales toward quadratic work on large 3MF archives.
                    // Leaf text retains slicer/metadata evidence without walking child text
                    // over and over. Vertex and triangle attributes are geometry payload,
                    // not print-method or special-format evidence.
                    if (!e.HasElements) allText.Append(' ').Append(e.Value);
                    if (!local.Equals("vertex", StringComparison.OrdinalIgnoreCase) &&
                        !local.Equals("triangle", StringComparison.OrdinalIgnoreCase))
                        foreach(var a in e.Attributes()) allText.Append(' ').Append(a.Name.LocalName).Append(' ').Append(a.Value);

                    if (local.Equals("object", StringComparison.OrdinalIgnoreCase)) objects++;
                    else if (local.Equals("vertex", StringComparison.OrdinalIgnoreCase)) vertexBounds.Add(e);
                    else if (local.Equals("item", StringComparison.OrdinalIgnoreCase)) buildItems++;
                    else if (local.Equals("base", StringComparison.OrdinalIgnoreCase))
                    {
                        var materialName = e.Attribute("name")?.Value;
                        if (!string.IsNullOrWhiteSpace(materialName)) modelMaterials.Add(materialName);
                    }
                    if (local.Equals("object", StringComparison.OrdinalIgnoreCase) ||
                        local.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                    {
                        var nameAttr=e.Attribute("name")?.Value;
                        if(!string.IsNullOrWhiteSpace(nameAttr)) { semanticText.Append(' ').Append(nameAttr); RecordSemanticInput($"{local}.name", nameAttr); if (local.Equals("object", StringComparison.OrdinalIgnoreCase)) AppendSourceEvidence(nameAttr); }
                        var partAttr=e.Attribute("partnumber")?.Value;
                        if(!string.IsNullOrWhiteSpace(partAttr)) { semanticText.Append(' ').Append(partAttr); RecordSemanticInput($"{local}.partnumber", partAttr); if (local.Equals("object", StringComparison.OrdinalIgnoreCase)) AppendSourceEvidence(partAttr); }
                        if(local.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                        {
                            var value=e.Value?.Trim();
                            if(!string.IsNullOrWhiteSpace(value) && value.Length<=500)
                            {
                                semanticText.Append(' ').Append(value); RecordSemanticInput($"metadata[{nameAttr ?? "unnamed"}]", value);
                                if (nameAttr is not null && IsCuratedSemanticMetadataName(nameAttr)) AppendSourceEvidence(value);
                            }
                        }
                    }
                }
                var metadataReadStarted = captureTrace ? Stopwatch.GetTimestamp() : 0L;
                foreach(var e in z.Entries.Where(e=>e.Length<2_000_000 && (e.FullName.EndsWith(".config",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".txt",StringComparison.OrdinalIgnoreCase))))
                {
                    try { using var es=e.Open(); using var sr=new StreamReader(es); allText.Append(' ').Append(sr.ReadToEnd()); } catch { }
                }
                if (captureTrace) trace.PackageMetadataReadMilliseconds += (long)Stopwatch.GetElapsedTime(metadataReadStarted).TotalMilliseconds;
                low=allText.ToString().ToLowerInvariant();
                var semanticLow=semanticText.ToString().ToLowerInvariant();
                special=DetectSpecialType(low, z);
                // Normalize common filename separators before semantic scoring. '+' and '_'
                // are frequently used as spaces in model repositories.
                var semanticScoringText = NormalizeSemanticSeparators(semanticLow);
                var compactSemanticScoringText = CompactForTokenMatching(semanticScoringText);
                var scoringStarted = captureTrace ? Stopwatch.GetTimestamp() : 0L;
                var scored=Signals.Select(s=>(s,score:TokenScore(semanticScoringText,s.Phrase,s.Weight,compactSemanticScoringText))).Where(x=>x.score>0).GroupBy(x=>x.s.Category).Select(g=>new {g.Key,Score=g.Sum(x=>x.score),Hits=g.OrderByDescending(x=>x.score).Take(4).ToList()}).OrderByDescending(x=>x.Score).ToList();
                if (captureTrace) trace.SemanticScoringMilliseconds += (long)Stopwatch.GetElapsedTime(scoringStarted).TotalMilliseconds;
                if (captureTrace)
                {
                    trace.SemanticInputCharacters = semanticScoringText.Length;
                    trace.SemanticInputs = semanticInputs!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    trace.TopCandidates = scored.Take(5).Select(g => new ThreeMfSignalCandidateTrace(
                        g.Key, g.Score, g.Hits.Select(h => $"{h.s.Phrase} (weight {h.s.Weight:0.##}, score {h.score:0.##})").ToArray())).ToArray();
                    trace.SelectedSignals = scored.Count > 0 && scored[0].Score >= 2.5
                        ? scored[0].Hits.Select(h => h.s.Phrase).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray()
                        : Array.Empty<string>();
                }
                if(special is not null)
                {
                    category=special.Category; family=special.Family; type=special.Type; subtype=special.Subtype; specialType=special.Type;
                    tags.Add(special.Tag);
                    reason=special.Reason;
                    evidence.Add($"{category}: {reason}");
                }
                else if(scored.Count>0 && scored[0].Score>=2.5)
                {
                    category=scored[0].Key; var best=scored[0].Hits.First(); family=best.s.Family; type=best.s.Type; subtype=best.s.Subtype;
                    foreach(var h in scored[0].Hits) tags.Add(h.s.Tag);
                    reason=string.Join(", ",scored[0].Hits.Select(h=>h.s.Phrase).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
                    evidence.Add($"{category}: {reason}");
                }
                else risk.Add("Low semantic confidence");
                if (captureTrace) { trace.ObjectCount = objects; trace.VertexCount = vertexBounds.Count; }
                var unit=doc.Root?.Attribute("unit")?.Value?.ToLowerInvariant()??"millimeter";
                dimensions=vertexBounds.GetDimensions(unit);
                if(objects>0){score+=0.20;evidence.Add($"{objects} object(s)");} else risk.Add("No mesh objects detected");
                if(vertexBounds.Count>0){score+=0.12;} else risk.Add("No vertices detected");
                if (captureTrace) trace.BuildItemCount = buildItems;
                if(buildItems>0){score+=0.08;evidence.Add($"{buildItems} build item(s)");} else risk.Add("No build items detected");
                if(!string.IsNullOrWhiteSpace(dimensions)){score+=0.08;}

                // 8.6 geometry readiness is package-aware. Many valid 3MF packages store
                // build objects as component references to other 3D/*.model parts, so a
                // direct vertex count in 3D/3dmodel.model can legitimately be zero. The
                // resolver follows Build -> Object -> Component -> referenced Object and
                // uses package relationships/candidate resolution without changing semantic
                // classification.
                if (vertexBounds.Count == 0)
                {
                    var geometryStarted = captureTrace ? Stopwatch.GetTimestamp() : 0L;
                    var geometry = geometryResolver.Resolve(path, modelDocument,
                        stopAfterFirstReachableGeometry: !exactComponentGeometry);
                    if (captureTrace)
                    {
                        trace.GeometryResolutionMilliseconds += (long)Stopwatch.GetElapsedTime(geometryStarted).TotalMilliseconds;
                        trace.ComponentGeometryCountsExact = geometry.CountsExact;
                        trace.GeometryModelParts = geometry.ModelParts;
                        trace.GeometryComponentReferences = geometry.ComponentReferences;
                        trace.GeometryUnresolvedReferences = geometry.UnresolvedReferences;
                        trace.GeometryCycles = geometry.Cycles;
                        trace.GeometryReachableVertices = geometry.ReachableVertices;
                        trace.GeometryReachableTriangles = geometry.ReachableTriangles;
                    }
                    if (geometry.BuildItems > 0 && geometry.ReachableVertices > 0)
                    {
                        ready = true;
                        risk.RemoveAll(x => string.Equals(x, "No vertices detected", StringComparison.OrdinalIgnoreCase));
                        score += 0.12;
                        evidence.Add(geometry.CountsExact
                            ? $"Component-aware geometry: {geometry.ReachableVertices:N0} reachable vertices"
                            : "Component-aware geometry: reachable mesh vertices detected");
                    }
                    else
                    {
                        ready=false;
                    }
                }
                else
                {
                    ready=objects>0 && vertexBounds.Count>0 && model.Length>0;
                }
            }
            var entries=string.Join(" ",z.Entries.Select(e=>e.FullName));
            var metadataText=string.Join(" ",z.Entries.Where(e=>e.Length<500_000).Select(e=>e.FullName));
            var slicerLow=(entries+" "+metadataText).ToLowerInvariant();
            foreach(var kv in SlicerNames) if(kv.Value.Any(slicerLow.Contains)){slicer=kv.Key;break;}
            if(slicer.Length>0){score+=0.08;evidence.Add(slicer);tags.Add(slicer.Replace(" ","-",StringComparison.Ordinal));}
            var mat=modelMaterials;
            foreach(var e in z.Entries){var n=e.FullName; if(n.Contains("filament",StringComparison.OrdinalIgnoreCase)||n.Contains("material",StringComparison.OrdinalIgnoreCase)){var stem=Path.GetFileNameWithoutExtension(e.Name); if(stem.Length>1) mat.Add(stem);}}
            materials=string.Join(", ",mat.Take(12)); if(mat.Count>0){score+=0.05;tags.Add("material-detected");}
            var pm = DetectPrintMethod(low, z, slicer, materials); printMethod=pm.Method; printMethodConfidence=pm.Confidence; printMethodEvidence=pm.Evidence; if(printMethod!="Unknown") { evidence.Add($"Print method: {printMethod} ({printMethodConfidence:P0})"); tags.Add(printMethod.ToLowerInvariant()); }
            if(ready) score+=0.12; else risk.Add("Structural review recommended");
            score=Math.Clamp(score + (category!="Uncategorized"
                ? (special is not null ? Math.Min(.30, special.Score * .025) : Math.Min(.25,Signals.Where(s=>s.Category==category).Sum(s=>TokenScore(low,s.Phrase,s.Weight,compactName))*.008))
                : 0),0,0.99);
        }
        catch(Exception ex){risk.Add(ex.GetType().Name);ready=false;if(captureTrace){trace.FailureType=ex.GetType().Name;trace.FailureMessage=ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;}}
        if(category!="Uncategorized") tags.Add(category.ToLowerInvariant().Replace(" ","-",StringComparison.Ordinal));
        if(!string.IsNullOrWhiteSpace(family)) tags.Add(family.ToLowerInvariant().Replace("/","-",StringComparison.Ordinal).Replace(" ","-",StringComparison.Ordinal));
        var suggested=string.Join(", ",tags.Where(x=>x.Length<=60).Take(12));
        if(risk.Count==0) evidence.Add("No structural risk flags");
        reason=string.Join(" | ",evidence.Take(4));
        if (captureTrace) trace.TotalElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds;
        return new IntelligenceResult(category,score,reason,family,objects,dimensions,slicer,materials,ready,type,subtype,suggested,string.Join(", ",risk.Distinct(StringComparer.OrdinalIgnoreCase)),printMethod,printMethodConfidence,printMethodEvidence,specialType)
        {
            SourceSemanticEvidence = sourceSemanticEvidence.ToString()
        };
    }

    private static bool IsCuratedSemanticMetadataName(string name)
        => name.Equals("title", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("description", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("model_name", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("partnumber", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("part_number", StringComparison.OrdinalIgnoreCase);

    private static double TokenScore(string text,string phrase,double weight,string? compactText = null)
    {
        if(string.IsNullOrWhiteSpace(text)) return 0;
        var p=phrase.ToLowerInvariant();
        var matcher=TokenRegexCache.GetOrAdd(p, static value =>
            new Regex($@"(?<![a-z0-9]){Regex.Escape(value)}(?![a-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant));
        var count=matcher.Matches(text).Count;
        // Aircraft/model designations are often stored with punctuation differences
        // (AH-64 vs AH64). Compact matching is allowed only for phrases that contain a
        // digit or hyphen. Callers scoring a signal set pass a precomputed compact string,
        // avoiding a full-text normalization for every individual signal.
        if(count==0 && (p.Any(char.IsDigit) || p.Contains('-')))
        {
            compactText ??= CompactForTokenMatching(text);
            var compactPhrase=Regex.Replace(p, @"[^a-z0-9]", "");
            if(compactPhrase.Length >= 3 && compactText.Contains(compactPhrase, StringComparison.OrdinalIgnoreCase))
                count=1;
        }
        if(count==0) return 0;
        var phraseBoost=p.Contains(' ')?1.4:1.0;
        return Math.Min(10,count) * Math.Max(.5,weight / 5.0) * phraseBoost;
    }

    private static string CompactForTokenMatching(string text)
        => Regex.Replace(text, @"[^a-z0-9]", "");

    private static string NormalizeSemanticSeparators(string value)
        => Regex.Replace(value.Replace('+', ' ').Replace('_', ' '), @"[\\/|]+", " ");

    private sealed class VertexBounds
    {
        private bool hasCoordinates;
        private double minX, minY, minZ, maxX, maxY, maxZ;
        public int Count { get; private set; }

        public void Add(XElement vertex)
        {
            Count++;
            if (!double.TryParse(vertex.Attribute("x")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(vertex.Attribute("y")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(vertex.Attribute("z")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return;

            if (!hasCoordinates)
            {
                minX = maxX = x;
                minY = maxY = y;
                minZ = maxZ = z;
                hasCoordinates = true;
                return;
            }

            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }

        public string GetDimensions(string unit)
        {
            if (!hasCoordinates) return "";
            var scale = unit switch { "meter" => 1000, "centimeter" => 10, "centimetre" => 10, "micron" => .001, "inch" => 25.4, "foot" => 304.8, _ => 1 };
            return $"{(maxX - minX) * scale:0.##} × {(maxY - minY) * scale:0.##} × {(maxZ - minZ) * scale:0.##} mm";
        }
    }
}
