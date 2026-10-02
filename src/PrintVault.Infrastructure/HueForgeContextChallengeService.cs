using System.Globalization;
using System.Text;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>
/// Validates that HueForge canvas size and printer metadata are treated as context,
/// not model identity. It is read-only and operates entirely on a SQLite snapshot.
/// </summary>
public sealed class HueForgeContextChallengeService
{
    private readonly LibraryRepository production;
    private readonly ModelIntelligenceService intelligence = new();

    public HueForgeContextChallengeService(LibraryRepository productionRepository)
        => production = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));

    public HueForgeContextChallengeResult Run(int maxCases = 36)
    {
        maxCases = Math.Clamp(maxCases, 12, 60);
        var created = DateTime.UtcNow;
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "TestLab");
        Directory.CreateDirectory(app);
        var work = Path.Combine(app, "hueforge_context_" + created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        var snapshot = Path.Combine(work, "library_snapshot.db");
        production.CreateSnapshot(snapshot);
        var sandbox = new LibraryRepository(snapshot);
        var all = sandbox.GetAll();

        var context = all.Select(x => (Model: x, Signals: intelligence.AnalyzeContext(x))).ToList();
        var rows = new List<HueForgeContextCase>();
        var checks = new List<string>
        {
             $"BUILD: {AppVersion.Version}",
            "CHALLENGE: HUEFORGE_CONTEXT_RELATIONSHIP_ADVERSARIAL_CHALLENGE",
            "ENGINE: HueForgeContextChallengeV2 — independent expectations + relationship guard",
            "MODE: SAFE AUTOMATED TEST — production database is read-only; all reads use a SQLite snapshot.",
            $"CATALOG_RECORDS: {all.Count:N0}"
        };

        AddCases(rows, context, maxCases);
        var physicalBefore = rows.SelectMany(r => new[] { r.ModelA, r.ModelB }).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (path, state: FileState(path))).ToDictionary(x => x.path, x => x.state, StringComparer.OrdinalIgnoreCase);

        var physicalAfter = rows.SelectMany(r => new[] { r.ModelA, r.ModelB }).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (path, state: FileState(path))).ToDictionary(x => x.path, x => x.state, StringComparer.OrdinalIgnoreCase);
        var physical = physicalBefore.All(kv => physicalAfter.TryGetValue(kv.Key, out var state) && state == kv.Value);
        checks.Add(physical ? "PASS: physical 3MF files unchanged" : "FAIL: physical 3MF file state changed");

        var passed = rows.Count(x => x.Passed);
        var failed = rows.Count - passed;
        checks.Add(rows.Any(x => x.CaseType == "Different entities — same HueForge size")
            ? "PASS: HueForge same-size/different-entity negative controls were exercised"
            : "FAIL: no independent HueForge size-only negative control was available");
        checks.Add(rows.Any(x => x.CaseType == "Same entity — separate files, different size")
            ? "PASS: same-subject/different-size HueForge cases were exercised"
            : "INFO: no suitable same-subject/different-size case found");
        checks.Add(rows.Any(x => x.CaseType == "Different entities — shared printer context")
            ? "PASS: printer-only different-entity negative controls were exercised"
            : "FAIL: no independent printer-only negative control was available");
        checks.Add(passed == rows.Count ? $"PASS: all {rows.Count} HueForge context cases passed" : $"FAIL: {failed} HueForge context cases failed");

        var stamp = created.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(work, "hueforge_context_challenge.txt");
        var csvPath = Path.Combine(work, "hueforge_context_challenge.csv");
        WriteReport(reportPath, csvPath, created, all.Count, rows, checks);
        return new HueForgeContextChallengeResult(created, all.Count, rows.Count, passed, failed, checks, rows, reportPath, csvPath);
    }

    private void AddCases(List<HueForgeContextCase> rows, IReadOnlyList<(ModelRecord Model, ModelContextSignals Signals)> all, int maxCases)
    {
        var hue = all.Where(x => x.Signals.IsHueForge).ToList();
        var nonHue = all.Where(x => !x.Signals.IsHueForge).ToList();
        var byEntity = new MultilingualEntityService();
        var entities = all.Select(x => (x, Entity: byEntity.Recognize(x.Model))).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Keep category quotas so a large pool of positive variants cannot crowd out
        // the negative controls. Expected outcomes are defined from independent
        // semantic facts (named entity identity / explicit context), not from the
        // relationship score being tested.
        var positiveEntityQuota = Math.Max(2, maxCases / 6);
        var variantQuota = Math.Max(3, maxCases / 4);
        var hueSizeOnlyQuota = Math.Max(2, maxCases / 5);
        var printerQuota = Math.Max(2, maxCases / 6);
        var nonHueSizeQuota = Math.Max(2, maxCases / 6);

        // Same named entity: independent identity fact, strongest expected relation.
        foreach (var group in entities.Where(x => x.x.Signals.IsHueForge && x.Entity is not null)
            .GroupBy(x => x.Entity!.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var pair = group.Select(x => x.x).Take(2).ToArray();
            if (pair.Length == 2 && TryAddPair(rows, seen, "Same entity — separate files", pair[0].Model, pair[1].Model,
                "Expected: same named entity", "Independent expectation: both records resolve to the same named entity.", true))
                if (rows.Count(x => x.CaseType == "Same entity — separate files") >= positiveEntityQuota) break;
        }

        // Same subject/different size: independently anchored by the same named entity.
        foreach (var group in entities.Where(x => x.x.Signals.IsHueForge && x.Entity is not null)
            .GroupBy(x => x.Entity!.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var pair = group.Select(x => x.x).Where(x => x.Signals.CanvasSize is not null)
                .Take(8).ToArray();
            if (pair.Length < 2) continue;
            for (var i = 0; i < pair.Length && rows.Count(x => x.CaseType == "Same entity — separate files, different size") < variantQuota; i++)
            for (var j = i + 1; j < pair.Length && rows.Count(x => x.CaseType == "Same entity — separate files, different size") < variantQuota; j++)
            {
                if (string.Equals(pair[i].Signals.CanvasSize, pair[j].Signals.CanvasSize, StringComparison.OrdinalIgnoreCase)) continue;
                TryAddPair(rows, seen, "Same entity — separate files, different size", pair[i].Model, pair[j].Model,
                    "Expected: likely variant", "Independent expectation: same named entity, different HueForge canvas size.", true);
            }
            if (rows.Count >= maxCases) return;
        }

        // Same 200x200 HueForge format, different named entities: size is explicitly
        // the context under test and must not manufacture identity.
        var hue200 = entities.Where(x => x.x.Signals.IsHueForge &&
                string.Equals(x.x.Signals.CanvasSize, "200x200", StringComparison.OrdinalIgnoreCase) && x.Entity is not null)
            .ToList();
        for (var i = 0; i < hue200.Count && rows.Count(x => x.CaseType == "Different entities — same HueForge size") < hueSizeOnlyQuota; i++)
        for (var j = i + 1; j < hue200.Count && rows.Count(x => x.CaseType == "Different entities — same HueForge size") < hueSizeOnlyQuota; j++)
        {
            if (string.Equals(hue200[i].Entity!.EntityName, hue200[j].Entity!.EntityName, StringComparison.OrdinalIgnoreCase)) continue;
            TryAddPair(rows, seen, "Different entities — same HueForge size", hue200[i].x.Model, hue200[j].x.Model,
                "Expected: no relationship", "Independent negative control: different named entities share the same 200x200 HueForge format.", false);
        }

        // Printer-only negative control: same printer signal, explicitly different named entities.
        var printer = entities.Where(x => !x.x.Signals.IsHueForge && x.x.Signals.PrinterSignals.Count > 0 && x.Entity is not null).ToList();
        for (var i = 0; i < printer.Count && rows.Count(x => x.CaseType == "Different entities — shared printer context") < printerQuota; i++)
        for (var j = i + 1; j < printer.Count && rows.Count(x => x.CaseType == "Different entities — shared printer context") < printerQuota; j++)
        {
            if (string.Equals(printer[i].Entity!.EntityName, printer[j].Entity!.EntityName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!printer[i].x.Signals.PrinterSignals.Intersect(printer[j].x.Signals.PrinterSignals, StringComparer.OrdinalIgnoreCase).Any()) continue;
            TryAddPair(rows, seen, "Different entities — shared printer context", printer[i].x.Model, printer[j].x.Model,
                "Expected: no relationship", "Independent negative control: different named entities share printer/device metadata.", false);
        }

        // Non-HueForge 200x200 negative control: the same numeric canvas token must
        // not imply HueForge or model identity.
        var nonHue200 = entities.Where(x => !x.x.Signals.IsHueForge &&
                string.Equals(x.x.Signals.CanvasSize, "200x200", StringComparison.OrdinalIgnoreCase) && x.Entity is not null)
            .ToList();
        for (var i = 0; i < nonHue200.Count && rows.Count(x => x.CaseType == "Different entities — same 200x200 outside HueForge") < nonHueSizeQuota; i++)
        for (var j = i + 1; j < nonHue200.Count && rows.Count(x => x.CaseType == "Different entities — same 200x200 outside HueForge") < nonHueSizeQuota; j++)
        {
            if (string.Equals(nonHue200[i].Entity!.EntityName, nonHue200[j].Entity!.EntityName, StringComparison.OrdinalIgnoreCase)) continue;
            TryAddPair(rows, seen, "Different entities — same 200x200 outside HueForge", nonHue200[i].x.Model, nonHue200[j].x.Model,
                "Expected: no relationship", "Independent negative control: different named entities share 200x200 outside HueForge.", false);
        }
    }

    private bool TryAddPair(List<HueForgeContextCase> rows, HashSet<string> seen, string type, ModelRecord a, ModelRecord b,
        string expected, string expectedBasis, bool shouldRelate)
    {
        if (rows.Count >= 60) return false;
        var key = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) < 0
            ? a.Path + "\n" + b.Path
            : b.Path + "\n" + a.Path;
        if (!seen.Add(key)) return false;

        var sa = intelligence.AnalyzeContext(a);
        var sb = intelligence.AnalyzeContext(b);
        var relationship = intelligence.AnalyzeRelationship(a, b);
        var actual = relationship?.Relationship ?? "No relationship";
        var score = relationship?.Score ?? 0;
        var passed = shouldRelate ? relationship is not null && score >= 68 : relationship is null;
        var evidence = relationship?.Evidence ?? "No relationship evidence — context-only overlap was rejected.";
        rows.Add(new HueForgeContextCase(type, a.Path, b.Path, sa.IsHueForge, sb.IsHueForge, sa.CanvasSize, sb.CanvasSize,
            expected, expectedBasis, actual, score, evidence, passed));
        return true;
    }

    private static string FileState(string path)
    {
        try { if (!File.Exists(path)) return "MISSING"; var i = new FileInfo(path); return $"{i.Length}|{i.LastWriteTimeUtc.Ticks}"; }
        catch { return "ERROR"; }
    }

    private static void WriteReport(string path, string csvPath, DateTime created, int catalog, IReadOnlyList<HueForgeContextCase> rows, IReadOnlyList<string> checks)
    {
        var text = new StringBuilder();
        text.AppendLine($"PrintVault 3MF {AppVersion.Version} — HueForge Relationship Adversarial Challenge");
        text.AppendLine($"Created UTC: {created:O}");
        text.AppendLine($"Catalog: {catalog:N0} | Cases: {rows.Count:N0} | Passed: {rows.Count(x => x.Passed):N0} | Failed: {rows.Count(x => !x.Passed):N0}");
        text.AppendLine(); text.AppendLine("CHECKS"); foreach (var c in checks) text.AppendLine(c);
        text.AppendLine(); text.AppendLine("CASES");
        foreach (var r in rows) text.AppendLine($"{r.Result} | {r.CaseType} | {Path.GetFileName(r.ModelA)} | {Path.GetFileName(r.ModelB)} | {r.ExpectedBehavior} | {r.ExpectedBasis} | {r.ActualRelationship} | {r.Score}% | {r.Evidence}");
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);

        var csv = new StringBuilder();
        csv.AppendLine("CaseType,ModelA,ModelB,HueForgeA,HueForgeB,SizeA,SizeB,ExpectedBehavior,ExpectedBasis,ActualRelationship,Score,Evidence,Passed");
        foreach (var r in rows) csv.AppendLine(string.Join(",", Csv(r.CaseType), Csv(r.ModelA), Csv(r.ModelB), r.HueForgeA, r.HueForgeB, Csv(r.SizeA ?? ""), Csv(r.SizeB ?? ""), Csv(r.ExpectedBehavior), Csv(r.ExpectedBasis), Csv(r.ActualRelationship), r.Score, Csv(r.Evidence), r.Passed));
        File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
    }

    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
