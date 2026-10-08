using System.Collections.Generic;

namespace PrintVault.Infrastructure;

/// <summary>
/// Owns filesystem discovery for the library. Keeping discovery separate from indexing
/// makes traversal, reparse-point handling, and future daily-proofing independently testable.
/// </summary>
public sealed class LibraryFileDiscovery
{
    public IReadOnlyList<string> Discover3MfFiles(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var rootList = roots.Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return rootList
            .SelectMany(SafeEnumerate3Mf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SafeEnumerate3Mf(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                var info = new DirectoryInfo(dir);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 &&
                    !string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch { continue; }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.3mf", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                throw new IOException($"PrintVault could not enumerate 3MF files in '{dir}'. The scan was stopped to prevent an incomplete library from being treated as authoritative.", ex);
            }
            foreach (var file in files) yield return file;

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex)
            {
                throw new IOException($"PrintVault could not enumerate subdirectories in '{dir}'. The scan was stopped to prevent an incomplete library from being treated as authoritative.", ex);
            }
            foreach (var child in dirs) pending.Push(child);
        }
    }
}
