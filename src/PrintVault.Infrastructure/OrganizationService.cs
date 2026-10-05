using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed class OrganizationService
{
    private readonly LibraryRepository repo;
    private readonly string root;
    private readonly Stack<MoveResult> undo = new();

    public OrganizationService(LibraryRepository repo, string libraryRoot)
    {
        this.repo = repo;
        root = Path.GetFullPath(libraryRoot);
    }

    public MoveResult MoveToCategory(ModelRecord m, string category)
    {
        if (!Directory.Exists(root)) throw new InvalidOperationException("Library root not found.");
        category = NormalizeCategory(category);
        var dir = Path.Combine(root, category);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, m.Name);
        if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(m.Path), StringComparison.OrdinalIgnoreCase))
            return new(m.Path, m.Path, m, m.Category, m.Tags);

        if (File.Exists(dest)) dest = Unique(dest);
        var old = m.Path;
        var previousCategory = m.Category;
        var previousTags = m.Tags;

        File.Move(old, dest);
        m.Path = dest;
        m.Name = Path.GetFileName(dest);
        m.Category = category;
        m.Tags = ReplaceCategoryTag(m.Tags, previousCategory, category);
        m.ModifiedUtc = File.GetLastWriteTimeUtc(dest);
        repo.RenamePath(old, dest, m);
        repo.Upsert(m);

        var result = new MoveResult(old, dest, m, previousCategory, previousTags);
        lock (undo) undo.Push(result);
        return result;
    }

    public int OrganizeClassifiedFiles()
    {
        if (!Directory.Exists(root)) throw new InvalidOperationException("Library root not found.");
        var moved = 0;
        foreach (var model in repo.GetAll().ToList())
        {
            if (string.IsNullOrWhiteSpace(model.Path) || !File.Exists(model.Path)) continue;
            if (string.IsNullOrWhiteSpace(model.Category) ||
                string.Equals(model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Category, "Unknown", StringComparison.OrdinalIgnoreCase)) continue;

            var source = Path.GetFullPath(model.Path);
            if (!source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var category = NormalizeCategory(model.Category);
            var targetDir = Path.Combine(root, category);
            Directory.CreateDirectory(targetDir);
            var target = Path.Combine(targetDir, Path.GetFileName(source));
            if (string.Equals(source, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(target)) target = Unique(target);

            File.Move(source, target);
            model.Path = target;
            model.Name = Path.GetFileName(target);
            model.ModifiedUtc = File.GetLastWriteTimeUtc(target);
            repo.RenamePath(source, target, model);
            repo.Upsert(model);
            moved++;
        }
        return moved;
    }


    public TaxonomyCleanupResult CleanupLegacyTaxonomy()
    {
        if (!Directory.Exists(root)) throw new InvalidOperationException("Library root not found.");

        var legacy = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "06_Cosplay", "08_Aviation", "09_Models", "10_Multi_Color", "Soap Holders", "test eng 8.6"
        };

        var records = repo.GetAll().ToList();
        var legacyCategories = 0;
        var legacyTags = 0;
        var normalizedRecords = 0;

        foreach (var model in records)
        {
            var categoryIsLegacy = !string.IsNullOrWhiteSpace(model.Category) && legacy.Contains(model.Category.Trim());
            if (categoryIsLegacy)
            {
                legacyCategories++;
                model.Category = "Uncategorized";
                normalizedRecords++;
            }

            var tags = model.Tags
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag =>
                {
                    if (legacy.Contains(tag))
                    {
                        legacyTags++;
                        return false;
                    }
                    return true;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            model.Tags = string.Join(", ", tags);
            if (File.Exists(model.Path))
                model.ModifiedUtc = File.GetLastWriteTimeUtc(model.Path);
            repo.Upsert(model);
        }

        // Move every confidently classified record after legacy categories have
        // been neutralized. Uncategorized/Unknown records stay at the root.
        var moved = OrganizeClassifiedFiles();

        var knownLegacyFoldersRemaining = 0;
        foreach (var name in legacy)
        {
            var path = Path.Combine(root, name);
            if (!Directory.Exists(path)) continue;

            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                knownLegacyFoldersRemaining++;
                continue;
            }

            Directory.Delete(path, false);
        }

        var remainingLegacyFolders = legacy.Count(name =>
        {
            var path = Path.Combine(root, name);
            return Directory.Exists(path);
        });

        var remainingLegacyCategories = repo.GetAll().Count(m =>
            !string.IsNullOrWhiteSpace(m.Category) && legacy.Contains(m.Category.Trim()));

        var remainingLegacyTags = repo.GetAll().Sum(m =>
            m.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Count(tag => legacy.Contains(tag)));

        return new TaxonomyCleanupResult(
            legacyCategories,
            legacyTags,
            normalizedRecords,
            moved,
            remainingLegacyCategories,
            remainingLegacyTags,
            remainingLegacyFolders);
    }

    public MoveResult? UndoLast()
    {
        lock (undo)
        {
            if (undo.Count == 0) return null;
            var r = undo.Pop();
            if (!File.Exists(r.NewPath)) return null;

            var dir = Path.GetDirectoryName(r.OldPath)!;
            Directory.CreateDirectory(dir);
            var dest = File.Exists(r.OldPath) ? Unique(r.OldPath) : r.OldPath;
            File.Move(r.NewPath, dest);

            r.Model.Path = dest;
            r.Model.Name = Path.GetFileName(dest);
            r.Model.Category = r.PreviousCategory;
            r.Model.Tags = r.PreviousTags;
            r.Model.ModifiedUtc = File.GetLastWriteTimeUtc(dest);
            repo.RenamePath(r.NewPath, dest, r.Model);
            repo.Upsert(r.Model);
            return new MoveResult(r.NewPath, dest, r.Model, r.Model.Category, r.Model.Tags);
        }
    }

    private static string ReplaceCategoryTag(string tags, string previousCategory, string category)
    {
        var values = tags
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, previousCategory, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x, category, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        values.Insert(0, category);
        return string.Join(", ", values);
    }

    private static string NormalizeCategory(string value)
    {
        var category = value.Trim();
        if (category.Length == 0) throw new ArgumentException("Category cannot be empty.", nameof(value));
        if (category is "." or "..") throw new ArgumentException("Invalid category name.", nameof(value));
        if (category.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Category contains characters that Windows does not allow in folder names.", nameof(value));
        category = category.TrimEnd(' ', '.');
        if (category.Length == 0) throw new ArgumentException("Invalid category name.", nameof(value));
        return category;
    }

    private static string Unique(string p)
    {
        var d = Path.GetDirectoryName(p)!;
        var n = Path.GetFileNameWithoutExtension(p);
        var e = Path.GetExtension(p);
        var i = 2;
        var x = p;
        while (File.Exists(x)) x = Path.Combine(d, $"{n} ({i++}){e}");
        return x;
    }
}

public sealed record TaxonomyCleanupResult(
    int LegacyCategoriesFound,
    int LegacyTagsRemoved,
    int RecordsNormalized,
    int FilesMoved,
    int LegacyCategoriesRemaining,
    int LegacyTagsRemaining,
    int LegacyFoldersRemaining);
