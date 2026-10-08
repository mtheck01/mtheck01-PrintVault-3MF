using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record ConsolidationMove(string SourcePath, string TargetCategory, string Reason, string Confidence);
public sealed record ConsolidationGroup(string SourceFolder, string TargetCategory, int FileCount, long Bytes, string Confidence, string Reason);
public sealed record ConsolidationPlan(IReadOnlyList<ConsolidationMove> Moves, IReadOnlyList<ConsolidationGroup> Groups, int ReviewCount, int IgnoredCount);
public sealed record ConsolidationResult(int FilesMoved, int FoldersRemoved, long BytesMoved, IReadOnlyList<string> Warnings);
public sealed record ConsolidationUndoEntry(string OldPath, string NewPath, string PreviousCategory, string PreviousTags, bool PreviousCategoryOverride);
public sealed record ConsolidationUndoManifest(DateTime CreatedUtc, string LibraryRoot, IReadOnlyList<ConsolidationUndoEntry> Entries, IReadOnlyList<string> RemovedFolders);

/// <summary>
/// Builds and applies a conservative file-level migration plan for legacy library folders.
/// This is intentionally the single cleanup/consolidation path so folder cleanup rules do not drift.
/// </summary>
public sealed class LibraryConsolidationService
{
    private readonly LibraryRepository repo;
    private readonly string root;
    private readonly string undoPath;

    public LibraryConsolidationService(LibraryRepository repo, string libraryRoot)
    {
        this.repo = repo ?? throw new ArgumentNullException(nameof(repo));
        root = NormalizeRoot(libraryRoot);
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault");
        Directory.CreateDirectory(app);
        undoPath = Path.Combine(app, "last_consolidation.json");
    }

    public ConsolidationPlan Preview(IReadOnlyList<string> categories)
    {
        if (!Directory.Exists(root))
            return new(Array.Empty<ConsolidationMove>(), Array.Empty<ConsolidationGroup>(), 0, 0);

        var canonical = BuildCanonicalMap(categories);
        var moves = new List<ConsolidationMove>();
        var ignored = 0;
        var review = 0;

        // Root-level 3MF files are first-class library members. They must be
        // planned from their catalog classification rather than being silently
        // excluded because they do not have a source folder.
        foreach (var file in Directory.EnumerateFiles(root, "*.3mf", SearchOption.TopDirectoryOnly))
        {
            var model = repo.Get(file);
            var target = DetermineRootFileTarget(model, categories);
            if (target.Target == null)
            {
                ignored++;
                continue;
            }

            var destination = GetSafeDestination(target.Target, Path.GetFileName(file));
            if (PathsEqual(file, destination))
                continue;

            if (target.NeedsReview) review++;
            moves.Add(new ConsolidationMove(file, target.Target, target.Reason, target.Confidence));
        }

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var folder = Path.GetFileName(dir);
            foreach (var file in Directory.EnumerateFiles(dir, "*.3mf", SearchOption.TopDirectoryOnly))
            {
                var model = repo.Get(file);
                var target = DetermineTarget(folder, model, canonical, categories);
                if (target.Target == null)
                {
                    ignored++;
                    continue;
                }

                var destination = GetSafeDestination(target.Target, Path.GetFileName(file));
                if (PathsEqual(file, destination))
                    continue;

                if (target.NeedsReview) review++;
                moves.Add(new ConsolidationMove(file, target.Target, target.Reason, target.Confidence));
            }
        }

        var groups = moves
            .GroupBy(x => (Folder: Path.GetFileName(Path.GetDirectoryName(x.SourcePath)!), x.TargetCategory, x.Confidence, x.Reason))
            .Select(g => new ConsolidationGroup(
                g.Key.Folder,
                g.Key.TargetCategory,
                g.Count(),
                g.Sum(x => SafeLength(x.SourcePath)),
                g.Key.Confidence,
                g.Key.Reason))
            .OrderBy(x => x.SourceFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TargetCategory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConsolidationPlan(moves, groups, review, ignored);
    }

    public ConsolidationResult Apply(ConsolidationPlan plan, bool removeEmptyFolders, bool includeReview)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var warnings = new List<string>();
        var entries = new List<ConsolidationUndoEntry>();
        var removedFolders = new List<string>();
        var moved = 0;
        long bytes = 0;
        var sourceDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan.Moves)
        {
            if (!includeReview && string.Equals(item.Confidence, "Review", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsUnderRoot(item.SourcePath) || string.IsNullOrWhiteSpace(item.TargetCategory))
            {
                warnings.Add($"Skipped unsafe consolidation source: {item.SourcePath}");
                continue;
            }

            if (!File.Exists(item.SourcePath))
            {
                warnings.Add($"Source file no longer exists: {item.SourcePath}");
                continue;
            }

            try
            {
                var source = Path.GetFullPath(item.SourcePath);
                var sourceDir = Path.GetDirectoryName(source)!;
                var targetDir = GetCategoryDirectory(item.TargetCategory);
                Directory.CreateDirectory(targetDir);
                var dest = Unique(Path.Combine(targetDir, Path.GetFileName(source)));

                if (PathsEqual(source, dest))
                    continue;

                var model = repo.Get(source);
                var previousCategory = model?.Category ?? "Uncategorized";
                var previousTags = model?.Tags ?? "";
                var previousCategoryOverride = model?.CategoryOverride ?? false;

                File.Move(source, dest);
                var size = SafeLength(dest);
                moved++;
                bytes += size;
                sourceDirs.Add(sourceDir);
                entries.Add(new ConsolidationUndoEntry(source, dest, previousCategory, previousTags, previousCategoryOverride));

                if (model != null)
                {
                    model.Path = dest;
                    model.Name = Path.GetFileName(dest);
                    if (!model.CategoryOverride)
                        model.Category = item.TargetCategory;
                    model.ModifiedUtc = File.GetLastWriteTimeUtc(dest);
                    repo.RenamePath(source, dest, model);
                    repo.Upsert(model);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(item.SourcePath)}: {ex.Message}");
            }
        }

        if (removeEmptyFolders)
        {
            foreach (var dir in sourceDirs.OrderByDescending(x => x.Length))
            {
                try
                {
                    if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                        continue;
                    Directory.Delete(dir, false);
                    removedFolders.Add(dir);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }

        if (entries.Count > 0)
        {
            var manifest = new ConsolidationUndoManifest(DateTime.UtcNow, root, entries, removedFolders);
            SaveManifest(manifest);
        }

        return new ConsolidationResult(moved, removedFolders.Count, bytes, warnings);
    }

    public bool CanUndo
    {
        get
        {
            var manifest = LoadManifest(out _);
            return manifest != null && PathsEqual(manifest.LibraryRoot, root) && manifest.Entries.Count > 0;
        }
    }

    public ConsolidationResult UndoLast()
    {
        var manifest = LoadManifest(out var loadError);
        if (manifest == null)
            return new(0, 0, 0, new[] { loadError ?? "No consolidation is available to undo." });

        if (!PathsEqual(manifest.LibraryRoot, root))
            return new(0, 0, 0, new[] { "The saved consolidation belongs to a different library root." });

        var warnings = new List<string>();
        var remaining = new List<ConsolidationUndoEntry>();
        var moved = 0;
        long bytes = 0;

        foreach (var entry in manifest.Entries.Reverse())
        {
            if (!File.Exists(entry.NewPath))
            {
                warnings.Add($"Restore source is missing: {entry.NewPath}");
                remaining.Add(entry);
                continue;
            }

            try
            {
                if (!IsUnderRoot(entry.OldPath) || !IsUnderRoot(entry.NewPath))
                {
                    warnings.Add($"Skipped unsafe undo path: {entry.OldPath}");
                    remaining.Add(entry);
                    continue;
                }

                var parent = Path.GetDirectoryName(entry.OldPath)!;
                Directory.CreateDirectory(parent);
                if (File.Exists(entry.OldPath))
                {
                    warnings.Add($"Skipped restore because the original path is occupied: {entry.OldPath}");
                    remaining.Add(entry);
                    continue;
                }

                var model = repo.Get(entry.NewPath);
                File.Move(entry.NewPath, entry.OldPath);
                if (model != null)
                {
                    repo.RenamePath(entry.NewPath, entry.OldPath, model);
                    model.Path = entry.OldPath;
                    model.Name = Path.GetFileName(entry.OldPath);
                    model.Category = entry.PreviousCategory;
                    model.Tags = entry.PreviousTags;
                    model.CategoryOverride = entry.PreviousCategoryOverride;
                    model.ModifiedUtc = File.GetLastWriteTimeUtc(entry.OldPath);
                    repo.Upsert(model);
                }

                moved++;
                bytes += SafeLength(entry.OldPath);
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(entry.NewPath)}: {ex.Message}");
                remaining.Add(entry);
            }
        }

        if (remaining.Count == 0)
        {
            TryDeleteManifest(warnings);
        }
        else
        {
            SaveManifest(manifest with { Entries = remaining });
        }

        return new ConsolidationResult(moved, 0, bytes, warnings);
    }

    private (string? Target, string Reason, string Confidence, bool NeedsReview) DetermineRootFileTarget(
        ModelRecord? model,
        IReadOnlyList<string> categories)
    {
        // A root-level file has no folder taxonomy to resolve. Its persisted
        // classification is the only safe source of truth for automatic cleanup.
        // Manual category overrides remain authoritative.
        var category = model?.Category?.Trim();
        if (string.IsNullOrWhiteSpace(category) ||
            string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "Unknown", StringComparison.OrdinalIgnoreCase))
            return (null, "Root-level file has no resolved category", "Review", true);

        // Resolve through the same canonical alias map used for legacy folders. Do not
        // trust a legacy category merely because it still exists in the user registry.
        var canonical = BuildCanonicalMap(categories);
        if (!canonical.TryGetValue(Normalize(category), out var actual))
            return (null, "Root-level file category is not in the active canonical taxonomy", "Review", true);

        return model?.CategoryOverride == true
            ? (actual, "Preserved category override resolved to canonical taxonomy", "High", false)
            : (actual, "Model intelligence category resolved to canonical taxonomy", "High", false);
    }

    private (string? Target, string Reason, string Confidence, bool NeedsReview) DetermineTarget(
        string folder,
        ModelRecord? model,
        Dictionary<string, string> map,
        IReadOnlyList<string> categories)
    {
        // A user category override is authoritative and always wins over legacy folder aliases.
        if (model?.CategoryOverride == true && categories.Any(c => string.Equals(c, model.Category, StringComparison.OrdinalIgnoreCase)))
            return (model.Category, "Preserved custom category override", "High", false);

        var key = Normalize(folder);
        if (map.TryGetValue(key, out var direct))
            return (direct, "Legacy folder mapped to canonical category", "High", false);

        if (key is "models" or "other" or "testprint" or "testprints")
        {
            if (model != null && !string.IsNullOrWhiteSpace(model.Category) &&
                !string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) &&
                categories.Any(c => string.Equals(c, model.Category, StringComparison.OrdinalIgnoreCase)))
            {
                return (model.Category, "Model intelligence/category used to resolve legacy folder", "Medium", key is "models");
            }

            if (key is "other")
                return (FindCategory(categories, "Uncategorized") ?? "Uncategorized", "Legacy Other folder", "High", false);

            if (key is "testprint" or "testprints")
            {
                var test = FindCategory(categories, "Test Prints");
                return (test ?? "Uncategorized", "Legacy test-print folder", test != null ? "High" : "Review", test == null);
            }

            return (null, "Ambiguous legacy folder requires review", "Review", true);
        }

        return (null, "Unmapped folder left untouched", "Review", true);
    }

    private Dictionary<string, string> BuildCanonicalMap(IReadOnlyList<string> categories)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in categories.Where(x => !string.IsNullOrWhiteSpace(x)))
            map[Normalize(c)] = c;

        void Add(string alias, string category)
        {
            var actual = FindCategory(categories, category);
            if (actual != null) map[Normalize(alias)] = actual;
        }

        Add("02_Functional", "Functional");
        Add("02_Household", "Household");
        Add("03_Automotive", "Vehicles"); Add("07_Automotive", "Vehicles"); Add("Automotive", "Vehicles"); Add("Vehicles", "Vehicles");
        Add("03_Decor", "Art & Decor"); Add("06_Decorative", "Art & Decor"); Add("Art & Decor", "Art & Decor"); Add("Decor", "Art & Decor");
        Add("04_Figures", "Figures & Characters"); Add("Figures", "Figures & Characters"); Add("Figures & Characters", "Figures & Characters");
        Add("05_Game_Models", "Toys & Games"); Add("05_Gaming", "Toys & Games"); Add("Gaming", "Toys & Games"); Add("Toys & Games", "Toys & Games");
        Add("08_Aviation", "Vehicles"); Add("09_Aircraft", "Vehicles"); Add("Aviation", "Vehicles"); Add("Aircraft", "Vehicles");
        Add("07_Multi_Color", "Uncategorized"); Add("10_Multi_Color", "Uncategorized"); Add("Multi_Color", "Uncategorized"); Add("Multi-Color", "Uncategorized");
        Add("08_Test_Print", "Uncategorized"); Add("11_Test_Print", "Uncategorized"); Add("Test_Print", "Uncategorized"); Add("Test Prints", "Uncategorized");
        Add("99_Other", "Uncategorized"); Add("Other", "Uncategorized");
        Add("Hueforge", "HueForge"); Add("HueForge", "HueForge");
        Add("Keychain", "Keychains"); Add("Keychains", "Keychains");
        return map;
    }

    private string GetCategoryDirectory(string category)
    {
        var clean = category.Trim().TrimEnd(' ', '.');
        if (clean.Length == 0 || clean is "." or ".." || clean.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Invalid consolidation category: {category}", nameof(category));
        var path = Path.GetFullPath(Path.Combine(root, clean));
        if (!IsUnderRoot(path))
            throw new InvalidOperationException("Consolidation target escaped the library root.");
        return path;
    }

    private string GetSafeDestination(string category, string fileName)
    {
        var dir = GetCategoryDirectory(category);
        return Path.Combine(dir, fileName);
    }

    private bool IsUnderRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return PathsEqual(full, root) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRoot(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot)) throw new ArgumentException("Library root is required.", nameof(libraryRoot));
        return Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private ConsolidationUndoManifest? LoadManifest(out string? error)
    {
        error = null;
        if (!File.Exists(undoPath))
        {
            error = "No consolidation is available to undo.";
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ConsolidationUndoManifest>(File.ReadAllText(undoPath));
            if (manifest == null || manifest.Entries.Count == 0)
            {
                error = "The consolidation undo record is empty.";
                return null;
            }
            return manifest;
        }
        catch (Exception ex)
        {
            error = $"The consolidation undo record could not be read: {ex.Message}";
            return null;
        }
    }

    private void SaveManifest(ConsolidationUndoManifest manifest)
    {
        var temp = undoPath + ".tmp";
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, undoPath, true);
    }

    private void TryDeleteManifest(List<string> warnings)
    {
        try { if (File.Exists(undoPath)) File.Delete(undoPath); }
        catch (Exception ex) { warnings.Add($"Could not clear the undo record: {ex.Message}"); }
    }

    private static string? FindCategory(IReadOnlyList<string> categories, string wanted) =>
        categories.FirstOrDefault(c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value)
    {
        var s = value.Trim();
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i > 0 && i < s.Length && (s[i] == '_' || s[i] == '-' || s[i] == ' ')) s = s[(i + 1)..];
        s = s.Replace("_", " ").Replace("-", " ").Replace("&", "and");
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var n = 2;
        var candidate = path;
        while (File.Exists(candidate)) candidate = Path.Combine(dir, $"{name} ({n++}){ext}");
        return candidate;
    }
}
