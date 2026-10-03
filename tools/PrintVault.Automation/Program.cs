using System.Text.Json;
using PrintVault.Core;
using PrintVault.Infrastructure;

if (args.Length == 0 || !string.Equals(args[0], "rootcause", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "repair", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: PrintVault.Automation rootcause [outputDirectory] [expectedCatalog]");
    Console.Error.WriteLine("   or: PrintVault.Automation repair");
    return 2;
}

var command = args[0];
var outputDirectory = args.Length > 1 && !string.Equals(command, "repair", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[1])
    ? Path.GetFullPath(args[1])
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "reports");

var expectedCatalog = args.Length > 2 && int.TryParse(args[2], out var parsed)
    ? parsed
    : 1758;

bool IsBuiltInOrUnresolved(string? category) =>
    string.IsNullOrWhiteSpace(category) ||
    string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(category, "Unknown", StringComparison.OrdinalIgnoreCase) ||
    BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

try
{
    var repository = new LibraryRepository();
    Console.WriteLine($"DATABASE={repository.DatabasePath}");

    if (string.Equals(command, "repair", StringComparison.OrdinalIgnoreCase))
    {
        var models = repository.GetAll();
        if (models.Count == 0)
        {
            Console.Error.WriteLine("REPAIR_ABORTED=NO_LIBRARY_RECORDS");
            return 12;
        }

        var reportPath = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? Path.GetFullPath(args[1])
            : "";
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            Console.Error.WriteLine("REPAIR_ABORTED=REPORT_REQUIRED");
            return 13;
        }

        var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var rows = report.RootElement.GetProperty("rows");
        var byId = models.ToDictionary(m => m.Id);
        var changed = 0;
        var unresolvedLexical = 0;
        var resolutionCandidates = 0;
        var unresolvedRoleCandidates = 0;
        var skippedProtected = 0;

        var backup = repository.BackupDatabase();
        Console.WriteLine($"REPAIR_BACKUP={backup}");
        Console.WriteLine($"REPAIR_INPUT_CATALOG={models.Count}");
        Console.WriteLine("REPAIR_MODE=REPORT_DRIVEN_DETERMINISTIC_WRITEBACK");
        Console.WriteLine($"REPAIR_REPORT={reportPath}");

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("Id", out var idNode) || !idNode.TryGetInt32(out var id) || !byId.TryGetValue(id, out var model))
                continue;

            var inferred = row.TryGetProperty("InferredCategory", out var inferredNode) ? inferredNode.GetString() ?? "" : "";
            var stored = row.TryGetProperty("StoredCategory", out var storedNode) ? storedNode.GetString() ?? "" : "";
            var disposition = row.TryGetProperty("Disposition", out var dispositionNode) ? dispositionNode.GetString() ?? "" : "";
            var basis = row.TryGetProperty("Basis", out var basisNode) ? basisNode.GetString() ?? "" : "";
            var evidence = row.TryGetProperty("Evidence", out var evidenceNode) ? evidenceNode.GetString() ?? "" : "";
            var confidence = row.TryGetProperty("ClassificationConfidence", out var confidenceNode) && confidenceNode.TryGetInt32(out var c) ? c : 0;
            var evidenceQuality = row.TryGetProperty("EvidenceQuality", out var evidenceNode2) && evidenceNode2.TryGetInt32(out var eq) ? eq : 0;

            if (string.IsNullOrWhiteSpace(inferred) ||
                string.Equals(inferred, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                model.CategoryOverride ||
                !IsBuiltInOrUnresolved(model.Category))
            {
                if (model.CategoryOverride) skippedProtected++;
                continue;
            }

            var storedUnresolved = string.IsNullOrWhiteSpace(stored) ||
                                   string.Equals(stored, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(stored, "Unknown", StringComparison.OrdinalIgnoreCase);

            var strongUnresolvedLexical = storedUnresolved &&
                                          basis.StartsWith("Lexical evidence:", StringComparison.OrdinalIgnoreCase) &&
                                          confidence >= 60 &&
                                          evidence.Contains("Lexical cue:", StringComparison.OrdinalIgnoreCase);

            var unresolvedFunctionalCandidate = storedUnresolved &&
                                          string.Equals(inferred, "Functional", StringComparison.OrdinalIgnoreCase) &&
                                          confidence >= 85 &&
                                          evidenceQuality >= 25;
            var unresolvedRoleCandidate = storedUnresolved &&
                                          confidence >= 85 &&
                                          evidenceQuality >= 25 &&
                                          (basis.Contains("Object role evidence:", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Generalized artifact role evidence", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Role-aware artifact arbitration:", StringComparison.OrdinalIgnoreCase));

            var highConfidenceResolution = string.Equals(disposition, "RESOLUTION_CANDIDATE", StringComparison.OrdinalIgnoreCase) &&
                                            confidence >= 85;

            if (!strongUnresolvedLexical && !unresolvedFunctionalCandidate && !unresolvedRoleCandidate && !highConfidenceResolution)
                continue;

            if (string.Equals(model.Category, inferred, StringComparison.OrdinalIgnoreCase))
                continue;

            model.Category = inferred;
            changed++;
            if (strongUnresolvedLexical) unresolvedLexical++;
            if (unresolvedRoleCandidate) unresolvedRoleCandidates++;
            if (highConfidenceResolution) resolutionCandidates++;
        }

        repository.SaveAll(models, new HashSet<string>(models.Select(m => m.Path), StringComparer.OrdinalIgnoreCase));
        var persisted = repository.GetAll().Count;

        Console.WriteLine($"REPAIR_RECLASSIFIED={changed}");
        Console.WriteLine($"REPAIR_UNRESOLVED_LEXICAL={unresolvedLexical}");
        Console.WriteLine($"REPAIR_UNRESOLVED_ROLE_CANDIDATES={unresolvedRoleCandidates}");
        Console.WriteLine($"REPAIR_RESOLUTION_CANDIDATES={resolutionCandidates}");
        Console.WriteLine($"REPAIR_SKIPPED_PROTECTED={skippedProtected}");
        Console.WriteLine($"REPAIR_CATALOG={models.Count}");
        Console.WriteLine($"REPAIR_PERSISTED_CATALOG={persisted}");

        if (models.Count != expectedCatalog) return 14;
        if (persisted != expectedCatalog) return 15;

        Console.WriteLine("REPAIR=PASS");
        return 0;
    }

    Console.WriteLine($"EXPECTED_CATALOG={expectedCatalog}");

    var forensic = new WholeLibraryRootCauseAnalysisService(repository);
    var resultReport = forensic.Run(
        outputDirectory: outputDirectory,
        progress: new Progress<(int processed, int total, string phase)>(p =>
        {
            if (p.processed == p.total || p.processed % 100 == 0)
                Console.WriteLine($"PROGRESS={p.processed}/{p.total} PHASE={p.phase}");
        }));

    Console.WriteLine($"CATALOG={resultReport.Catalog}");
    Console.WriteLine($"PROCESSED={resultReport.Processed}");
    Console.WriteLine($"CONFLICTS={resultReport.Conflicts}");
    Console.WriteLine($"ANALYZED={resultReport.AnalyzedConflicts}");
    Console.WriteLine($"FAILURES={resultReport.Failed}");
    Console.WriteLine($"REPORT={resultReport.ReportPath}");
    Console.WriteLine($"CSV={resultReport.CsvPath}");
    Console.WriteLine($"JSON={resultReport.JsonPath}");

    if (resultReport.Failed != 0)
        return 10;
    if (resultReport.Catalog != expectedCatalog || resultReport.Processed != expectedCatalog)
        return 11;

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{command.ToUpperInvariant()}_EXCEPTION");
    Console.Error.WriteLine(ex);
    return 99;
}

static IReadOnlyList<string> InferCommonRoots(IEnumerable<string> paths)
{
    var directories = paths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.GetFullPath(p))
        .Select(Path.GetDirectoryName)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => p!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (directories.Count == 0) return Array.Empty<string>();

    var common = directories[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    foreach (var directory in directories.Skip(1))
    {
        var candidate = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!IsSameOrBelow(candidate, common))
        {
            var parent = Directory.GetParent(common)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, common, StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();
            common = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    var root = Path.GetPathRoot(common)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
    if (string.Equals(common, root, StringComparison.OrdinalIgnoreCase))
        return Array.Empty<string>();

    return new[] { common };
}

static bool IsSameOrBelow(string path, string root)
{
    var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
           normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
           normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
