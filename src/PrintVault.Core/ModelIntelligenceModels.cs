namespace PrintVault.Core;

public sealed record ModelIntelligenceProfile(
    long ModelId,
    string Path,
    string Name,
    string TranslatedTitle,
    string EntityName,
    string EntityDomain,
    string Category,
    string SemanticType,
    string Subtype,
    string Family,
    int IdentityConfidence,
    int ClassificationConfidence,
    string[] IntelligenceTags,
    string Evidence,
    int RelatedCount,
    int DisplayedRelationshipCount,
    bool PossibleDuplicate);

public sealed record ModelRelationship(
    ModelRecord Model,
    int Score,
    string Relationship,
    string Evidence);

public sealed record HueForgeContextCase(
    string CaseType,
    string ModelA,
    string ModelB,
    bool HueForgeA,
    bool HueForgeB,
    string? SizeA,
    string? SizeB,
    string ExpectedBehavior,
    string ExpectedBasis,
    string ActualRelationship,
    int Score,
    string Evidence,
    bool Passed)
{
    public string Result => Passed ? "PASS" : "FAIL";
}

public sealed record HueForgeContextChallengeResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Cases,
    int Passed,
    int Failed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<HueForgeContextCase> Rows,
    string ReportPath,
    string CsvPath);
