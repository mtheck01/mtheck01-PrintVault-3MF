using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PrintVault.Infrastructure;

/// <summary>Persists user-defined category names independently of the UI.</summary>
public sealed class CategoryRegistryStore
{
    private readonly string path;

    public CategoryRegistryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrintVault",
            "categories.txt"))
    {
    }

    public CategoryRegistryStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A category registry path is required.", nameof(path));

        this.path = Path.GetFullPath(path);
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(path)) return Array.Empty<string>();

        try
        {
            return File.ReadAllLines(path)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception readError)
        {
            var recoveryPath = path + $".corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.txt";
            try
            {
                File.Move(path, recoveryPath, false);
            }
            catch (Exception moveError)
            {
                throw new InvalidDataException(
                    "PrintVault category registry is unreadable and could not be preserved for recovery.",
                    new AggregateException(readError, moveError));
            }

            return Array.Empty<string>();
        }
    }

    public void Save(IEnumerable<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporaryPath, categories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public int Remove(IReadOnlySet<string> categoriesToRemove)
    {
        ArgumentNullException.ThrowIfNull(categoriesToRemove);
        if (!File.Exists(path)) return 0;

        var lines = File.ReadAllLines(path);
        var removed = lines.Count(line => categoriesToRemove.Contains(line.Trim()));
        Save(lines.Where(line => !categoriesToRemove.Contains(line.Trim())));
        return removed;
    }

    public int Count(IReadOnlySet<string> categoriesToCount)
    {
        ArgumentNullException.ThrowIfNull(categoriesToCount);
        if (!File.Exists(path)) return 0;
        return File.ReadAllLines(path).Count(line => categoriesToCount.Contains(line.Trim()));
    }
}
