using PrintVault.Infrastructure;

if (args.Length == 0 || !string.Equals(args[0], "rootcause", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: PrintVault.Automation rootcause [outputDirectory] [expectedCatalog]");
    return 2;
}

var outputDirectory = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
    ? Path.GetFullPath(args[1])
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "reports");

var expectedCatalog = args.Length > 2 && int.TryParse(args[2], out var parsed)
    ? parsed
    : 1758;

try
{
    var repository = new LibraryRepository();
    Console.WriteLine($"DATABASE={repository.DatabasePath}");
    Console.WriteLine($"EXPECTED_CATALOG={expectedCatalog}");

    var service = new WholeLibraryRootCauseAnalysisService(repository);
    var result = service.Run(
        outputDirectory: outputDirectory,
        progress: new Progress<(int processed, int total, string phase)>(p =>
        {
            if (p.processed == p.total || p.processed % 100 == 0)
                Console.WriteLine($"PROGRESS={p.processed}/{p.total} PHASE={p.phase}");
        }));

    Console.WriteLine($"CATALOG={result.Catalog}");
    Console.WriteLine($"PROCESSED={result.Processed}");
    Console.WriteLine($"CONFLICTS={result.Conflicts}");
    Console.WriteLine($"ANALYZED={result.AnalyzedConflicts}");
    Console.WriteLine($"FAILURES={result.Failed}");
    Console.WriteLine($"REPORT={result.ReportPath}");
    Console.WriteLine($"CSV={result.CsvPath}");
    Console.WriteLine($"JSON={result.JsonPath}");

    if (result.Failed != 0)
        return 10;
    if (result.Catalog != expectedCatalog || result.Processed != expectedCatalog)
        return 11;

    return 0;
}
