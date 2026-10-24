using System.Text.Json;
using PrintVault.Core;
using PrintVault.Infrastructure;

if (args.Length == 0 || (!string.Equals(args[0], "rootcause", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "repair", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "organize", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "cleanup", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "selftest-category-registry", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(args[0], "selftest-semantic-cues", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: PrintVault.Automation rootcause [outputDirectory] [expectedCatalog]");
    Console.Error.WriteLine("   or: PrintVault.Automation scan [libraryRoot] [mode]");
    Console.Error.WriteLine("       Autonomous runs may supply PRINTVAULT_TEST_LIBRARY_ROOT instead of libraryRoot.");
    Console.Error.WriteLine("   or: PrintVault.Automation repair");
    Console.Error.WriteLine("   or: PrintVault.Automation organize [libraryRoot]");
    Console.Error.WriteLine("   or: PrintVault.Automation cleanup [libraryRoot]");
    Console.Error.WriteLine("   or: PrintVault.Automation selftest-category-registry");
    Console.Error.WriteLine("   or: PrintVault.Automation selftest-semantic-cues");
    return 2;
}

var command = args[0];

if (string.Equals(command, "selftest-category-registry", StringComparison.OrdinalIgnoreCase))
    return CategoryRegistrySelfTest.Run();
if (string.Equals(command, "selftest-semantic-cues", StringComparison.OrdinalIgnoreCase))
    return SemanticCueSelfTest.Run();

var configuredTestRoot = Environment.GetEnvironmentVariable("PRINTVAULT_TEST_LIBRARY_ROOT");
var scanRoot = string.Equals(command, "scan", StringComparison.OrdinalIgnoreCase)
    ? (!string.IsNullOrWhiteSpace(configuredTestRoot)
        ? Path.GetFullPath(configuredTestRoot)
        : args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? Path.GetFullPath(args[1])
            : "")
    : (string.Equals(command, "organize", StringComparison.OrdinalIgnoreCase) ||
       string.Equals(command, "cleanup", StringComparison.OrdinalIgnoreCase))
        ? (!string.IsNullOrWhiteSpace(configuredTestRoot)
            ? Path.GetFullPath(configuredTestRoot)
            : args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                ? Path.GetFullPath(args[1])
                : "")
        : "";
var scanMode = args.Length > 2 && string.Equals(command, "scan", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<ScanMode>(args[2], true, out var parsedMode) ? parsedMode : ScanMode.Deep;
var outputDirectory = args.Length > 1 && !string.Equals(command, "repair", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[1])
    ? Path.GetFullPath(args[1])
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "reports");

int? expectedCatalog = args.Length > 2 && int.TryParse(args[2], out var parsed)
    ? parsed
    : null;

bool IsLegacyCategory(string? category)
{
    if (string.IsNullOrWhiteSpace(category)) return false;
    var value = category.Trim();
    if (value is
        "02_Functional" or "02_Household" or
        "03_Automotive" or "07_Automotive" or "Automotive" or
        "03_Decor" or "06_Decorative" or "Decor" or "Decorative" or
        "04_Figures" or "Figures" or
        "05_Game_Models" or "05_Gaming" or "Gaming" or "Game Models" or
        "06_Cosplay" or "Cosplay" or
        "08_Aviation" or "09_Aircraft" or "Aviation" or "Aircraft" or
        "09_Models" or "Models" or
        "07_Multi_Color" or "10_Multi_Color" or "Multi_Color" or "Multi-Color" or
        "08_Test_Print" or "11_Test_Print" or "Test_Print" or "Test Print" or "Test Prints" or
        "99_Other" or "Other" or "Needs Review" or
        "Soap Holders" or "test eng 8.6")
        return true;

    var i = 0;
    while (i < value.Length && char.IsDigit(value[i])) i++;
    return i > 0 && i < value.Length && value[i] is '_' or '-' or ' ';
}

bool IsBuiltInOrUnresolved(string? category) =>
    string.IsNullOrWhiteSpace(category) ||
    string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(category, "Unknown", StringComparison.OrdinalIgnoreCase) ||
    IsLegacyCategory(category) ||
    BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

try
{
    var configuredDatabasePath = Environment.GetEnvironmentVariable("PRINTVAULT_DATABASE_PATH");
    var repository = string.IsNullOrWhiteSpace(configuredDatabasePath)
        ? new LibraryRepository()
        : new LibraryRepository(configuredDatabasePath);
    Console.WriteLine($"DATABASE={repository.DatabasePath}");

    if (string.Equals(command, "scan", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"SCAN_ROOT_SOURCE={(string.IsNullOrWhiteSpace(configuredTestRoot) ? "ARGUMENT" : "ENVIRONMENT")}");

        if (string.IsNullOrWhiteSpace(scanRoot) || !Directory.Exists(scanRoot))
        {
            Console.Error.WriteLine($"SCAN_ABORTED=LIBRARY_ROOT_NOT_FOUND:{scanRoot}");
            return 20;
        }
        Console.WriteLine($"SCAN_ROOT={scanRoot}");
        Console.WriteLine($"SCAN_MODE={scanMode}");
        var scanEngine = new LibraryEngine(repository);
        var lastFailed = 0;
        long lastStageLogTicks = DateTime.UtcNow.Ticks;
        var scanProgress = new InlineProgress<ScanProgress>(p =>
        {
            lastFailed = p.Failed;
            if (p.CurrentStage is "Analysis" or "Hash" or "Thumbnail")
            {
                var now = DateTime.UtcNow.Ticks;
                var previous = Interlocked.Read(ref lastStageLogTicks);
                if (now - previous >= TimeSpan.FromSeconds(15).Ticks &&
                    Interlocked.CompareExchange(ref lastStageLogTicks, now, previous) == previous)
                    Console.WriteLine($"SCAN_ACTIVE_STAGE={p.CurrentStage} PATH={JsonSerializer.Serialize(p.CurrentPath)}");
            }
            if (p.CurrentStage == "Completed" && p.LastFileElapsedMilliseconds >= 5000)
                Console.WriteLine($"SCAN_SLOW_FILE PATH={JsonSerializer.Serialize(p.CurrentPath)} TOTAL_MS={p.LastFileElapsedMilliseconds} ANALYSIS_MS={p.LastFileAnalysisMilliseconds} HASH_MS={p.LastFileHashMilliseconds} THUMBNAIL_MS={p.LastFileThumbnailMilliseconds}");
            if (p.Processed == p.Discovered || p.Processed % 100 == 0)
                Console.WriteLine($"SCAN_PROGRESS={p.Processed}/{p.Discovered} INDEXED={p.Indexed} FAILED={p.Failed} CHECKPOINTS={p.CheckpointCount} ANALYSIS_MS={p.AnalysisElapsedMilliseconds} HASH_MS={p.HashElapsedMilliseconds} THUMBNAIL_MS={p.ThumbnailElapsedMilliseconds} PERSISTENCE_MS={p.PersistenceElapsedMilliseconds} LAST_FILE_MS={p.LastFileElapsedMilliseconds} LAST_FILE_ANALYSIS_MS={p.LastFileAnalysisMilliseconds} LAST_FILE_HASH_MS={p.LastFileHashMilliseconds} LAST_FILE_THUMBNAIL_MS={p.LastFileThumbnailMilliseconds}");
            if (p.Phase == "Completed")
                Console.WriteLine($"SCAN_STAGE_TOTALS ANALYSIS_MS={p.AnalysisElapsedMilliseconds} HASH_MS={p.HashElapsedMilliseconds} THUMBNAIL_MS={p.ThumbnailElapsedMilliseconds} PERSISTENCE_MS={p.PersistenceElapsedMilliseconds} CHECKPOINTS={p.CheckpointCount}");
        });
        var scanned = await scanEngine.ScanAsync(new[] { scanRoot }, scanMode, default, scanProgress);
        var persisted = scanEngine.Repository.GetAll().Count;
        Console.WriteLine($"SCAN_FAILED={lastFailed}");
        Console.WriteLine($"SCAN_DISCOVERED={scanned.Count}");
        Console.WriteLine($"SCAN_PERSISTED={persisted}");
        if (persisted != scanned.Count)
        {
            Console.Error.WriteLine($"SCAN_PERSISTENCE_MISMATCH=EXPECTED:{scanned.Count};ACTUAL:{persisted}");
            return 21;
        }
        if (lastFailed != 0)
        {
            Console.Error.WriteLine($"SCAN_FAILED_FILES={lastFailed}; catalog was preserved, but scan is not considered a pass.");
            return 22;
        }
        Console.WriteLine("SCAN=PASS");
        return 0;
    }
    if (string.Equals(command, "cleanup", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"CLEANUP_ROOT_SOURCE={(string.IsNullOrWhiteSpace(configuredTestRoot) ? "ARGUMENT" : "ENVIRONMENT")}");

        if (string.IsNullOrWhiteSpace(scanRoot) || !Directory.Exists(scanRoot))
        {
            Console.Error.WriteLine($"CLEANUP_ABORTED=LIBRARY_ROOT_NOT_FOUND:{scanRoot}");
            return 30;
        }

        Console.WriteLine($"CLEANUP_ROOT={scanRoot}");
        var beforeFiles = Directory.EnumerateFiles(scanRoot, "*.3mf", SearchOption.AllDirectories).Count();
        var beforeRecords = repository.GetAll().Count;
        var organization = new OrganizationService(repository, scanRoot);
        var result = organization.CleanupLegacyTaxonomy();
        var afterFiles = Directory.EnumerateFiles(scanRoot, "*.3mf", SearchOption.AllDirectories).Count();
        var afterRecords = repository.GetAll().Count;

        Console.WriteLine($"FILES_BEFORE={beforeFiles}");
        Console.WriteLine($"FILES_AFTER={afterFiles}");
        Console.WriteLine($"RECORDS_BEFORE={beforeRecords}");
        Console.WriteLine($"RECORDS_AFTER={afterRecords}");
        Console.WriteLine($"LEGACY_CATEGORIES_FOUND={result.LegacyCategoriesFound}");
        Console.WriteLine($"LEGACY_TAGS_REMOVED={result.LegacyTagsRemoved}");
        Console.WriteLine($"RECORDS_NORMALIZED={result.RecordsNormalized}");
        Console.WriteLine($"FILES_MOVED={result.FilesMoved}");
        Console.WriteLine($"LEGACY_CATEGORIES_REMAINING={result.LegacyCategoriesRemaining}");
        Console.WriteLine($"LEGACY_TAGS_REMAINING={result.LegacyTagsRemaining}");
        Console.WriteLine($"LEGACY_FOLDERS_REMAINING={result.LegacyFoldersRemaining}");
        Console.WriteLine($"LEGACY_REGISTRY_ENTRIES_REMOVED={result.LegacyRegistryEntriesRemoved}");
        Console.WriteLine($"LEGACY_REGISTRY_ENTRIES_REMAINING={result.LegacyRegistryEntriesRemaining}");

        if (afterFiles != beforeFiles) return 31;
        if (afterRecords != beforeRecords) return 32;
        if (result.LegacyCategoriesRemaining != 0) return 33;
        if (result.LegacyTagsRemaining != 0) return 34;
        if (result.LegacyFoldersRemaining != 0) return 35;

        Console.WriteLine("LEGACY_TAXONOMY_CLEANUP=PASS");
        return 0;
    }

    if (string.Equals(command, "organize", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"ORGANIZE_ROOT_SOURCE={(string.IsNullOrWhiteSpace(configuredTestRoot) ? "ARGUMENT" : "ENVIRONMENT")}");

        if (string.IsNullOrWhiteSpace(scanRoot) || !Directory.Exists(scanRoot))
        {
            Console.Error.WriteLine($"ORGANIZE_ABORTED=LIBRARY_ROOT_NOT_FOUND:{scanRoot}");
            return 30;
        }
        Console.WriteLine($"ORGANIZE_ROOT={scanRoot}");
        var organization = new OrganizationService(repository, scanRoot);
        var moved = organization.OrganizeClassifiedFiles();
        Console.WriteLine($"ORGANIZE_MOVED={moved}");
        Console.WriteLine("ORGANIZE=PASS");
        return 0;
    }

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
        var reportRoot = report.RootElement;
        var reportCatalogValid = reportRoot.TryGetProperty("Catalog", out var reportCatalogNode) &&
                                 reportCatalogNode.TryGetInt32(out var reportCatalog) && reportCatalog == models.Count;
        var reportProcessedValid = reportRoot.TryGetProperty("Processed", out var reportProcessedNode) &&
                                   reportProcessedNode.TryGetInt32(out var reportProcessed) && reportProcessed == models.Count;
        if (!reportCatalogValid || !reportProcessedValid)
        {
            Console.Error.WriteLine($"REPAIR_ABORTED=REPORT_CATALOG_MISMATCH REPORT_CATALOG={reportCatalogNode} REPORT_PROCESSED={reportProcessedNode} DATABASE_CATALOG={models.Count}");
            return 16;
        }
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
            var arbitrationDecision = row.TryGetProperty("ArbitrationDecision", out var decisionNode) ? decisionNode.GetString() ?? "" : "";
            var basis = row.TryGetProperty("Basis", out var basisNode) ? basisNode.GetString() ?? "" : "";
            var evidence = row.TryGetProperty("Evidence", out var evidenceNode) ? evidenceNode.GetString() ?? "" : "";
            var confidence = row.TryGetProperty("ClassificationConfidence", out var confidenceNode) && confidenceNode.TryGetInt32(out var c) ? c : 0;
            var evidenceQuality = row.TryGetProperty("EvidenceQuality", out var evidenceNode2) && evidenceNode2.TryGetInt32(out var eq) ? eq : 0;

            if (string.IsNullOrWhiteSpace(inferred) ||
                string.Equals(inferred, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                model.CategoryOverride)
            {
                if (model.CategoryOverride) skippedProtected++;
                continue;
            }

            // The release gate and writeback must share the same typed policy.
            // REVIEW/protected/unknown decisions are evidence for a person, not
            // authorization to persist a candidate category.
            if (!string.Equals(arbitrationDecision, "ACTIONABLE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(model.Category, stored, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"REPAIR_ABORTED=STALE_REPORT_RECORD ID={id} REPORT_STORED={stored} DATABASE_STORED={model.Category}");
                return 17;
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

            // Legacy/custom folder categories are not semantic classifications. When an
            // unprotected record has a strong built-in inference, migrate it deterministically
            // instead of spending an AI repair call on a known taxonomy migration problem.
            // Manual CategoryOverride records remain protected above.
            var legacyCategoryMigration = (IsLegacyCategory(model.Category) || !IsBuiltInOrUnresolved(model.Category)) &&
                                          BuiltInCategories.All.Contains(inferred.Trim(), StringComparer.OrdinalIgnoreCase) &&
                                          confidence >= 60 &&
                                          evidenceQuality >= 25 &&
                                          (basis.StartsWith("Lexical evidence:", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Object role evidence:", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Generalized artifact role evidence", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Role-aware artifact arbitration:", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Artifact lexical evidence", StringComparison.OrdinalIgnoreCase) ||
                                           basis.Contains("Source-derived analyzer", StringComparison.OrdinalIgnoreCase));

            if (!IsBuiltInOrUnresolved(model.Category) && !highConfidenceResolution && !legacyCategoryMigration)
                continue;

            if (!strongUnresolvedLexical && !unresolvedFunctionalCandidate && !unresolvedRoleCandidate && !highConfidenceResolution && !legacyCategoryMigration)
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

        if (expectedCatalog.HasValue && models.Count != expectedCatalog.Value) return 14;
        if (expectedCatalog.HasValue && persisted != expectedCatalog.Value) return 15;

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
    Console.WriteLine($"RECORDS_ANALYZED={resultReport.RecordsAnalyzed}");
    Console.WriteLine($"CONFLICT_REVIEW_ROWS={resultReport.AnalyzedConflicts}");
    Console.WriteLine($"RESOLVED_INFERENCES={resultReport.ResolvedInferences}");
    Console.WriteLine($"UNRESOLVED_INFERENCES={resultReport.UnresolvedInferences}");
    Console.WriteLine($"EVIDENCE_BEARING_RESULTS={resultReport.EvidenceBearingResults}");
    Console.WriteLine($"REVIEW_REQUIRED_RESULTS={resultReport.ReviewRequiredResults}");
    Console.WriteLine($"MISSING_INFERENCE_RESULTS={resultReport.MissingInferenceResults}");
    Console.WriteLine($"OUTCOME_FINGERPRINT={resultReport.OutcomeFingerprint}");
    Console.WriteLine($"FAILURES={resultReport.Failed}");
    Console.WriteLine($"REPORT={resultReport.ReportPath}");
    Console.WriteLine($"CSV={resultReport.CsvPath}");
    Console.WriteLine($"OUTCOMES_CSV={resultReport.OutcomesCsvPath}");
    Console.WriteLine($"JSON={resultReport.JsonPath}");

    if (resultReport.Failed != 0)
        return 10;
    if (expectedCatalog.HasValue && (resultReport.Catalog != expectedCatalog.Value || resultReport.Processed != expectedCatalog.Value))
        return 11;
    if (resultReport.RecordsAnalyzed != resultReport.Processed ||
        resultReport.ResolvedInferences + resultReport.UnresolvedInferences != resultReport.RecordsAnalyzed ||
        resultReport.MissingInferenceResults != 0)
    {
        Console.Error.WriteLine("DIAGNOSTIC_ACCOUNTING=FAIL");
        return 12;
    }
    if (resultReport.Catalog > 0 &&
        (resultReport.ResolvedInferences == 0 || resultReport.EvidenceBearingResults == 0))
    {
        Console.Error.WriteLine("DIAGNOSTIC_EVIDENCE_COVERAGE=FAIL");
        return 13;
    }

    Console.WriteLine("DIAGNOSTIC_ACCOUNTING=PASS");
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

sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> report;

    public InlineProgress(Action<T> report) => this.report = report;

    public void Report(T value) => report(value);
}
