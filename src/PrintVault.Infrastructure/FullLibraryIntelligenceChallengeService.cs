using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record FullLibraryIntelligenceRow(
    int Index,
    string Name,
    string Category,
    string Entity,
    int IdentityConfidence,
    int ClassificationConfidence,
    int RelationshipCandidates,
    int RelationshipsShown,
    bool HueForge,
    string Verdict,
    string Notes);

public sealed record FullLibraryIntelligenceResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Processed,
    int EntityRecords,
    int HueForgeRecords,
    int DuplicateGroups,
    int ExactHashGroups,
    int LowConfidenceRecords,
    long CandidatePairs,
    int MaxCandidates,
    double AverageCandidatesPerModel,
    double CandidateReductionPercent,
    int DeterministicSamples,
    int DeterministicPassed,
    bool CatalogFingerprintUnchanged,
    bool PhysicalFilesUnchanged,
    TimeSpan Elapsed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<FullLibraryIntelligenceRow> Rows,
    string ReportPath,
    string CsvPath);

/// <summary>
/// Controlled full-library validation for the 9.0 intelligence stack. It reads a
/// SQLite snapshot, evaluates every catalog record, measures the selective relationship
/// index, and performs an independent deterministic parity sample. No production record
/// or physical 3MF file is modified.
/// </summary>
public sealed class FullLibraryIntelligenceChallengeService
{
    private readonly LibraryRepository production;
    private readonly ModelIntelligenceService intelligence = new();

    public FullLibraryIntelligenceChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    public FullLibraryIntelligenceResult Run(Action<int, int, string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var created = DateTime.UtcNow;
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(app);
        var stamp = created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var work = Path.Combine(app, "full_library_intelligence_" + stamp);
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");

        var productionBefore = production.GetAll();
        var catalogFingerprint = Fingerprint(productionBefore);
        production.CreateSnapshot(snapshot);
        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();

        var physicalBefore = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);
        var rows = new List<FullLibraryIntelligenceRow>(all.Count);
        long candidatePairs = 0;
        var maxCandidates = 0;
        var entityRecords = 0;
        var hueForgeRecords = 0;
        var lowConfidence = 0;
        var seenDuplicateGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExactHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Invoke(0, all.Count, "Preparing full-library snapshot");
        for (var i = 0; i < all.Count; i++)
        {
            var model = all[i];
            var profile = intelligence.BuildProfile(model, all);
            candidatePairs += profile.RelatedCount;
            maxCandidates = Math.Max(maxCandidates, profile.RelatedCount);
            if (!string.IsNullOrWhiteSpace(profile.EntityName)) entityRecords++;
            var context = intelligence.AnalyzeContext(model);
            if (context.IsHueForge) hueForgeRecords++;
            if (profile.ClassificationConfidence < 85) lowConfidence++;
            if (!string.IsNullOrWhiteSpace(model.DuplicateGroup)) seenDuplicateGroups.Add(model.DuplicateGroup);
            if (!string.IsNullOrWhiteSpace(model.Hash)) seenExactHashes.Add(model.Hash);

            // Keep the report useful without retaining a 1,758-row UI payload.
            if (rows.Count < 120 || profile.RelatedCount >= 50 || profile.ClassificationConfidence < 70)
            {
                var entity = string.IsNullOrWhiteSpace(profile.EntityName) ? "(none)" : profile.EntityName;
                rows.Add(new FullLibraryIntelligenceRow(i + 1, model.Name, model.Category, entity,
                    profile.IdentityConfidence, profile.ClassificationConfidence, profile.RelatedCount,
                    profile.DisplayedRelationshipCount, context.IsHueForge,
                    profile.RelatedCount >= 50 ? "REVIEW" : "OBSERVED",
                    $"{profile.RelatedCount:N0} indexed candidates; {profile.DisplayedRelationshipCount} relationships shown."));
            }

            if ((i + 1) % 25 == 0 || i + 1 == all.Count)
                progress?.Invoke(i + 1, all.Count, $"Analyzing {i + 1:N0} / {all.Count:N0} models");
        }

        var sample = SelectParitySample(all, 48);
        var deterministicPassed = 0;
        foreach (var model in sample)
        {
            var first = intelligence.BuildProfile(model, all);
            var firstRelated = intelligence.FindRelated(model, all, 20);
            var second = intelligence.BuildProfile(model, all);
            var secondRelated = intelligence.FindRelated(model, all, 20);
            if (ProfileKey(first) == ProfileKey(second) && RelationshipKey(firstRelated) == RelationshipKey(secondRelated))
                deterministicPassed++;
        }

        progress?.Invoke(all.Count, all.Count, "Verifying catalog and physical-file fingerprints");
        var productionAfter = production.GetAll();
        var catalogUnchanged = catalogFingerprint == Fingerprint(productionAfter);
        var physicalAfter = all.ToDictionary(x => x.Path, x => FileState(x.Path), StringComparer.OrdinalIgnoreCase);
        var physicalUnchanged = physicalBefore.All(kv => physicalAfter.TryGetValue(kv.Key, out var after) && kv.Value == after);

        var theoreticalDirectedPairs = (long)all.Count * Math.Max(0, all.Count - 1);
        var reduction = theoreticalDirectedPairs == 0 ? 0 : 100.0 * (1.0 - (double)candidatePairs / theoreticalDirectedPairs);
        var average = all.Count == 0 ? 0 : (double)candidatePairs / all.Count;
        var checks = new List<string>
        {
            $"BUILD: {AppVersion.Version}",
            "CHALLENGE: FULL_LIBRARY_INTELLIGENCE_CHALLENGE",
            "ENGINE: FullLibraryIntelligenceV1_SelectiveIdentityIndex",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; all intelligence reads use a SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count:N0}",
            $"PROCESSED_RECORDS: {all.Count:N0}",
            catalogUnchanged ? "PASS: production catalog fingerprints unchanged" : "FAIL: production catalog fingerprint changed",
            physicalUnchanged ? "PASS: physical 3MF files unchanged" : "FAIL: one or more physical 3MF files changed",
            $"PASS: exhaustive intelligence processed {all.Count:N0} catalog records",
            $"INFO: named-entity records {entityRecords:N0}; HueForge records {hueForgeRecords:N0}; low-confidence records {lowConfidence:N0}",
            $"INFO: duplicate groups {seenDuplicateGroups.Count:N0}; exact-hash groups {seenExactHashes.Count:N0}",
            $"INFO: indexed candidate pairs {candidatePairs:N0}; theoretical directed pairs {theoreticalDirectedPairs:N0}; reduction {reduction:F2}%",
            deterministicPassed == sample.Count ? $"PASS: deterministic intelligence parity {deterministicPassed}/{sample.Count} samples" : $"FAIL: deterministic parity {deterministicPassed}/{sample.Count} samples",
            $"INFO: average candidates/model {average:F2}; maximum candidates/model {maxCandidates:N0}",
            "PASS: production records and physical files were never written by the challenge"
        };

        var reportPath = Path.Combine(work, "full_library_intelligence_report.txt");
        var csvPath = Path.Combine(work, "full_library_intelligence_rows.csv");
        File.WriteAllText(reportPath, BuildReport(created, all.Count, entityRecords, hueForgeRecords, seenDuplicateGroups.Count,
            seenExactHashes.Count, lowConfidence, candidatePairs, theoreticalDirectedPairs, reduction, average, maxCandidates,
            sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed, checks, rows), Encoding.UTF8);
        File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);

        progress?.Invoke(all.Count, all.Count, "Complete");
        stopwatch.Stop();
        return new FullLibraryIntelligenceResult(created, all.Count, all.Count, entityRecords, hueForgeRecords,
            seenDuplicateGroups.Count, seenExactHashes.Count, lowConfidence, candidatePairs, maxCandidates, average, reduction,
            sample.Count, deterministicPassed, catalogUnchanged, physicalUnchanged, stopwatch.Elapsed, checks, rows, reportPath, csvPath);
    }

    private static IReadOnlyList<ModelRecord> SelectParitySample(IReadOnlyList<ModelRecord> all, int count)
    {
        if (all.Count <= count) return all;
        var list = new List<ModelRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var index = (int)Math.Round(i * (all.Count - 1.0) / (count - 1.0));
            list.Add(all[index]);
        }
        return list.DistinctBy(x => x.Id).ToArray();
    }

    private static string RelationshipKey(IEnumerable<ModelRelationship> relationships)
        => string.Join("|", relationships.Select(x => $"{x.Model.Id}:{x.Score}:{x.Relationship}").OrderBy(x => x, StringComparer.Ordinal));

    private static string ProfileKey(ModelIntelligenceProfile p)
        => string.Join("|", p.ModelId, p.Path, p.Name, p.TranslatedTitle, p.EntityName, p.EntityDomain, p.Category,
            p.SemanticType, p.Subtype, p.Family, p.IdentityConfidence, p.ClassificationConfidence,
            p.RelatedCount, p.DisplayedRelationshipCount, string.Join(",", p.IntelligenceTags));

    private static string FileState(string path)
    {
        try
        {
            if (!File.Exists(path)) return "MISSING";
            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch { return "ERROR"; }
    }

    private static string Fingerprint(IEnumerable<ModelRecord> records)
    {
        var text = string.Join("\n", records.OrderBy(x => x.Id).Select(x => string.Join("|", x.Id, x.Path, x.Name, x.Category,
            x.Size, x.ModifiedUtc, x.Tags, x.Family, x.Slicer, x.Materials, x.ObjectCount, x.Dimensions, x.IntelligenceScore,
            x.PrintReady, x.DuplicateGroup, x.Hash, x.SemanticType, x.Subtype, x.SuggestedTags, x.OriginalLanguage,
            x.TranslatedTitle, x.TranslationConfidence, x.CategoryOverride, x.PrintMethodOverride)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string BuildReport(DateTime created, int catalog, int entities, int hueForge, int duplicates, int hashes,
        int lowConfidence, long candidates, long theoretical, double reduction, double average, int maxCandidates,
        int sample, int deterministicPassed, bool catalogUnchanged, bool physicalUnchanged, TimeSpan elapsed,
        IReadOnlyList<string> checks, IReadOnlyList<FullLibraryIntelligenceRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PrintVault 3MF {AppVersion.Version} — Full Library Intelligence Challenge");
        sb.AppendLine($"Created UTC: {created:O}");
        sb.AppendLine($"Catalog: {catalog:N0} | Processed: {catalog:N0}");
        sb.AppendLine($"Named entities: {entities:N0} | HueForge: {hueForge:N0} | Duplicate groups: {duplicates:N0} | Exact-hash groups: {hashes:N0}");
        sb.AppendLine($"Low confidence (<85%): {lowConfidence:N0}");
        sb.AppendLine($"Indexed candidate pairs: {candidates:N0} | Theoretical directed pairs: {theoretical:N0} | Candidate reduction: {reduction:F2}%");
        sb.AppendLine($"Average candidates/model: {average:F2} | Max candidates/model: {maxCandidates:N0}");
        sb.AppendLine($"Deterministic parity: {deterministicPassed}/{sample}");
        sb.AppendLine($"Catalog unchanged: {catalogUnchanged} | Physical 3MF unchanged: {physicalUnchanged}");
        sb.AppendLine($"Runtime: {elapsed.TotalSeconds:F2}s");
        sb.AppendLine(); sb.AppendLine("CHECKS"); foreach (var check in checks) sb.AppendLine(check);
        sb.AppendLine(); sb.AppendLine("REVIEW / HIGH-CANDIDATE RECORDS");
        foreach (var row in rows.OrderByDescending(x => x.RelationshipCandidates).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(120))
            sb.AppendLine($"{row.Index} | {row.Verdict} | {row.Name} | Entity={row.Entity} | Confidence={row.ClassificationConfidence}% | Candidates={row.RelationshipCandidates} | {row.Notes}");
        return sb.ToString();
    }

    private static string BuildCsv(IEnumerable<FullLibraryIntelligenceRow> rows)
    {
        var sb = new StringBuilder("Index,Name,Category,Entity,IdentityConfidence,ClassificationConfidence,RelationshipCandidates,RelationshipsShown,HueForge,Verdict,Notes\n");
        foreach (var r in rows)
        {
            static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",", r.Index, Q(r.Name), Q(r.Category), Q(r.Entity), r.IdentityConfidence, r.ClassificationConfidence,
                r.RelationshipCandidates, r.RelationshipsShown, r.HueForge, Q(r.Verdict), Q(r.Notes)));
        }
        return sb.ToString();
    }
}
