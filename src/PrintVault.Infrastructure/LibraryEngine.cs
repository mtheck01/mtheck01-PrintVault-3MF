using System.Security.Cryptography;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed class LibraryEngine : ILibraryEngine
{
    private readonly LibraryRepository repo = new();
    private readonly ThreeMfAnalyzer analyzer = new();
    private readonly ModelIntelligencePipeline intelligencePipeline = new();
    private readonly LibraryFileDiscovery fileDiscovery = new();
    private readonly DuplicateGroupBuilder duplicateGroups = new();
    public LibraryRepository Repository => repo;

    public async Task<IReadOnlyList<ModelRecord>> ScanAsync(IEnumerable<string> roots, ScanMode mode = ScanMode.Turbo, CancellationToken token = default, IProgress<ScanProgress>? progress = null)
    {
        var rootList = roots.Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var autonomousClassificationOnly = string.Equals(
            Environment.GetEnvironmentVariable("PRINTVAULT_AUTONOMOUS_CLASSIFICATION_ONLY"),
            "1",
            StringComparison.OrdinalIgnoreCase);
        var files = fileDiscovery.Discover3MfFiles(rootList);

        progress?.Report(new ScanProgress("Indexing", files.Length, 0, 0, 0, files.Length == 0 ? 100 : 0));

        var existing = repo.GetAllMap();
        var result = new ModelRecord?[files.Length];
        var stale = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        var indexed = 0;
        var failed = 0;

        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = autonomousClassificationOnly ? Math.Clamp(Environment.ProcessorCount, 2, 6) : Math.Clamp(Environment.ProcessorCount, 4, 12),
            CancellationToken = token
        }, async (i, ct) =>
        {
            var path = files[i];
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return;

                existing.TryGetValue(path, out var m);
                var changed = m is null || m.Size != fi.Length || m.ModifiedUtc != fi.LastWriteTimeUtc;
                var existingCategory = m?.Category ?? "Uncategorized";
                var folderCategory = GetTopLevelCustomCategory(rootList, path);
                var preserveCustomCategory = m?.CategoryOverride == true || (!IsLegacyCategory(existingCategory) && IsCustomCategory(existingCategory)) || (!IsLegacyCategory(folderCategory) && !string.IsNullOrWhiteSpace(folderCategory));
                var preservedCategory = m?.CategoryOverride == true ? existingCategory : ((!IsLegacyCategory(existingCategory) && IsCustomCategory(existingCategory)) ? existingCategory : (!IsLegacyCategory(folderCategory) ? folderCategory : null));
                var needsIntelligence = m is null || changed || string.IsNullOrWhiteSpace(m.SemanticType) || string.IsNullOrWhiteSpace(m.IntelligenceReason);
                var doIntelligence = mode != ScanMode.Quick && (mode == ScanMode.Deep || needsIntelligence);
                // Autonomous classification must not block on enrichment. Hashing and thumbnail
                // extraction are useful UI enrichment, but they are not prerequisites for
                // classification, organization, or taxonomy cleanup. They are deliberately
                // deferred during the bounded autonomous pass.
                var doHash = !autonomousClassificationOnly &&
                             mode != ScanMode.Quick &&
                             (mode == ScanMode.Deep || changed || string.IsNullOrWhiteSpace(m?.Hash));
                var doThumbnail = !autonomousClassificationOnly && mode != ScanMode.Quick;
                m ??= new ModelRecord { Path = path, Name = fi.Name, Category = "Uncategorized" };
                m.Path = path;
                m.Name = fi.Name;
                m.Size = fi.Length;
                m.ModifiedUtc = fi.LastWriteTimeUtc;

                if (doIntelligence)
                {
                    var a = autonomousClassificationOnly ? analyzer.AnalyzeFilenameOnly(path, fi.Name) : analyzer.Analyze(path, fi.Name);
                    // Deep analysis must never destroy a user-defined category.
                    // Automatic classification is only allowed to choose a built-in category
                    // when the item is currently Uncategorized or already using a built-in one.
                    // A custom category is treated as user intent and remains authoritative.
                    m.Category = preserveCustomCategory && !string.IsNullOrWhiteSpace(preservedCategory)
                        ? preservedCategory!
                        : a.Category;
                    m.IntelligenceScore = a.Confidence;
                    if (!m.PrintMethodOverride) { m.PrintMethod = a.PrintMethod; m.PrintMethodConfidence = a.PrintMethodConfidence; m.PrintMethodEvidence = a.PrintMethodEvidence; }
                    m.SpecialType = a.SpecialType;
                    m.Family = a.Family;
                    m.ObjectCount = a.ObjectCount;
                    m.Dimensions = a.Dimensions;
                    m.Slicer = a.Slicer;
                    m.Materials = a.Materials;
                    m.PrintReady = a.PrintReady;
                    m.SemanticType = a.SemanticType; m.Subtype = a.Subtype; m.SuggestedTags = a.SuggestedTags; m.IntelligenceReason = a.Reason; m.RiskFlags = a.RiskFlags;
                    // Named-entity intelligence outranks generic analyzer metadata, while
                    // manual/custom categories remain protected. Raw analyzer evidence is
                    // retained in IntelligenceReason by EntityClassificationService.
                    intelligencePipeline.Apply(m);
                    // Preserve user tags; the intelligence layer stores its own suggested tags separately.
                    if (doHash)
                    {
                        m.Hash = await HashAsync(path, ct);
                    }
                    if (changed)
                    {
                        m.ThumbnailPath = null; // invalidate cache reference when file content/metadata changes
                        m.DuplicateGroup = "";
                        if (mode == ScanMode.Quick) m.Hash = "";
                    }
                }

                if (doThumbnail && !m.HasThumbnail)
                    m.ThumbnailPath = await ThumbnailService.ExtractAsync(path, ct);

                result[i] = m;
                Interlocked.Increment(ref indexed);
            }
            catch (OperationCanceledException) { throw; }
            catch { Interlocked.Increment(ref failed); }
            finally
            {
                var processed = Interlocked.Increment(ref n);
                var pct = processed * 100 / Math.Max(1, files.Length);
                progress?.Report(new ScanProgress("Indexing", files.Length, processed, Volatile.Read(ref indexed), Volatile.Read(ref failed), pct));
            }
        });

        token.ThrowIfCancellationRequested();
        stale.ExceptWith(result.Where(x => x is not null).Select(x => x!.Path));
        var models = result.Where(x => x is not null).Select(x => x!).ToList();

        // Rebuild duplicate groups from scratch so resolved duplicates do not retain stale group IDs.
        duplicateGroups.Rebuild(models);

        repo.SaveAll(models, stale);
        return models.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<LibraryRebuildResult> RebuildAsync(IEnumerable<string> roots, CancellationToken token = default, IProgress<ScanProgress>? progress = null)
    {
        // A rebuild deliberately ignores file timestamps and previous built-in classifications.
        // User-defined categories remain authoritative; the analyzer must re-evaluate everything else.
        var rootList = roots.Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var files = fileDiscovery.Discover3MfFiles(rootList);

        var previous = repo.GetAllMap();
        progress?.Report(new ScanProgress("Rebuilding", files.Length, 0, 0, 0, files.Length == 0 ? 100 : 0));
        var result = new ModelRecord?[files.Length];
        var n = 0;
        var indexed = 0;
        var failed = 0;
        var reclassified = 0;
        var preservedCustom = 0;

        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 12),
            CancellationToken = token
        }, async (i, ct) =>
        {
            var path = files[i];
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return;

                previous.TryGetValue(path, out var old);
                var oldCategory = old?.Category ?? "Uncategorized";
                var folderCategory = GetTopLevelCustomCategory(rootList, path);
                var customCategory = old?.CategoryOverride == true ? oldCategory : ((!IsLegacyCategory(oldCategory) && IsCustomCategory(oldCategory)) ? oldCategory : (!IsLegacyCategory(folderCategory) ? folderCategory : null));

                var m = new ModelRecord
                {
                    Id = old?.Id ?? 0,
                    Path = path,
                    Name = fi.Name,
                    Size = fi.Length,
                    ModifiedUtc = fi.LastWriteTimeUtc,
                    Category = customCategory ?? "Uncategorized",
                    CategoryOverride = old?.CategoryOverride ?? false,
                    PrintMethod = old?.PrintMethod ?? "Unknown", PrintMethodConfidence = old?.PrintMethodConfidence ?? 0, PrintMethodEvidence = old?.PrintMethodEvidence ?? "", PrintMethodOverride = old?.PrintMethodOverride ?? false, SpecialType = old?.SpecialType ?? "",
                    Favorite = old?.Favorite ?? false,
                    Tags = old?.Tags ?? ""
                };

                var a = analyzer.Analyze(path, fi.Name);
                if (!string.IsNullOrWhiteSpace(customCategory))
                {
                    m.Category = customCategory!;
                    Interlocked.Increment(ref preservedCustom);
                }
                else
                {
                    m.Category = a.Category;
                    if (!string.Equals(oldCategory, m.Category, StringComparison.OrdinalIgnoreCase))
                        Interlocked.Increment(ref reclassified);
                }

                m.IntelligenceScore = a.Confidence;
                if (!m.PrintMethodOverride) { m.PrintMethod = a.PrintMethod; m.PrintMethodConfidence = a.PrintMethodConfidence; m.PrintMethodEvidence = a.PrintMethodEvidence; }
                m.SpecialType = a.SpecialType;
                m.Family = a.Family;
                m.ObjectCount = a.ObjectCount;
                m.Dimensions = a.Dimensions;
                m.Slicer = a.Slicer;
                m.Materials = a.Materials;
                m.PrintReady = a.PrintReady;
                m.SemanticType = a.SemanticType;
                m.Subtype = a.Subtype;
                m.SuggestedTags = a.SuggestedTags;
                m.IntelligenceReason = a.Reason;
                m.RiskFlags = a.RiskFlags;
                // Rebuilds must use the same semantic arbitration path as incremental scans.
                // Previously the forensic fusion engine existed but RebuildAsync never applied
                // it, so whole-library rebuilds could not benefit from the architecture we were
                // testing. Named entities and high-confidence role/source-derived evidence now
                // participate in production classification during the actual rebuild.
                intelligencePipeline.Apply(m);
                // Optional enrichment must never be allowed to discard an otherwise valid model.
                // A corrupt/unsupported thumbnail or hash is a property of the enrichment step,
                // not a reason to lose the catalog record.
                try { m.Hash = await HashAsync(path, ct); }
                catch { m.Hash = ""; }
                try
                {
                    m.ThumbnailPath = old?.ThumbnailPath;
                    if (!m.HasThumbnail) m.ThumbnailPath = await ThumbnailService.ExtractAsync(path, ct);
                }
                catch { m.ThumbnailPath = null; }

                result[i] = m;
                Interlocked.Increment(ref indexed);
            }
            catch (OperationCanceledException) { throw; }
            catch { Interlocked.Increment(ref failed); }
            finally
            {
                var processed = Interlocked.Increment(ref n);
                var pct = processed * 100 / Math.Max(1, files.Length);
                progress?.Report(new ScanProgress("Rebuilding", files.Length, processed, Volatile.Read(ref indexed), Volatile.Read(ref failed), pct));
            }
        });

        token.ThrowIfCancellationRequested();
        var models = result.Where(x => x is not null).Select(x => x!).ToList();
        duplicateGroups.Rebuild(models);

        // SaveAll with the old path set removes records for files that no longer exist.
        repo.SaveAll(models, new HashSet<string>(previous.Keys, StringComparer.OrdinalIgnoreCase));
        var persisted = repo.GetAll();
        if (persisted.Count != models.Count)
            throw new InvalidOperationException($"Rebuild reconciliation failed: produced {models.Count:N0} records but database read-back returned {persisted.Count:N0}.");
        return new LibraryRebuildResult(models.Count, reclassified, preservedCustom, failed, files.Length);
    }

    public async Task<(int Models, int Reclassified, int Failed)> ReconcileCategoriesAsync(
        CancellationToken token = default, IProgress<(int processed, int total, int reclassified, int failed)>? progress = null)
    {
        var models = repo.GetAll();
        var reclassified = 0;
        var failed = 0;
        var processed = 0;

        // Category repair is deliberately classification-only. It must not re-hash 1,758
        // files or regenerate thumbnails; those enrichment operations are unrelated to
        // category reconciliation and were making the autonomous repair stage unnecessarily
        // long and cancellable.
        await Parallel.ForEachAsync(models, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 12),
            CancellationToken = token
        }, (model, ct) =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var before = model.Category;
                var a = analyzer.Analyze(model.Path, model.Name);
                if (!model.CategoryOverride && IsBuiltInOrUnresolved(before))
                {
                    model.IntelligenceScore = a.Confidence;
                    model.Family = a.Family;
                    model.ObjectCount = a.ObjectCount;
                    model.Dimensions = a.Dimensions;
                    model.Slicer = a.Slicer;
                    model.Materials = a.Materials;
                    model.PrintReady = a.PrintReady;
                    model.SemanticType = a.SemanticType;
                    model.Subtype = a.Subtype;
                    model.SuggestedTags = a.SuggestedTags;
                    model.IntelligenceReason = a.Reason;
                    model.RiskFlags = a.RiskFlags;
                    if (!model.PrintMethodOverride)
                    {
                        model.PrintMethod = a.PrintMethod;
                        model.PrintMethodConfidence = a.PrintMethodConfidence;
                        model.PrintMethodEvidence = a.PrintMethodEvidence;
                    }

                    intelligencePipeline.Apply(model);

                    if (!string.Equals(before, model.Category, StringComparison.OrdinalIgnoreCase))
                        Interlocked.Increment(ref reclassified);
                }

                return ValueTask.CompletedTask;
            }
            catch (OperationCanceledException) { throw; }
            catch { Interlocked.Increment(ref failed); return ValueTask.CompletedTask; }
            finally
            {
                var done = Interlocked.Increment(ref processed);
                progress?.Report((done, models.Count, Volatile.Read(ref reclassified), Volatile.Read(ref failed)));
            }
        });

        token.ThrowIfCancellationRequested();
        repo.SaveAll(models, new HashSet<string>(models.Select(m => m.Path), StringComparer.OrdinalIgnoreCase));
        var persisted = repo.GetAll().Count;
        if (persisted != models.Count)
            throw new InvalidOperationException($"Category reconciliation lost records: expected {models.Count:N0}, persisted {persisted:N0}.");

        return (models.Count, reclassified, failed);
    }

    public Task<IntelligenceResult> AnalyzeAsync(ModelRecord model, CancellationToken token = default)
        => Task.Run(() => analyzer.Analyze(model.Path, model.Name), token);

    public Task<LibraryStats> GetStatsAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var all = repo.GetAll();
        var dup = all.Where(x => !string.IsNullOrEmpty(x.Hash)).GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() > 1);
        return Task.FromResult(new LibraryStats(all.Count, all.Count(m => m.Favorite), dup, all.Count(m => m.IntelligenceScore < .5), all.Sum(m => m.Size), all.Count(m => !m.HasThumbnail)));
    }


    private static bool IsBuiltInOrUnresolved(string? category)
        => string.IsNullOrWhiteSpace(category) ||
           string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
           IsLegacyCategory(category) ||
           BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsLegacyCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        var value = category.Trim();
        if (value is "06_Cosplay" or "08_Aviation" or "09_Models" or "10_Multi_Color" or "11_Test_Print" or
            "Soap Holders" or "test eng 8.6" or "Cosplay" or "Aviation" or "Aircraft" or "Automotive" or
            "Decor" or "Decorative" or "Figures" or "Gaming" or "Game Models" or "Multi-Color" or
            "Test Print" or "Test Prints" or "Other" or "Needs Review")
            return true;

        // Older libraries also encoded the taxonomy as numbered folder names
        // (for example 11_Test_Print). Treat every numbered taxonomy folder as
        // legacy so it cannot be mistaken for user intent and preserved forever.
        var i = 0;
        while (i < value.Length && char.IsDigit(value[i])) i++;
        return i > 0 && i < value.Length && value[i] is '_' or '-' or ' ';
    }

    private static bool IsCustomCategory(string? category)
        => !string.IsNullOrWhiteSpace(category) &&
           !BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string? GetTopLevelCustomCategory(IEnumerable<string> libraryRoots, string path)
    {
        try
        {
            var fileFull = Path.GetFullPath(path);
            foreach (var libraryRoot in libraryRoots)
            {
                var rootFull = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var relative = Path.GetRelativePath(rootFull, fileFull);
                if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
                var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var top = parts[0].Trim();
                if (IsCustomCategory(top) && !IsLegacyCategory(top)) return top;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var h = await sha.ComputeHashAsync(s, ct);
        return Convert.ToHexString(h);
    }
}
