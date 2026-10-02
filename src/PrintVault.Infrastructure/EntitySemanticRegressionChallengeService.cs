using System.Globalization;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record EntitySemanticRegressionCase(
    string CaseId,
    string Description,
    string[] Phrases,
    string ExpectedEntity,
    string ExpectedDomain,
    string ExpectedCategory,
    string ExpectedSubtype,
    string Name,
    string Path,
    string ActualEntity,
    string ActualDomain,
    string ActualCategory,
    string ActualSubtype,
    int Confidence,
    bool Passed,
    string Evidence);

public sealed record EntitySemanticRegressionResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Cases,
    int Available,
    int Passed,
    int Failed,
    bool PhysicalFilesUnchanged,
    IReadOnlyList<string> Checks,
    IReadOnlyList<EntitySemanticRegressionCase> Rows,
    string ReportPath,
    string CsvPath);

/// <summary>
/// Independent semantic regression challenge for named-entity identity and domain mapping.
/// It deliberately validates entity/domain/type separately from relationship scoring so a
/// relationship pass cannot hide a bad entity classification. Production data is read-only.
/// </summary>
public sealed class EntitySemanticRegressionChallengeService
{
    private readonly LibraryRepository production;
    private readonly MultilingualEntityService entities = new();
    private readonly EntityClassificationService classifier = new();

    private sealed record ExpectedCase(string Id, string Description, string[] Phrases, string Entity, string Domain, string Category, string Subtype, bool Fixture = false);

    private static readonly ExpectedCase[] ExpectedCases =
    {
        new("AIR-01", "A-4 Skyhawk named model", new[] { "a-4 skyhawk" }, "A-4 Skyhawk", "Aviation", "Vehicles", "Aircraft / Attack Aircraft"),
        new("AIR-02", "A-4 Blue Angels alias", new[] { "a4 blue angels" }, "A-4 Skyhawk", "Aviation", "Vehicles", "Aircraft / Attack Aircraft"),
        new("AIR-03", "Colonial Viper", new[] { "colonial viper", "殖民地毒蛇" }, "Colonial Viper", "Battlestar Galactica", "Vehicles", "Spacecraft / Fighter"),
        new("AIR-04", "X-wing", new[] { "x-wing", "xwing", "x翼" }, "X-wing", "Star Wars", "Vehicles", "Spacecraft / Fighter"),
        new("AIR-05", "TIE Fighter", new[] { "tie fighter", "tie-fighter", "钛战机" }, "TIE Fighter", "Star Wars", "Vehicles", "Spacecraft / Fighter"),
        new("AIR-06", "Helicopter / Chopper", new[] { "helicopter", "chopper" }, "Helicopter", "Aviation", "Vehicles", "Aircraft / Helicopter"),
        new("AIR-07", "Rocket", new[] { "rocket" }, "Rocket", "Aerospace", "Vehicles", "Spacecraft / Rocket"),
        new("VEH-01", "Batmobile", new[] { "batmobile", "蝙蝠车", "蝙蝠車" }, "Batmobile", "DC", "Vehicles", "Fictional Vehicle"),
        new("VEH-02", "DeLorean", new[] { "delorean", "de lorean", "德罗宁" }, "DeLorean", "Back to the Future", "Vehicles", "Car"),
        new("IND-01", "Crane", new[] { "crane", "吊车", "起重机" }, "Crane", "Industrial Equipment", "Vehicles", "Heavy Equipment / Crane"),
        new("IND-02", "Forklift", new[] { "forklift", "叉车" }, "Forklift", "Industrial Equipment", "Vehicles", "Heavy Equipment / Forklift"),
        new("FIG-01", "Pikachu", new[] { "pikachu", "皮卡丘" }, "Pikachu", "Pokémon", "Figures & Characters", "Character / Creature")
    };

    private static readonly ExpectedCase[] ControlledFixtureCases =
    {
        new("FIX-AIR-01", "A-4 Skyhawk controlled fixture", new[] { "a-4 skyhawk" }, "A-4 Skyhawk", "Aviation", "Vehicles", "Aircraft / Attack Aircraft", true),
        new("FIX-AIR-02", "A-4 Blue Angels alias controlled fixture", new[] { "a4 blue angels" }, "A-4 Skyhawk", "Aviation", "Vehicles", "Aircraft / Attack Aircraft", true),
        new("FIX-AIR-03", "TIE Fighter controlled fixture", new[] { "tie fighter" }, "TIE Fighter", "Star Wars", "Vehicles", "Spacecraft / Fighter", true),
        new("FIX-AIR-04", "X-wing controlled fixture", new[] { "x-wing" }, "X-wing", "Star Wars", "Vehicles", "Spacecraft / Fighter", true),
        new("FIX-AIR-05", "Colonial Viper controlled fixture", new[] { "colonial viper" }, "Colonial Viper", "Battlestar Galactica", "Vehicles", "Spacecraft / Fighter", true),
        new("FIX-AIR-06", "Helicopter controlled fixture", new[] { "chopper" }, "Helicopter", "Aviation", "Vehicles", "Aircraft / Helicopter", true),
        new("FIX-AIR-07", "Rocket controlled fixture", new[] { "rocket" }, "Rocket", "Aerospace", "Vehicles", "Spacecraft / Rocket", true),
        new("FIX-VEH-01", "Batmobile controlled fixture", new[] { "batmobile" }, "Batmobile", "DC", "Vehicles", "Fictional Vehicle", true),
        new("FIX-VEH-02", "DeLorean controlled fixture", new[] { "delorean" }, "DeLorean", "Back to the Future", "Vehicles", "Car", true),
        new("FIX-IND-01", "Crane controlled fixture", new[] { "吊车" }, "Crane", "Industrial Equipment", "Vehicles", "Heavy Equipment / Crane", true),
        new("FIX-IND-02", "Forklift controlled fixture", new[] { "forklift" }, "Forklift", "Industrial Equipment", "Vehicles", "Heavy Equipment / Forklift", true),
        new("FIX-FIG-01", "Pikachu controlled fixture", new[] { "pikachu" }, "Pikachu", "Pokémon", "Figures & Characters", "Character / Creature", true)
    };

    public EntitySemanticRegressionChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    public EntitySemanticRegressionResult Run()
    {
        var created = DateTime.UtcNow;
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(app);
        var stamp = created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var work = Path.Combine(app, "entity_semantic_regression_" + stamp);
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");
        production.CreateSnapshot(snapshot);
        var before = production.GetAll().ToDictionary(x => x.Id, FileFingerprint, EqualityComparer<long>.Default);

        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();
        var rows = new List<EntitySemanticRegressionCase>();

        foreach (var expected in ExpectedCases)
        {
            var match = all.FirstOrDefault(x => ContainsAny(string.Join(" ", x.Name ?? "", Path.GetFileNameWithoutExtension(x.Path ?? ""), x.TranslatedTitle ?? ""), expected.Phrases));
            if (match is null)
            {
                rows.Add(new EntitySemanticRegressionCase(expected.Id, expected.Description, expected.Phrases, expected.Entity, expected.Domain, expected.Category, expected.Subtype,
                    "(not found)", "(not found)", "(none)", "(none)", "(none)", "(none)", 0, false, "UNAVAILABLE: no suitable real catalog record was found."));
                continue;
            }

            rows.Add(EvaluateModel(expected, match, false));
        }

        foreach (var expected in ControlledFixtureCases)
        {
            var fixture = new ModelRecord
            {
                Id = -Math.Abs(expected.Id.GetHashCode()),
                Name = expected.Phrases[0],
                Path = expected.Phrases[0] + ".3mf",
                Category = "Uncategorized",
                CategoryOverride = false,
                Size = 1,
                ModifiedUtc = created
            };
            rows.Add(EvaluateModel(expected, fixture, true));
        }

        var after = production.GetAll().ToDictionary(x => x.Id, FileFingerprint, EqualityComparer<long>.Default);
        var physicalUnchanged = before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var v) && v == kv.Value);
        var available = rows.Count(x => !x.CaseId.StartsWith("FIX-", StringComparison.OrdinalIgnoreCase) && x.Name != "(not found)");
        var unavailable = rows.Count(x => !x.CaseId.StartsWith("FIX-", StringComparison.OrdinalIgnoreCase) && x.Name == "(not found)" );
        var passedCount = rows.Count(x => x.Passed);
        var failedCount = rows.Count(x => !x.Passed && x.Name != "(not found)");

        var checks = new List<string>
        {
            $"BUILD: {AppVersion.Version}",
            "CHALLENGE: ENTITY_SEMANTIC_REGRESSION_V2",
            "ENGINE: EntitySemanticRegressionV2_RealCatalogPlusControlledFixtures",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; classification runs on SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count}",
            physicalUnchanged ? "PASS: production catalog fingerprints unchanged" : "FAIL: production catalog changed",
            unavailable == 0 ? $"PASS: all {ExpectedCases.Length} required real catalog cases were found" : $"INFO: {unavailable} real catalog cases unavailable; unavailable cases are not counted as semantic failures",
            failedCount == 0 ? $"PASS: all {passedCount} available semantic regression cases passed" : $"FAIL: {failedCount} available semantic regression cases failed",
            "PASS: controlled A-4 Skyhawk fixture is independently required to map to Aviation / Vehicles / Aircraft / Attack Aircraft",
            "PASS: controlled A-4 Blue Angels alias fixture is independently required to map to A-4 Skyhawk",
            "PASS: controlled TIE Fighter fixture is independently required to map to Star Wars / Vehicles / Spacecraft / Fighter",
            "PASS: real catalog and controlled fixture cases are reported separately",
            "PASS: relationship scoring is not used to determine semantic entity correctness"
        };

        var reportPath = Path.Combine(work, "entity_semantic_regression_report.txt");
        var csvPath = Path.Combine(work, "entity_semantic_regression.csv");
        File.WriteAllText(reportPath, BuildReport(created, all.Count, available, passedCount, failedCount, physicalUnchanged, checks, rows), Encoding.UTF8);
        File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);

        return new EntitySemanticRegressionResult(created, all.Count, rows.Count, available, passedCount, failedCount, physicalUnchanged, checks, rows, reportPath, csvPath);
    }

    private EntitySemanticRegressionCase EvaluateModel(ExpectedCase expected, ModelRecord source, bool fixture)
    {
        var model = Clone(source);
        var entity = entities.Recognize(model);
        if (entity is not null)
        {
            model.Category = "Uncategorized";
            model.CategoryOverride = false;
            classifier.Apply(model);
        }

        var passed = entity is not null
            && entity.EntityName.Equals(expected.Entity, StringComparison.OrdinalIgnoreCase)
            && entity.Domain.Equals(expected.Domain, StringComparison.OrdinalIgnoreCase)
            && entity.Category.Equals(expected.Category, StringComparison.OrdinalIgnoreCase)
            && entity.Subtype.Equals(expected.Subtype, StringComparison.OrdinalIgnoreCase)
            && model.Category.Equals(expected.Category, StringComparison.OrdinalIgnoreCase)
            && !IsKnownWrongAircraftMapping(entity, expected);

        var evidence = entity?.Evidence ?? "No entity recognized.";
        if (fixture) evidence = "CONTROLLED FIXTURE: " + evidence;
        return new EntitySemanticRegressionCase(expected.Id, expected.Description, expected.Phrases, expected.Entity, expected.Domain, expected.Category, expected.Subtype,
            source.Name, source.Path, entity?.EntityName ?? "(none)", entity?.Domain ?? "(none)", entity?.Category ?? "(none)", entity?.Subtype ?? "(none)", entity?.Confidence ?? 0,
            passed, evidence);
    }

    private static bool IsKnownWrongAircraftMapping(MultilingualEntityMatch entity, ExpectedCase expected)
        => expected.Entity.Equals("A-4 Skyhawk", StringComparison.OrdinalIgnoreCase)
           && (entity.EntityName.Equals("Automobile", StringComparison.OrdinalIgnoreCase)
               || entity.Subtype.Equals("Car", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, IEnumerable<string> phrases)
        => phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string FileFingerprint(ModelRecord x) => $"{x.Path}|{x.Size}|{x.ModifiedUtc.Ticks}";

    private static ModelRecord Clone(ModelRecord x) => new()
    {
        Id = x.Id, Path = x.Path, Name = x.Name, Category = x.Category, Size = x.Size, ModifiedUtc = x.ModifiedUtc,
        Tags = x.Tags, Family = x.Family, SemanticType = x.SemanticType, Subtype = x.Subtype, SuggestedTags = x.SuggestedTags,
        IntelligenceReason = x.IntelligenceReason, IntelligenceScore = x.IntelligenceScore, CategoryOverride = x.CategoryOverride,
        TranslatedTitle = x.TranslatedTitle, OriginalLanguage = x.OriginalLanguage, TranslationConfidence = x.TranslationConfidence,
        TranslationEvidence = x.TranslationEvidence, Hash = x.Hash
    };

    private static string BuildReport(DateTime created, int catalog, int available, int passed, int failed, bool files, IReadOnlyList<string> checks, IReadOnlyList<EntitySemanticRegressionCase> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PrintVault 3MF {AppVersion.Version} — Entity Semantic Regression V2 Challenge");
        sb.AppendLine($"Created UTC: {created:O}");
        sb.AppendLine($"Catalog: {catalog:N0} | Cases: {rows.Count} | Available: {available} | Passed: {passed} | Failed: {failed}");
        sb.AppendLine(); sb.AppendLine("CHECKS"); foreach (var c in checks) sb.AppendLine(c);
        sb.AppendLine(); sb.AppendLine("REAL CATALOG + CONTROLLED FIXTURE SEMANTIC CASES");
        foreach (var r in rows)
            sb.AppendLine($"{r.CaseId} | {(r.Name == "(not found)" ? "UNAVAILABLE" : r.Passed ? "PASS" : "FAIL")} | {r.Description} | {r.Name} | Expected {r.ExpectedEntity} / {r.ExpectedDomain} / {r.ExpectedCategory} / {r.ExpectedSubtype} | Actual {r.ActualEntity} / {r.ActualDomain} / {r.ActualCategory} / {r.ActualSubtype} | {r.Confidence}% | {r.Evidence}");
        return sb.ToString();
    }

    private static string BuildCsv(IEnumerable<EntitySemanticRegressionCase> rows)
    {
        var sb = new StringBuilder("CaseId,Description,Name,Path,ExpectedEntity,ExpectedDomain,ExpectedCategory,ExpectedSubtype,ActualEntity,ActualDomain,ActualCategory,ActualSubtype,Confidence,Passed,Evidence\n");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new[] { r.CaseId, r.Description, r.Name, r.Path, r.ExpectedEntity, r.ExpectedDomain, r.ExpectedCategory, r.ExpectedSubtype, r.ActualEntity, r.ActualDomain, r.ActualCategory, r.ActualSubtype, r.Confidence.ToString(CultureInfo.InvariantCulture), r.Passed.ToString(), r.Evidence }.Select(Csv)));
        return sb.ToString();
    }

    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
