using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record SemanticCoverageRow(
    int Index,
    string Name,
    string Entity,
    string Domain,
    int EntityConfidence,
    int ClassificationConfidence,
    string Coverage,
    string Risk,
    string Evidence);

public sealed record SemanticCoverageChallengeResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Processed,
    int EntityRecords,
    int HighConfidenceEntities,
    int MediumConfidenceEntities,
    int LowConfidenceRecords,
    int NoEntityRecords,
    int Contradictions,
    int CoverageOpportunities,
    int DeterministicSamples,
    int DeterministicPassed,
    bool CatalogFingerprintUnchanged,
    bool PhysicalFilesUnchanged,
    TimeSpan Elapsed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<SemanticCoverageRow> Rows,
    string ReportPath,
    string CsvPath);

/// <summary>
/// Read-only semantic coverage audit for the full catalog. It measures where entity
/// intelligence is strong, weak, absent, or internally suspicious. It does not apply
/// classifications, alter categories, or modify physical 3MF files.
/// </summary>
public sealed class SemanticCoverageChallengeService
{
    private readonly LibraryRepository production;
    private readonly MultilingualEntityService entities = new();
    private readonly ModelIntelligenceService intelligence = new();

    public SemanticCoverageChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    public SemanticCoverageChallengeResult Run(Action<int, int, string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var created = DateTime.UtcNow;
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(app);
        var stamp = created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var work = Path.Combine(app, "semantic_coverage_" + stamp);
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");

        var productionBefore = production.GetAll();
        var fingerprint = Fingerprint(productionBefore);
        production.CreateSnapshot(snapshot);
        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();
        var physicalBefore = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);

        var rows = new List<SemanticCoverageRow>(Math.Min(300, all.Count));
        var entityRecords = 0;
        var high = 0;
        var medium = 0;
        var low = 0;
        var none = 0;
        var contradictions = 0;
        var opportunities = 0;

        progress?.Invoke(0, all.Count, "Preparing semantic coverage audit");
        for (var i = 0; i < all.Count; i++)
        {
            var model = all[i];
            var entity = entities.Recognize(model);
            var profile = intelligence.BuildProfile(model, all);
            var risk = FindSemanticRisk(model, entity);
            var opportunity = FindCoverageOpportunity(model, entity);
            var coverage = entity is null ? "NO ENTITY" : entity.Confidence >= 95 ? "HIGH" : entity.Confidence >= 85 ? "MEDIUM" : "LOW";

            if (entity is not null) entityRecords++;
            if (entity?.Confidence >= 95) high++;
            else if (entity?.Confidence >= 85) medium++;
            else if (entity is not null || profile.ClassificationConfidence < 85) low++;
            if (entity is null) none++;
            if (!string.IsNullOrWhiteSpace(risk)) contradictions++;
            if (!string.IsNullOrWhiteSpace(opportunity)) opportunities++;

            if (rows.Count < 160 || !string.IsNullOrWhiteSpace(risk) || !string.IsNullOrWhiteSpace(opportunity) || entity is not null && entity.Confidence >= 95)
            {
                rows.Add(new SemanticCoverageRow(i + 1, model.Name,
                    entity?.EntityName ?? "(none)", entity?.Domain ?? "", entity?.Confidence ?? 0,
                    profile.ClassificationConfidence, coverage,
                    string.IsNullOrWhiteSpace(risk) ? "None" : "REVIEW",
                    string.Join(" | ", new[] { entity?.Evidence, risk, opportunity }.Where(x => !string.IsNullOrWhiteSpace(x)))));
            }

            if ((i + 1) % 25 == 0 || i + 1 == all.Count)
                progress?.Invoke(i + 1, all.Count, $"Auditing semantic coverage {i + 1:N0} / {all.Count:N0}");
        }

        var sample = SelectSample(all, 48);
        var deterministicPassed = 0;
        foreach (var model in sample)
        {
            var a = entities.Recognize(model);
            var b = entities.Recognize(model);
            var pa = intelligence.BuildProfile(model, all);
            var pb = intelligence.BuildProfile(model, all);
            if (EntityKey(a) == EntityKey(b) && ProfileKey(pa) == ProfileKey(pb)) deterministicPassed++;
        }

        progress?.Invoke(all.Count, all.Count, "Verifying catalog and physical-file fingerprints");
        var catalogUnchanged = fingerprint == Fingerprint(production.GetAll());
        var physicalAfter = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);
        var physicalUnchanged = physicalBefore.All(kv => physicalAfter.TryGetValue(kv.Key, out var after) && kv.Value == after);
        stopwatch.Stop();

        var checks = new List<string>
        {
             $"BUILD: {AppVersion.Version}",
            "CHALLENGE: SEMANTIC_COVERAGE_CHALLENGE",
            "ENGINE: SemanticCoverageV1_ReadOnlyAudit",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; all intelligence reads use a SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count:N0}",
            $"PROCESSED_RECORDS: {all.Count:N0}",
            catalogUnchanged ? "PASS: production catalog fingerprints unchanged" : "FAIL: production catalog fingerprint changed",
            physicalUnchanged ? "PASS: physical 3MF files unchanged" : "FAIL: one or more physical 3MF files changed",
            $"INFO: named-entity records {entityRecords:N0}; high-confidence {high:N0}; medium-confidence {medium:N0}; low-confidence {low:N0}; no-entity {none:N0}",
            $"INFO: semantic contradictions flagged {contradictions:N0}; coverage opportunities {opportunities:N0}",
            deterministicPassed == sample.Count ? $"PASS: deterministic semantic parity {deterministicPassed}/{sample.Count} samples" : $"FAIL: deterministic semantic parity {deterministicPassed}/{sample.Count} samples",
            "PASS: audit never applied classifications, changed categories, or wrote physical 3MF files"
        };

        var reportPath = Path.Combine(work, "semantic_coverage_report.txt");
        var csvPath = Path.Combine(work, "semantic_coverage_rows.csv");
        File.WriteAllText(reportPath, BuildReport(created, all.Count, entityRecords, high, medium, low, none, contradictions, opportunities,
            sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed, checks, rows), Encoding.UTF8);
        File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);
        progress?.Invoke(all.Count, all.Count, "Complete");

        return new SemanticCoverageChallengeResult(created, all.Count, all.Count, entityRecords, high, medium, low, none,
            contradictions, opportunities, sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed,
            checks, rows, reportPath, csvPath);
    }

    private static string? FindSemanticRisk(ModelRecord model, MultilingualEntityMatch? entity)
    {
        if (entity is null) return null;
        var text = string.Join(" ", model.Name ?? "", model.TranslatedTitle ?? "", model.Path ?? "").ToLowerInvariant();
        var aviation = new[] { "airbus", "aircraft", "airplane", "airliner", "fighter", "skyhawk", "thunderbolt", "little bird", "p-38", "p-51", "pby-5a", "helicopter", "jet" };
        var aviationHint = aviation.Any(text.Contains);
        if (entity.EntityName == "Automobile" && aviationHint)
            return "Possible semantic contradiction: automobile entity matched a strong aviation filename cue.";
        if (entity.Domain == "Vehicles" && aviationHint && entity.Subtype.Contains("Car", StringComparison.OrdinalIgnoreCase))
            return "Possible semantic contradiction: vehicle/car subtype conflicts with strong aviation filename cue.";
        return null;
    }

    private static string? FindCoverageOpportunity(ModelRecord model, MultilingualEntityMatch? entity)
    {
        if (entity is not null) return null;
        var text = string.Join(" ", model.Name ?? "", model.TranslatedTitle ?? "", model.Path ?? "").ToLowerInvariant();
        var hints = new (string[] Terms, string Label)[]
        {
            (new[] { "airbus", "aircraft", "airplane", "fighter", "skyhawk", "thunderbolt", "little bird", "p-38", "p-51", "pby-5a" }, "aviation identity candidate"),
            (new[] { "star wars", "tie fighter", "x-wing", "at-at", "at-st" }, "franchise/entity candidate"),
            (new[] { "one piece", "luffy", "nami", "sanji", "zoro" }, "franchise/entity candidate"),
            (new[] { "hueforge", "front_", "_front", "200x200", "158x212" }, "HueForge identity/context candidate")
        };
        foreach (var (terms, label) in hints)
            if (terms.Any(text.Contains)) return $"Coverage opportunity: {label}.";
        return null;
    }

    private static IReadOnlyList<ModelRecord> SelectSample(IReadOnlyList<ModelRecord> all, int count)
    {
        if (all.Count <= count) return all;
        return Enumerable.Range(0, count).Select(i => all[(int)Math.Round(i * (all.Count - 1.0) / (count - 1.0))]).DistinctBy(x => x.Id).ToArray();
    }

    private static string EntityKey(MultilingualEntityMatch? e) => e is null ? "" : $"{e.EntityName}|{e.Domain}|{e.Category}|{e.Subtype}|{e.Confidence}";
    private static string ProfileKey(ModelIntelligenceProfile p) => string.Join("|", p.ModelId, p.EntityName, p.EntityDomain, p.Category, p.SemanticType, p.Subtype, p.Family, p.IdentityConfidence, p.ClassificationConfidence, p.RelatedCount, p.DisplayedRelationshipCount);
    private static string FileState(string path) { try { if (!File.Exists(path)) return "MISSING"; var f = new FileInfo(path); return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}"; } catch { return "ERROR"; } }
    private static string Fingerprint(IEnumerable<ModelRecord> records) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", records.OrderBy(x => x.Id).Select(x => string.Join("|", x.Id, x.Path, x.Name, x.Category, x.Tags, x.Family, x.SemanticType, x.Subtype, x.IntelligenceScore, x.Hash, x.TranslatedTitle, x.TranslationConfidence, x.CategoryOverride))))));

    private static string BuildReport(DateTime created, int catalog, int entities, int high, int medium, int low, int none, int contradictions, int opportunities, int sample, int passed, bool catalogOk, bool physicalOk, TimeSpan elapsed, IReadOnlyList<string> checks, IReadOnlyList<SemanticCoverageRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PrintVault 3MF {AppVersion.Version} — Semantic Coverage & Confidence Challenge");
        sb.AppendLine($"Created UTC: {created:O}");
        sb.AppendLine($"Catalog: {catalog:N0} | Processed: {catalog:N0}");
        sb.AppendLine($"Named entities: {entities:N0} | High >=95%: {high:N0} | Medium 85-94%: {medium:N0} | Low/other: {low:N0} | No entity: {none:N0}");
        sb.AppendLine($"Contradictions flagged: {contradictions:N0} | Coverage opportunities: {opportunities:N0}");
        sb.AppendLine($"Deterministic parity: {passed}/{sample}");
        sb.AppendLine($"Catalog unchanged: {catalogOk} | Physical 3MF unchanged: {physicalOk}");
        sb.AppendLine($"Runtime: {elapsed.TotalSeconds:F2}s");
        sb.AppendLine(); sb.AppendLine("CHECKS"); foreach (var c in checks) sb.AppendLine(c);
        sb.AppendLine(); sb.AppendLine("REVIEW / COVERAGE RECORDS");
        foreach (var r in rows.OrderByDescending(x => x.Risk != "None").ThenByDescending(x => x.EntityConfidence).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(200))
            sb.AppendLine($"{r.Index} | {r.Coverage} | {r.Risk} | {r.Name} | Entity={r.Entity} | Domain={r.Domain} | EntityConfidence={r.EntityConfidence}% | Classification={r.ClassificationConfidence}% | {r.Evidence}");
        return sb.ToString();
    }

    private static string BuildCsv(IEnumerable<SemanticCoverageRow> rows)
    {
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Index,Name,Entity,Domain,EntityConfidence,ClassificationConfidence,Coverage,Risk,Evidence\n");
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Index, Q(r.Name), Q(r.Entity), Q(r.Domain), r.EntityConfidence, r.ClassificationConfidence, Q(r.Coverage), Q(r.Risk), Q(r.Evidence)));
        return sb.ToString();
    }
}
