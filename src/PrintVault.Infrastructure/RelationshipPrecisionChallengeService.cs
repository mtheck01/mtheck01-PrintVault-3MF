using System.Diagnostics;
using System.Globalization;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record RelationshipPrecisionCase(
    string CaseType,
    string ModelA,
    string ModelB,
    string Expected,
    string Basis,
    string Actual,
    int Score,
    bool Passed,
    string Evidence)
{
    public string Result => Passed ? "PASS" : "FAIL";
    public string BasisAndEvidence => Basis + " " + Evidence;
}

public sealed record RelationshipPrecisionChallengeResult(
    DateTime CreatedUtc,
    int CatalogSize,
    int Cases,
    int Passed,
    int Failed,
    int CandidatePairs,
    int TheoreticalPairs,
    double CandidateReductionPercent,
    double AverageCandidatesPerModel,
    int MaxCandidates,
    TimeSpan Elapsed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<RelationshipPrecisionCase> Rows,
    string ReportPath,
    string CsvPath);

/// <summary>
/// Full-library precision and retrieval audit for the Model Intelligence relationship
/// engine. Expectations are based on independent semantic facts (named entity identity,
/// explicit context, and distinct physical records), not on the relationship score.
/// Production is read-only; all work uses a SQLite snapshot.
/// </summary>
public sealed class RelationshipPrecisionChallengeService
{
    private readonly LibraryRepository production;
    private readonly ModelIntelligenceService intelligence = new();
    private readonly MultilingualEntityService entities = new();

    public RelationshipPrecisionChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    public RelationshipPrecisionChallengeResult Run(int maxCases = 30)
    {
        maxCases = Math.Clamp(maxCases, 12, 60);
        var created = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(root);
        var work = Path.Combine(root, "relationship_precision_" + created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");
        production.CreateSnapshot(snapshot);
        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();

        var checks = new List<string>
        {
             $"BUILD: {AppVersion.Version}",
            "CHALLENGE: RELATIONSHIP_IDENTITY_GATE_AND_PRECISION",
            "ENGINE: RelationshipIdentityGateV1_IndexedCandidateScan",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; all reads use the SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count:N0}"
        };

        var before = all.ToDictionary(x => x.Path ?? string.Empty, x => FileState(x.Path ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var rows = BuildIndependentCases(all, maxCases);
        var passed = rows.Count(x => x.Passed);
        var failed = rows.Count - passed;

        var totalCandidates = 0L;
        var maxCandidates = 0;
        var modelsWithCandidates = 0;
        foreach (var model in all)
        {
            var count = intelligence.GetRelationshipCandidateCount(model, all);
            totalCandidates += count;
            maxCandidates = Math.Max(maxCandidates, count);
            if (count > 0) modelsWithCandidates++;
        }

        var theoretical = Math.Max(0L, (long)all.Count * Math.Max(0, all.Count - 1));
        var reduction = theoretical == 0 ? 100d : 100d * (1d - totalCandidates / (double)theoretical);
        var average = all.Count == 0 ? 0d : totalCandidates / (double)all.Count;

        var after = all.ToDictionary(x => x.Path ?? string.Empty, x => FileState(x.Path ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var physical = before.All(kv => after.TryGetValue(kv.Key, out var state) && state == kv.Value);
        checks.Add(physical ? "PASS: physical 3MF files unchanged" : "FAIL: physical 3MF file state changed");
        checks.Add(rows.Any(x => x.CaseType.StartsWith("Same entity", StringComparison.OrdinalIgnoreCase))
            ? "PASS: independent same-entity positive controls exercised"
            : "FAIL: no same-entity positive control available");
        checks.Add(rows.Any(x => x.CaseType.Contains("same HueForge size", StringComparison.OrdinalIgnoreCase))
            ? "PASS: independent same-size/different-entity negative controls exercised"
            : "INFO: no suitable same-size/different-entity pair available");
        checks.Add(rows.Any(x => x.CaseType.Contains("shared printer", StringComparison.OrdinalIgnoreCase))
            ? "PASS: independent shared-printer negative controls exercised"
            : "INFO: no suitable shared-printer negative control available");
        checks.Add(failed == 0 ? $"PASS: all {rows.Count} independent precision cases passed" : $"FAIL: {failed} independent precision cases failed");
        checks.Add($"INFO: indexed candidate pairs evaluated {totalCandidates:N0}");
        checks.Add($"INFO: theoretical directed all-pairs comparisons {theoretical:N0}");
        checks.Add($"INFO: candidate reduction {reduction:F2}%");
        checks.Add($"INFO: average candidates/model {average:F2}; max {maxCandidates:N0}; models with candidates {modelsWithCandidates:N0}");
        checks.Add("INFO: context signals (HueForge size, printer/device, orientation, variant markers) cannot establish identity by themselves");

        timer.Stop();
        checks.Add($"INFO: runtime {timer.Elapsed.TotalSeconds:F2}s");
        var reportPath = Path.Combine(work, "relationship_precision_challenge.txt");
        var csvPath = Path.Combine(work, "relationship_precision_challenge.csv");
        WriteReport(reportPath, csvPath, created, all.Count, rows, checks, totalCandidates, theoretical, reduction, average, maxCandidates, timer.Elapsed);
        return new RelationshipPrecisionChallengeResult(created, all.Count, rows.Count, passed, failed,
            checked((int)Math.Min(int.MaxValue, totalCandidates)), checked((int)Math.Min(int.MaxValue, theoretical)),
            reduction, average, maxCandidates, timer.Elapsed, checks, rows, reportPath, csvPath);
    }

    private List<RelationshipPrecisionCase> BuildIndependentCases(IReadOnlyList<ModelRecord> all, int maxCases)
    {
        var rows = new List<RelationshipPrecisionCase>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var info = all.Select(x => (Model: x, Entity: entities.Recognize(x), Context: intelligence.AnalyzeContext(x))).ToList();

        foreach (var group in info.Where(x => x.Entity is not null)
            .GroupBy(x => x.Entity!.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var pair = group.Select(x => x.Model).Take(2).ToArray();
            if (pair.Length < 2) continue;
            Add(rows, seen, "Same entity — separate files", pair[0], pair[1], "relationship", "Both physical files independently resolve to the same named entity.");
            if (rows.Count >= Math.Max(3, maxCases / 4)) break;
        }

        var hue = info.Where(x => x.Context.IsHueForge && x.Entity is not null && x.Context.CanvasSize is not null).ToList();
        for (var i = 0; i < hue.Count && rows.Count < maxCases; i++)
        for (var j = i + 1; j < hue.Count && rows.Count < maxCases; j++)
        {
            if (string.Equals(hue[i].Entity!.EntityName, hue[j].Entity!.EntityName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(hue[i].Context.CanvasSize, hue[j].Context.CanvasSize, StringComparison.OrdinalIgnoreCase)) continue;
            Add(rows, seen, "Different entities — same HueForge size", hue[i].Model, hue[j].Model, "none", "Different named entities share the same HueForge canvas size; size is context only.");
        }

        var printers = info.Where(x => x.Entity is not null && x.Context.PrinterSignals.Count > 0).ToList();
        for (var i = 0; i < printers.Count && rows.Count < maxCases; i++)
        for (var j = i + 1; j < printers.Count && rows.Count < maxCases; j++)
        {
            if (string.Equals(printers[i].Entity!.EntityName, printers[j].Entity!.EntityName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!printers[i].Context.PrinterSignals.Intersect(printers[j].Context.PrinterSignals, StringComparer.OrdinalIgnoreCase).Any()) continue;
            Add(rows, seen, "Different entities — shared printer context", printers[i].Model, printers[j].Model, "none", "Different named entities share printer/device metadata; printer context is not identity.");
        }

        // Explicit generic-token stress: pick records that share a common construction
        // word but do not resolve to the same named entity. The expected outcome is no
        // relationship unless another independent identity anchor exists.
        var genericCandidates = info.Where(x => x.Entity is null || x.Entity.Confidence < 90).ToList();
        for (var i = 0; i < genericCandidates.Count && rows.Count < maxCases; i++)
        for (var j = i + 1; j < genericCandidates.Count && rows.Count < maxCases; j++)
        {
            if (string.Equals(genericCandidates[i].Model.Path, genericCandidates[j].Model.Path, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ShareOnlyWeakVocabulary(genericCandidates[i].Context.IdentityTokens, genericCandidates[j].Context.IdentityTokens)) continue;
            Add(rows, seen, "Generic vocabulary only", genericCandidates[i].Model, genericCandidates[j].Model, "none", "Shared vocabulary does not contain a distinctive identity anchor.");
        }

        return rows.Take(maxCases).ToList();
    }

    private void Add(List<RelationshipPrecisionCase> rows, HashSet<string> seen, string type, ModelRecord a, ModelRecord b, string expected, string basis)
    {
        var key = a.Path + "\n" + b.Path;
        if (!seen.Add(key)) return;
        var actual = intelligence.AnalyzeRelationship(a, b);
        var actualName = actual?.Relationship ?? "No relationship";
        var passed = expected == "none"
            ? actual is null
            : actual is not null && (actual.Relationship == "Same entity" || actual.Relationship == "Likely variant" || actual.Relationship == "Related model");
        rows.Add(new RelationshipPrecisionCase(type, a.Name, b.Name, expected, basis, actualName, actual?.Score ?? 0, passed, actual?.Evidence ?? "No relationship returned."));
    }

    private static bool ShareOnlyWeakVocabulary(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        var shared = a.Intersect(b, StringComparer.OrdinalIgnoreCase).ToArray();
        if (shared.Length == 0) return false;
        return shared.All(x => x.Length <= 6 || x is "dragon" or "bear" or "holder" or "tower" or "print" or "model");
    }

    private static string FileState(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        }
        catch { return "MISSING"; }
    }

    private static void WriteReport(string path, string csvPath, DateTime created, int catalogSize,
        IReadOnlyList<RelationshipPrecisionCase> rows, IReadOnlyList<string> checks,
        long candidates, long theoretical, double reduction, double average, int maxCandidates, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PrintVault 3MF {AppVersion.Version} — Relationship Identity Gate & Precision Challenge");
        sb.AppendLine($"Created UTC: {created:O}");
        sb.AppendLine($"Catalog: {catalogSize:N0} | Cases: {rows.Count} | Passed: {rows.Count(x => x.Passed)} | Failed: {rows.Count(x => !x.Passed)}");
        sb.AppendLine($"Indexed candidate pairs: {candidates:N0} | Theoretical directed pairs: {theoretical:N0}");
        sb.AppendLine($"Candidate reduction: {reduction:F2}% | Average candidates/model: {average:F2} | Max: {maxCandidates:N0}");
        sb.AppendLine($"Runtime: {elapsed.TotalSeconds:F2}s");
        sb.AppendLine();
        sb.AppendLine("CHECKS");
        foreach (var check in checks) sb.AppendLine(check);
        sb.AppendLine();
        sb.AppendLine("INDEPENDENT PRECISION CASES");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            sb.AppendLine($"{i + 1:00} | {r.Result} | {r.CaseType} | {r.ModelA} | {r.ModelB} | Expected {r.Expected} | Actual {r.Actual} | {r.Score}% | {r.Basis} | {r.Evidence}");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        var csv = new StringBuilder();
        csv.AppendLine("CaseType,ModelA,ModelB,Expected,Basis,Actual,Score,Passed,Evidence");
        foreach (var r in rows)
            csv.AppendLine(string.Join(",", Csv(r.CaseType), Csv(r.ModelA), Csv(r.ModelB), Csv(r.Expected), Csv(r.Basis), Csv(r.Actual), r.Score, r.Passed, Csv(r.Evidence)));
        File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
    }

    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}

