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
