namespace PrintVault.Core;

public sealed class ModelRecord
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Uncategorized";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string? ThumbnailPath { get; set; }
    public bool Favorite { get; set; }
    public string Tags { get; set; } = "";
    public string Family { get; set; } = "";
    public string Slicer { get; set; } = "";
    public string Materials { get; set; } = "";
    public int ObjectCount { get; set; }
    public string Dimensions { get; set; } = "";
    public double IntelligenceScore { get; set; }
    public bool PrintReady { get; set; }
    public string DuplicateGroup { get; set; } = "";
    public string Hash { get; set; } = "";
    public string SemanticType { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string SuggestedTags { get; set; } = "";
    public string IntelligenceReason { get; set; } = "";
    public string RiskFlags { get; set; } = "";
    public string PrintMethod { get; set; } = "Unknown";
    public double PrintMethodConfidence { get; set; }
    public string PrintMethodEvidence { get; set; } = "";
    public string SpecialType { get; set; } = "";
    public bool CategoryOverride { get; set; }
    public bool PrintMethodOverride { get; set; }
    public string OriginalLanguage { get; set; } = "Unknown";
    public string TranslatedTitle { get; set; } = "";
    public int TranslationConfidence { get; set; }
    public string TranslationEvidence { get; set; } = "";
    public string SizeText => Size < 1024 ? $"{Size:N0} B" : Size < 1024 * 1024 ? $"{Size / 1024d:0.0} KB" : $"{Size / 1024d / 1024d:0.0} MB";
    public string ModifiedText => ModifiedUtc.ToLocalTime().ToString("g");
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);
}

public static class BuiltInCategories
{
    public static readonly string[] All =
    {
        "Functional", "Terrain & Props", "Figures & Characters", "Vehicles",
        "Household", "Tools & Workshop", "Toys & Games", "Art & Decor", "HueForge", "Keychains", "Buildings", "Nature & Scenery", "Tabletop Terrain", "Props & Accessories", "Uncategorized"
    };
}

public sealed record DuplicateGroup(string Hash, IReadOnlyList<ModelRecord> Models);
public sealed record LibraryStats(int Models, int Favorites, int Duplicates, int NeedsReview, long Bytes, int MissingThumbnails);
public sealed record LibraryRebuildResult(int Models, int Reclassified, int PreservedCustomCategories, int Failed, int Discovered);
public sealed record IntelligenceResult(string Category, double Confidence, string Reason, string Family, int ObjectCount, string Dimensions, string Slicer, string Materials, bool PrintReady, string SemanticType, string Subtype, string SuggestedTags, string RiskFlags, string PrintMethod, double PrintMethodConfidence, string PrintMethodEvidence, string SpecialType);
public sealed record MoveResult(string OldPath, string NewPath, ModelRecord Model, string PreviousCategory, string PreviousTags);
