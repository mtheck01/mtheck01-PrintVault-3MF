using System.Text.Json;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record CategoryReconciliationItem(
    string SourceCategory,
    string TargetCategory,
    int FileCount,
    int ProtectedCount,
    string Status,
    string Reason);

public sealed record CategoryReconciliationPlan(
    IReadOnlyList<CategoryReconciliationItem> Items,
    int LegacyUsedCount,
    int EmptyLegacyCount,
    int ReviewCount,
    int ProtectedCount);

public sealed record CategoryReconciliationResult(
    int FilesReclassified,
    int CategoriesRetired,
    int ProtectedFiles,
    IReadOnlyList<string> Warnings);

public sealed record CategoryReconciliationUndoEntry(
    string Path,
    string PreviousCategory,
    string PreviousTags,
    bool PreviousCategoryOverride);

public sealed record CategoryReconciliationUndoManifest(
    DateTime CreatedUtc,
    IReadOnlyList<CategoryReconciliationUndoEntry> Entries);

/// <summary>
/// Reconciles legacy category names left behind by older PrintVault versions.
/// This operates on the catalog as well as the category registry; it is deliberately
/// separate from filesystem consolidation so the two states cannot drift apart.
/// </summary>
public sealed class CategoryReconciliationService
{
    private readonly LibraryRepository repo;
    private readonly string undoPath;

    public CategoryReconciliationService(LibraryRepository repository)
    {
        repo = repository ?? throw new ArgumentNullException(nameof(repository));
        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault");
        Directory.CreateDirectory(app);
        undoPath = Path.Combine(app, "last_category_reconciliation.json");
    }

    public CategoryReconciliationPlan Preview(IReadOnlyList<string> categories)
    {
        var all = repo.GetAll();
        var counts = all.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var protectedCounts = all.Where(x => x.CategoryOverride)
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var items = new List<CategoryReconciliationItem>();
        var legacyUsed = 0;
        var empty = 0;
        var review = 0;
        var protectedFiles = 0;

        foreach (var source in categories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (BuiltInCategories.All.Contains(source, StringComparer.OrdinalIgnoreCase)) continue;
            var count = counts.TryGetValue(source, out var c) ? c : 0;
            var protectedCount = protectedCounts.TryGetValue(source, out var pc) ? pc : 0;
            var target = PreferredTarget(source);

            if (target != null && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                var status = count == 0 ? "Empty legacy" : protectedCount == count ? "Protected" : "Consolidate";
                var reason = count == 0
                    ? "Legacy category has no catalog records and can be retired."
                    : protectedCount > 0
                        ? $"{protectedCount:N0} file(s) have an explicit category override and will be preserved."
                        : "Legacy category name maps directly to the canonical category.";
                items.Add(new CategoryReconciliationItem(source, target, count, protectedCount, status, reason));
                if (count == 0) empty++;
                else if (protectedCount == count) protectedFiles += count;
                else legacyUsed++;
                continue;
            }

            if (LooksLikeLegacy(source))
            {
                var status = count == 0 ? "Empty legacy" : "Review";
                var reason = count == 0
                    ? "Legacy category has no files; it can be retired safely."
                    : "Legacy category is ambiguous; individual files need review before reassignment.";
                items.Add(new CategoryReconciliationItem(source, "", count, protectedCount, status, reason));
                if (count == 0) empty++; else review++;
                protectedFiles += protectedCount;
            }
        }

        return new CategoryReconciliationPlan(items, legacyUsed, empty, review, protectedFiles);
    }

    public CategoryReconciliationResult Apply(
        CategoryReconciliationPlan plan,
        IReadOnlySet<string> selectedSources,
        Action<string>? retireCategory = null)
    {
        var warnings = new List<string>();
        var undo = new List<CategoryReconciliationUndoEntry>();
        var candidates = repo.GetAll().ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        var retired = 0;
        var protectedFiles = 0;

        foreach (var item in plan.Items)
        {
            if (!selectedSources.Contains(item.SourceCategory)) continue;

            if (item.FileCount == 0)
            {
                retired++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.TargetCategory))
            {
                warnings.Add($"{item.SourceCategory}: no safe canonical target was found; left unchanged.");
                continue;
            }

            var models = candidates.Values
                .Where(x => string.Equals(x.Category, item.SourceCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var model in models)
            {
                if (model.CategoryOverride)
                {
                    protectedFiles++;
                    continue;
                }

                undo.Add(new CategoryReconciliationUndoEntry(model.Path, model.Category, model.Tags, model.CategoryOverride));
                model.Category = item.TargetCategory;
                model.Tags = ReplaceCategoryTag(model.Tags, item.SourceCategory, item.TargetCategory);
                changed++;
            }

            var remainingInCandidate = candidates.Values.Any(x =>
                string.Equals(x.Category, item.SourceCategory, StringComparison.OrdinalIgnoreCase));
            if (!remainingInCandidate)
                retired++;
        }

        // Persist the complete candidate set in one SQLite transaction. Never leave a
        // partially reconciled taxonomy behind if one record write fails.
        if (undo.Count > 0)
        {
            SaveManifest(new CategoryReconciliationUndoManifest(DateTime.UtcNow, undo));
            try
            {
                repo.SaveAll(candidates.Values, new HashSet<string>(candidates.Keys, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // Keep the manifest as a recovery record; the database transaction did
                // not commit, so UndoLast remains safe if the caller retries.
                throw;
            }
        }

        if (retireCategory != null)
        {
            foreach (var item in plan.Items.Where(x => selectedSources.Contains(x.SourceCategory)))
            {
                var remaining = repo.GetAll().Any(x => string.Equals(x.Category, item.SourceCategory, StringComparison.OrdinalIgnoreCase));
                if (!remaining)
                {
                    try { retireCategory(item.SourceCategory); }
                    catch (Exception ex) { warnings.Add($"Could not retire category '{item.SourceCategory}' from the registry: {ex.Message}"); }
                }
            }
        }

        return new CategoryReconciliationResult(changed, retired, protectedFiles, warnings);
    }

    public bool CanUndo => File.Exists(undoPath);

    public CategoryReconciliationResult UndoLast()
    {
        var warnings = new List<string>();
        var manifest = LoadManifest(out var error);
        if (manifest == null) return new(0, 0, 0, new[] { error ?? "No category reconciliation is available to undo." });

        var candidates = repo.GetAll().ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var entry in manifest.Entries.Reverse())
        {
            if (!candidates.TryGetValue(entry.Path, out var model))
            {
                warnings.Add($"Model no longer exists in the catalog: {entry.Path}");
                continue;
            }

            model.Category = entry.PreviousCategory;
            model.Tags = entry.PreviousTags;
            model.CategoryOverride = entry.PreviousCategoryOverride;
            changed++;
        }

        if (changed > 0)
            repo.SaveAll(candidates.Values, new HashSet<string>(candidates.Keys, StringComparer.OrdinalIgnoreCase));

        if (warnings.Count == 0) TryDeleteManifest(warnings);
        return new CategoryReconciliationResult(changed, 0, 0, warnings);
    }

    public static string? PreferredTarget(string category)
    {
        return Normalize(category) switch
        {
            "functional" => "Functional",
            "household" => "Household",
            "automotive" => "Vehicles",
            "decor" => "Art & Decor",
            "decorative" => "Art & Decor",
            "figures" => "Figures & Characters",
            "gaming" => "Toys & Games",
            "gamemodels" => "Toys & Games",
            "cosplay" => "Figures & Characters",
            "multicolor" => "Uncategorized",
            "aviation" => "Vehicles",
            "aircraft" => "Vehicles",
            "testprint" => "Uncategorized",
            "testprints" => "Uncategorized",
            "other" => "Uncategorized",
            "needsreview" => null,
            _ => null
        };
    }

    public static bool LooksLikeLegacy(string category)
    {
        var n = Normalize(category);
        return n is "needsreview" or "functional" or "household" or "automotive" or "decor" or "decorative" or "figures" or "gaming" or "gamemodels" or "cosplay" or "multicolor" or "aviation" or "aircraft" or "testprint" or "testprints" or "other" || (category.Length > 2 && char.IsDigit(category[0]) && category.Contains('_'));
    }

    private static string ReplaceCategoryTag(string tags, string oldCategory, string newCategory)
    {
        var values = (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, oldCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!values.Any(x => string.Equals(x, newCategory, StringComparison.OrdinalIgnoreCase))) values.Insert(0, newCategory);
        return string.Join(", ", values.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        var s = value.Trim();
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i > 0 && i < s.Length && (s[i] == '_' || s[i] == '-' || s[i] == ' ')) s = s[(i + 1)..];
        s = s.Replace("_", " ").Replace("-", " ").Replace("&", "and");
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private CategoryReconciliationUndoManifest? LoadManifest(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(undoPath)) { error = "No category reconciliation is available to undo."; return null; }
            var manifest = JsonSerializer.Deserialize<CategoryReconciliationUndoManifest>(File.ReadAllText(undoPath));
            if (manifest == null || manifest.Entries.Count == 0) { error = "The category reconciliation undo record is empty."; return null; }
            return manifest;
        }
        catch (Exception ex) { error = $"The category reconciliation undo record could not be read: {ex.Message}"; return null; }
    }

    private void SaveManifest(CategoryReconciliationUndoManifest manifest)
    {
        var temp = undoPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, undoPath, true);
    }

    private void TryDeleteManifest(List<string> warnings)
    {
        try { if (File.Exists(undoPath)) File.Delete(undoPath); }
        catch (Exception ex) { warnings.Add($"Could not clear the category reconciliation undo record: {ex.Message}"); }
    }
}
