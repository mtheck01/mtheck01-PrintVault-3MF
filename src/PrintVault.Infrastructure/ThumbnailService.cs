using System.IO.Compression;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public static class ThumbnailService
{
    private static readonly string CacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintVault", "thumbnails");

    public static Task<string?> ExtractAsync(string path, CancellationToken ct = default)
        => Task.Run(() => Extract(path, ct), ct);

    private static string? Extract(string path, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(CacheRoot);
            var fi = new FileInfo(path);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks))).ToLowerInvariant();
            var dest = Path.Combine(CacheRoot, key + ".png");
            if (File.Exists(dest)) return dest;

            using var z = ZipFile.OpenRead(path);
            var entry = FindThumbnail(z);
            if (entry is null) return null;
            using var src = entry.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
            ct.ThrowIfCancellationRequested();
            return dest;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public static ZipArchiveEntry? FindThumbnail(ZipArchive z)
    {
        var preferred = new[]
        {
            "Metadata/thumbnail.png", "Metadata/thumbnail.jpg", "Metadata/thumbnail.jpeg",
            "Metadata/plate_1.png", "Metadata/plate_1_small.png", "3D/thumbnail.png", "3D/thumbnail.jpg"
        };
        foreach (var name in preferred)
        {
            var exact = z.GetEntry(name);
            if (exact != null) return exact;
        }
        return z.Entries.FirstOrDefault(e =>
        {
            var n = e.FullName.Replace('\\', '/').ToLowerInvariant();
            return (n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".jpeg")) &&
                   (n.Contains("thumb") || n.Contains("plate_"));
        });
    }
}
