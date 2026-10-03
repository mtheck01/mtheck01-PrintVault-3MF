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

try
{
    var repository = new LibraryRepository();
    Console.WriteLine($"DATABASE={repository.DatabasePath}");

    if (string.Equals(command, "repair", StringComparison.OrdinalIgnoreCase))
    {
        var models = repository.GetAll()
            .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
            .ToList();

        if (models.Count == 0)
        {
            Console.Error.WriteLine("REPAIR_ABORTED=NO_LIBRARY_FILES");
            return 12;
        }

        var roots = InferCommonRoots(models.Select(m => m.Path));
        if (roots.Count == 0)
        {
            Console.Error.WriteLine("REPAIR_ABORTED=LIBRARY_ROOT_NOT_DETERMINED");
            return 13;
        }

        var backup = repository.BackupDatabase();
        Console.WriteLine($"REPAIR_BACKUP={backup}");
        Console.WriteLine($"REPAIR_ROOTS={string.Join(";", roots)}");
        Console.WriteLine($"REPAIR_INPUT_CATALOG={models.Count}");

        var engine = new LibraryEngine();
        var result = engine.RebuildAsync(
            roots,
            progress: new Progress<ScanProgress>(p =>
            {
                if (p.Processed == p.Total || p.Processed % 100 == 0)
                    Console.WriteLine($"REPAIR_PROGRESS={p.Processed}/{p.Total} PHASE={p.Phase}");
            })).GetAwaiter().GetResult();

        Console.WriteLine($"REPAIR_CATALOG={result.Models}");
        Console.WriteLine($"REPAIR_RECLASSIFIED={result.Reclassified}");
        Console.WriteLine($"REPAIR_PRESERVED_CUSTOM={result.PreservedCustomCategories}");
        Console.WriteLine($"REPAIR_DISCOVERED={result.Discovered}");
        Console.WriteLine($"REPAIR_FAILURES={result.Failed}");

        if (result.Failed != 0) return 14;
        if (result.Models != expectedCatalog) return 15;

        var persisted = repository.GetAll().Count;
        Console.WriteLine($"REPAIR_PERSISTED_CATALOG={persisted}");
        if (persisted != expectedCatalog) return 16;

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
