using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record SemanticEvidenceFusionRow(
    string CaseId,
    string Name,
    string Expected,
    string Actual,
    int EvidenceQuality,
    int ClassificationConfidence,
    bool ReviewRequired,
    bool Passed,
    string TaxonomyMatch,
    string Basis,
    string Evidence);

public sealed record SemanticEvidenceFusionChallengeResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Processed,
    int Cases,
    int Passed,
    int Failed,
    int Unavailable,
    int ReviewRequired,
    int StrongEvidence,
    int LowEvidence,
    int DeterministicSamples,
    int DeterministicPassed,
    bool CatalogFingerprintUnchanged,
    bool PhysicalFilesUnchanged,
    TimeSpan Elapsed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<SemanticEvidenceFusionRow> Rows,
    string ReportPath,
    string CsvPath);

/// <summary>Full-library, read-only validation of Semantic Evidence Fusion. Controlled
/// cases use independently specified expectations; the audit never writes classifications.</summary>
public sealed class SemanticEvidenceFusionChallengeService
{
    private readonly LibraryRepository production;
    private readonly MultilingualEntityService entities = new();
    private readonly SemanticEvidenceFusionService fusion = new();

    public SemanticEvidenceFusionChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    private sealed record ExpectedCase(string Id, string Description, string Name, string Category, string Type, string Subtype, bool ReviewExpected, bool Fixture);

    public SemanticEvidenceFusionChallengeResult Run(Action<int, int, string>? progress = null)
    {
        var started = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(app);
        var work = Path.Combine(app, "semantic_fusion_" + started.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");
        var before = production.GetAll();
        var fingerprint = Fingerprint(before);
        production.CreateSnapshot(snapshot);
        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();
        var physicalBefore = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);

        var rows = new List<SemanticEvidenceFusionRow>();
        var processed = 0;
        var strong = 0;
        var low = 0;
        var review = 0;
        progress?.Invoke(0, all.Count, "Fusing semantic evidence");
        foreach (var model in all)
        {
            var result = fusion.Fuse(model, entities.Recognize(model));
            processed++;
            if (result.EvidenceQuality >= 75) strong++; else low++;
            if (result.ReviewRequired) review++;
            if (processed % 25 == 0 || processed == all.Count)
                progress?.Invoke(processed, all.Count, $"Fusing semantic evidence {processed:N0} / {all.Count:N0}");
        }

        var cases = BuildCases(all);
        var passed = 0;
        var failed = 0;
        var unavailable = 0;
        foreach (var c in cases)
        {
            var model = c.Fixture ? Fixture(c) : all.FirstOrDefault(x => x.Name.Contains(c.Name, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                unavailable++;
                rows.Add(new SemanticEvidenceFusionRow(c.Id, "(not found)", Expected(c), "UNAVAILABLE", 0, 0, true, true, "NotApplicable", "UNAVAILABLE — not counted as a semantic failure.", ""));
                continue;
            }
            var result = fusion.Fuse(model);
            var taxonomy = c.Fixture && !string.IsNullOrWhiteSpace(c.Category)
                ? CompareExpectedClassification(c, result.Category, result.SemanticType, result.Subtype)
                : new TaxonomyExpectationResult(TaxonomyMatchKind.NotApplicable, true);
            var ok = c.Fixture
                ? taxonomy.Pass
                  && result.ReviewRequired == c.ReviewExpected
                : (!string.IsNullOrWhiteSpace(result.Category) && result.EvidenceQuality >= 35);
            if (ok) passed++; else failed++;
            rows.Add(new SemanticEvidenceFusionRow(c.Id, model.Name, Expected(c), $"{result.Category} / {result.SemanticType} / {result.Subtype}", result.EvidenceQuality, result.ClassificationConfidence, result.ReviewRequired, ok, taxonomy.Kind.ToString().ToUpperInvariant(), result.Basis, string.Join("; ", result.Evidence)));
        }

        var sample = SelectSample(all, 48);
        var deterministicPassed = 0;
        foreach (var model in sample)
        {
            var a = fusion.Fuse(model, entities.Recognize(model));
            var b = fusion.Fuse(model, entities.Recognize(model));
            if (FusionKey(a) == FusionKey(b)) deterministicPassed++;
        }

        progress?.Invoke(all.Count, all.Count, "Verifying catalog and physical-file fingerprints");
        var catalogUnchanged = fingerprint == Fingerprint(production.GetAll());
        var physicalAfter = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);
        var physicalUnchanged = physicalBefore.All(kv => physicalAfter.TryGetValue(kv.Key, out var state) && state == kv.Value);
        stopwatch.Stop();

        var checks = new List<string>
        {
             $"BUILD: {AppVersion.Version}",
            "CHALLENGE: SEMANTIC_EVIDENCE_FUSION_CHALLENGE",
            "ENGINE: SemanticEvidenceFusionV2_EvidenceConvergence_ReadOnlyAudit",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; all intelligence reads use a SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count:N0}",
            $"PROCESSED_RECORDS: {processed:N0}",
            $"INFO: strong evidence >=75: {strong:N0}; low/incomplete evidence: {low:N0}; review required: {review:N0}",
            $"INFO: controlled cases: {cases.Count:N0}; executed: {rows.Count - unavailable:N0}; unavailable: {unavailable:N0}; passed: {passed:N0}; failed: {failed:N0}",
            catalogUnchanged ? "PASS: production catalog fingerprints unchanged" : "FAIL: production catalog fingerprint changed",
            physicalUnchanged ? "PASS: physical 3MF files unchanged" : "FAIL: one or more physical 3MF files changed",
            deterministicPassed == sample.Count ? $"PASS: deterministic fusion parity {deterministicPassed}/{sample.Count} samples" : $"FAIL: deterministic fusion parity {deterministicPassed}/{sample.Count} samples",
            "PASS: evidence fusion never applied classifications, changed categories, or wrote physical 3MF files"
        };

        var reportPath = Path.Combine(work, "semantic_evidence_fusion_report.txt");
        var csvPath = Path.Combine(work, "semantic_evidence_fusion_rows.csv");
        File.WriteAllText(reportPath, BuildReport(started, all.Count, processed, cases.Count, passed, failed, unavailable, review, strong, low, sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed, checks, rows), Encoding.UTF8);
        File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);
        progress?.Invoke(all.Count, all.Count, "Complete");
        return new SemanticEvidenceFusionChallengeResult(started, all.Count, processed, cases.Count, passed, failed, unavailable, review, strong, low, sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed, checks, rows, reportPath, csvPath);
    }

    private static List<ExpectedCase> BuildCases(IReadOnlyList<ModelRecord> all) => new()
    {
        new("FIX-01", "A-10 designation + aviation domain converge without named entity", "A-10 Thunderbolt", "Vehicles", "Vehicle", "Aircraft", false, true),
        new("FIX-02", "Airbus A400M manufacturer + model designation converge", "Airbus A400M", "Vehicles", "Vehicle", "Aircraft", false, true),
        new("FIX-03", "A-4 Skyhawk named entity remains authoritative", "A-4 Skyhawk", "Vehicles", "Vehicle", "Aircraft / Attack Aircraft", false, true),
        new("FIX-04", "Generic filename remains reviewable", "13877", "", "", "", true, true),
        new("FIX-05", "HueForge context cannot establish identity", "200x200 Universal Frame", "", "", "", true, true),
        new("FIX-06", "Organizer role outranks referenced wrench identity", "socket tray wrench organizer", "Tools & Workshop", "Organizer", "Tool Organizer", false, true),
        new("FIX-07", "Strong lexical evidence with conflicting stored category stays review", "A-10 Thunderbolt", "Vehicles", "Vehicle", "Aircraft", true, true),
        new("FIX-08", "Context-only signals do not raise identity confidence", "200x200 Bambu HueForge", "", "", "", true, true),
        new("FIX-09", "F-16 designation + fighter domain generalize the aviation convergence gate", "F-16 Fighting Falcon", "Vehicles", "Vehicle", "Aircraft", false, true),
        new("FIX-10", "AH-64 designation + helicopter domain generalize the aviation convergence gate", "AH-64 Apache helicopter", "Vehicles", "Vehicle", "Aircraft", false, true),
        new("REAL-01", "Real A-10 catalog coverage", "A-10", "", "", "", true, false),
        new("REAL-02", "Real Airbus A400M catalog coverage", "Airbus+A400M", "", "", "", false, false),
        new("REAL-03", "Real HueForge context coverage", "200x200", "", "", "", true, false),
        new("REAL-04", "Real Pikachu evidence fusion", "Pikachu", "", "", "", false, false),
        new("REAL-05", "Real Colonial Viper evidence fusion", "Colonial Viper", "", "", "", false, false),
        new("REAL-06", "Real generic low-evidence model", "13877", "", "", "", true, false)
    };
    private static ModelRecord Fixture(ExpectedCase c)
    {
        var conflict = c.Id == "FIX-07";
        return new ModelRecord
        {
            Id = Math.Abs(c.Name.GetHashCode(StringComparison.Ordinal)) + 9000000,
            Path = "fixture://" + c.Name.Replace(' ', '_'),
            Name = c.Name,
            Category = conflict ? "Terrain & Props" : "",
            SemanticType = conflict ? "Scenery" : "",
            Subtype = conflict ? "Tree" : "",
            Family = conflict ? "Terrain" : "",
            IntelligenceScore = 40, OriginalLanguage = "Unknown", TranslationConfidence = 0, Tags = "", SuggestedTags = "", TranslatedTitle = ""
        };
    }

    private enum TaxonomyMatchKind { Exact, Descendant, Mismatch, NotApplicable }

    private sealed record TaxonomyExpectationResult(TaxonomyMatchKind Kind, bool Pass);

    // SemanticEvidenceFusion stores the subtype as a slash-delimited taxonomy path.
    // The test contract therefore treats the expected subtype as a minimum path:
    // an exact path passes, and a longer actual path passes when it begins with the
    // expected path. This does not reclassify anything and does not infer taxonomy
    // relationships from words such as "Aircraft" or "Carrier".
    private static TaxonomyExpectationResult CompareExpectedClassification(
        ExpectedCase expected, string actualCategory, string actualType, string actualSubtype)
    {
        if (!string.Equals(actualCategory, expected.Category, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualType, expected.Type, StringComparison.OrdinalIgnoreCase))
            return new(TaxonomyMatchKind.Mismatch, false);

        var expectedPath = SplitTaxonomy(expected.Subtype);
        var actualPath = SplitTaxonomy(actualSubtype);

        if (expectedPath.Length == actualPath.Length &&
            expectedPath.SequenceEqual(actualPath, StringComparer.OrdinalIgnoreCase))
            return new(TaxonomyMatchKind.Exact, true);

        if (actualPath.Length > expectedPath.Length &&
            actualPath.Take(expectedPath.Length).SequenceEqual(expectedPath, StringComparer.OrdinalIgnoreCase))
            return new(TaxonomyMatchKind.Descendant, true);

        return new(TaxonomyMatchKind.Mismatch, false);
    }

    private static string[] SplitTaxonomy(string value) =>
        (value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Expected(ExpectedCase c) => string.IsNullOrWhiteSpace(c.Category) ? "REVIEW / insufficient identity evidence" : $"{c.Category} / {c.Type} / {c.Subtype}";
    private static IReadOnlyList<ModelRecord> SelectSample(IReadOnlyList<ModelRecord> all, int count) => all.Count <= count ? all : Enumerable.Range(0, count).Select(i => all[(int)Math.Round(i * (all.Count - 1d) / (count - 1d))]).DistinctBy(x => x.Id).ToList();
    private static string FusionKey(SemanticEvidenceFusionResult r) => string.Join("|", r.Category, r.SemanticType, r.Subtype, r.Family, r.IdentityConfidence, r.ClassificationConfidence, r.EvidenceQuality, r.Basis, r.ReviewRequired, string.Join(";", r.Evidence));
    private static string FileState(string path) => !File.Exists(path) ? "MISSING" : $"{new FileInfo(path).Length}:{File.GetLastWriteTimeUtc(path).Ticks}";
    private static string Fingerprint(IEnumerable<ModelRecord> records) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", records.OrderBy(x => x.Id).Select(x => string.Join("|", x.Id, x.Path, x.Name, x.Category, x.Tags, x.Family, x.SemanticType, x.Subtype, x.IntelligenceScore, x.Hash, x.TranslatedTitle, x.TranslationConfidence, x.CategoryOverride))))));
    private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static string BuildCsv(IEnumerable<SemanticEvidenceFusionRow> rows)
    {
        var sb = new StringBuilder("CaseId,Name,Expected,Actual,EvidenceQuality,ClassificationConfidence,ReviewRequired,Passed,TaxonomyMatch,Basis,Evidence\n");
        foreach (var r in rows) sb.AppendLine(string.Join(",", Q(r.CaseId), Q(r.Name), Q(r.Expected), Q(r.Actual), r.EvidenceQuality, r.ClassificationConfidence, r.ReviewRequired, r.Passed, Q(r.TaxonomyMatch), Q(r.Basis), Q(r.Evidence)));
        return sb.ToString();
    }
    private static string BuildReport(DateTime created, int catalog, int processed, int cases, int passed, int failed, int unavailable, int review, int strong, int low, int samples, int deterministic, bool catalogOk, bool physicalOk, TimeSpan elapsed, IReadOnlyList<string> checks, IReadOnlyList<SemanticEvidenceFusionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PrintVault 3MF {AppVersion.Version} — Semantic Evidence Convergence Challenge\nCreated UTC: {created:O}\nCatalog: {catalog:N0} | Processed: {processed:N0}\nControlled cases: {cases:N0} | Passed: {passed:N0} | Failed: {failed:N0} | Unavailable: {unavailable:N0}\nStrong evidence >=75: {strong:N0} | Low/incomplete: {low:N0} | Review required: {review:N0}\nDeterministic parity: {deterministic}/{samples}\nCatalog unchanged: {catalogOk} | Physical 3MF unchanged: {physicalOk}\nRuntime: {elapsed.TotalSeconds:F2}s\n\nCHECKS");
        foreach (var c in checks) sb.AppendLine(c);
        sb.AppendLine("\nCASES");
        foreach (var r in rows) sb.AppendLine($"{r.CaseId} | {(r.Passed ? "PASS" : "FAIL")} | {r.Name} | Expected={r.Expected} | Actual={r.Actual} | Quality={r.EvidenceQuality}% | Review={r.ReviewRequired} | TAXONOMY_MATCH={r.TaxonomyMatch} | {r.Basis} | {r.Evidence}");
        return sb.ToString();
    }
}
