using Microsoft.Data.Sqlite;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed class MigrationService
{
    public string LegacyDbPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3MF Library Manager", "library.db");

    public int ImportLegacy()
    {
        if (!File.Exists(LegacyDbPath)) return 0;
        var target = new LibraryRepository();
        var list = new List<ModelRecord>();
        try
        {
            using var c = new SqliteConnection($"Data Source={LegacyDbPath}"); c.Open();
            using var q = c.CreateCommand(); q.CommandText = "SELECT * FROM Models";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var path = Get(r, "Path"); if (string.IsNullOrWhiteSpace(path)) continue;
                var fi = File.Exists(path) ? new FileInfo(path) : null;
                list.Add(new ModelRecord
                {
                    Path = path, Name = Get(r, "Name") ?? Path.GetFileName(path), Category = Get(r, "Category") ?? "Uncategorized",
                    Size = fi?.Length ?? GetLong(r, "Size"), ModifiedUtc = fi?.LastWriteTimeUtc ?? DateTime.UtcNow,
                    ThumbnailPath = Get(r, "ThumbnailPath"), Favorite = GetLong(r, "Favorite") != 0, Tags = Get(r, "Category") ?? ""
                });
            }
            target.SaveAll(list);
            return list.Count;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Legacy library migration failed. No imported records were committed: {LegacyDbPath}",
                ex);
        }
    }

    private static string? Get(SqliteDataReader r, string name) { try { var i = r.GetOrdinal(name); return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString(); } catch { return null; } }
    private static long GetLong(SqliteDataReader r, string name) { long.TryParse(Get(r, name), out var v); return v; }
}
