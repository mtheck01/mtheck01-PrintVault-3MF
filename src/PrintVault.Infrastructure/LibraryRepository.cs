using Microsoft.Data.Sqlite;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed class LibraryRepository
{
    private const int CurrentSchemaVersion = 5;
    private readonly string db;
    private readonly object gate = new();

    public string DatabasePath => db;

    public LibraryRepository() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "library.db")) { }

    public LibraryRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        db = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(db);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        Initialize();
    }

    /// <summary>Creates a consistent SQLite snapshot without changing the source database.</summary>
    public void CreateSnapshot(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(destination)) File.Delete(destination);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        cmd.Parameters.AddWithValue("$path", destination);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={db};Cache=Shared;Mode=ReadWriteCreate");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        Execute(c, tx, @"
CREATE TABLE IF NOT EXISTS Models(
 Id INTEGER PRIMARY KEY,
 Path TEXT NOT NULL UNIQUE,
 Name TEXT NOT NULL,
 Category TEXT NOT NULL DEFAULT 'Uncategorized',
 Size INTEGER NOT NULL DEFAULT 0,
 ModifiedUtc TEXT NOT NULL DEFAULT '',
 ThumbnailPath TEXT,
 Favorite INTEGER NOT NULL DEFAULT 0,
 Tags TEXT DEFAULT '',
 Family TEXT DEFAULT '',
 Slicer TEXT DEFAULT '',
 Materials TEXT DEFAULT '',
 ObjectCount INTEGER DEFAULT 0,
 Dimensions TEXT DEFAULT '',
 IntelligenceScore REAL DEFAULT 0,
 PrintReady INTEGER DEFAULT 0,
 DuplicateGroup TEXT DEFAULT '',
 Hash TEXT DEFAULT '',
 SemanticType TEXT DEFAULT '',
 Subtype TEXT DEFAULT '',
 SuggestedTags TEXT DEFAULT '',
 IntelligenceReason TEXT DEFAULT '',
 RiskFlags TEXT DEFAULT '',
 PrintMethod TEXT DEFAULT 'Unknown',
 PrintMethodConfidence REAL DEFAULT 0,
 PrintMethodEvidence TEXT DEFAULT '',
 SpecialType TEXT DEFAULT '',
 CategoryOverride INTEGER DEFAULT 0,
 PrintMethodOverride INTEGER DEFAULT 0,
 OriginalLanguage TEXT DEFAULT 'Unknown',
 TranslatedTitle TEXT DEFAULT '',
 TranslationConfidence INTEGER DEFAULT 0,
 TranslationEvidence TEXT DEFAULT ''
);");

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA table_info(Models);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1));
        }

        var migrations = new (string Name, string Definition)[]
        {
            ("Path", "TEXT NOT NULL DEFAULT ''"),
            ("Name", "TEXT NOT NULL DEFAULT ''"),
            ("Category", "TEXT NOT NULL DEFAULT 'Uncategorized'"),
            ("Size", "INTEGER NOT NULL DEFAULT 0"),
            ("ModifiedUtc", "TEXT NOT NULL DEFAULT ''"),
            ("ThumbnailPath", "TEXT"),
            ("Favorite", "INTEGER NOT NULL DEFAULT 0"),
            ("Tags", "TEXT DEFAULT ''"),
            ("Family", "TEXT DEFAULT ''"),
            ("Slicer", "TEXT DEFAULT ''"),
            ("Materials", "TEXT DEFAULT ''"),
            ("ObjectCount", "INTEGER DEFAULT 0"),
            ("Dimensions", "TEXT DEFAULT ''"),
            ("IntelligenceScore", "REAL DEFAULT 0"),
            ("PrintReady", "INTEGER DEFAULT 0"),
            ("DuplicateGroup", "TEXT DEFAULT ''"),
            ("Hash", "TEXT DEFAULT ''"),
            ("SemanticType", "TEXT DEFAULT ''"),
            ("Subtype", "TEXT DEFAULT ''"),
            ("SuggestedTags", "TEXT DEFAULT ''"),
            ("IntelligenceReason", "TEXT DEFAULT ''"),
            ("RiskFlags", "TEXT DEFAULT ''"),
            ("PrintMethod", "TEXT DEFAULT 'Unknown'"),
            ("PrintMethodConfidence", "REAL DEFAULT 0"),
            ("PrintMethodEvidence", "TEXT DEFAULT ''"),
            ("SpecialType", "TEXT DEFAULT ''"),
            ("CategoryOverride", "INTEGER DEFAULT 0"),
            ("PrintMethodOverride", "INTEGER DEFAULT 0"),
            ("OriginalLanguage", "TEXT DEFAULT 'Unknown'"),
            ("TranslatedTitle", "TEXT DEFAULT ''"),
            ("TranslationConfidence", "INTEGER DEFAULT 0"),
            ("TranslationEvidence", "TEXT DEFAULT ''")
        };

        foreach (var (name, definition) in migrations)
        {
            if (columns.Contains(name)) continue;
            using var add = c.CreateCommand();
            add.Transaction = tx;
            add.CommandText = $"ALTER TABLE Models ADD COLUMN {name} {definition};";
            add.ExecuteNonQuery();
            columns.Add(name);
        }

        Execute(c, tx, @"
CREATE INDEX IF NOT EXISTS IX_Models_Category ON Models(Category);
CREATE INDEX IF NOT EXISTS IX_Models_Hash ON Models(Hash);
CREATE INDEX IF NOT EXISTS IX_Models_Name ON Models(Name);");

        Execute(c, tx, @"UPDATE Models SET TranslationConfidence=0 WHERE TranslationConfidence IS NULL OR TRIM(CAST(TranslationConfidence AS TEXT))='';");
        Execute(c, tx, $"PRAGMA user_version={CurrentSchemaVersion};");
        tx.Commit();
    }

    private static void Execute(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string SelectColumns = "Id,Path,Name,Category,Size,ModifiedUtc,ThumbnailPath,Favorite,Tags,Family,Slicer,Materials,ObjectCount,Dimensions,IntelligenceScore,PrintReady,DuplicateGroup,Hash,SemanticType,Subtype,SuggestedTags,IntelligenceReason,RiskFlags,PrintMethod,PrintMethodConfidence,PrintMethodEvidence,SpecialType,CategoryOverride,PrintMethodOverride,OriginalLanguage,TranslatedTitle,TranslationConfidence,TranslationEvidence";

    public IReadOnlyDictionary<string, ModelRecord> GetAllMap()
    {
        var result = new Dictionary<string, ModelRecord>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var x = c.CreateCommand();
        x.CommandText = $"SELECT {SelectColumns} FROM Models";
        using var r = x.ExecuteReader();
        while (r.Read())
        {
            var m = Read(r);
            if (!string.IsNullOrWhiteSpace(m.Path)) result[m.Path] = m;
        }
        return result;
    }

    public IReadOnlyList<ModelRecord> GetAll() => GetAllMap().Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public ModelRecord? Get(string path)
    {
        using var c = Open();
        using var x = c.CreateCommand();
        x.CommandText = $"SELECT {SelectColumns} FROM Models WHERE Path=$p";
        x.Parameters.AddWithValue("$p", path);
        using var r = x.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public void SaveAll(IEnumerable<ModelRecord> models, ISet<string>? existingPaths = null)
    {
        var snapshot = models.Where(m => !string.IsNullOrWhiteSpace(m.Path)).ToList();
        lock (gate)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            using var x = c.CreateCommand();
            x.Transaction = tx;
            x.CommandText = @"
INSERT INTO Models(Path,Name,Category,Size,ModifiedUtc,ThumbnailPath,Favorite,Tags,Family,Slicer,Materials,ObjectCount,Dimensions,IntelligenceScore,PrintReady,DuplicateGroup,Hash,SemanticType,Subtype,SuggestedTags,IntelligenceReason,RiskFlags,PrintMethod,PrintMethodConfidence,PrintMethodEvidence,SpecialType,CategoryOverride,PrintMethodOverride,OriginalLanguage,TranslatedTitle,TranslationConfidence,TranslationEvidence)
VALUES($p,$n,$c,$s,$d,$t,$f,$tags,$fam,$sl,$mat,$oc,$dim,$score,$ready,$dup,$hash,$stype,$subtype,$suggested,$reason,$risk,$pm,$pmc,$pme,$special,$co,$pmo,$lang,$translated,$tc,$te)
ON CONFLICT(Path) DO UPDATE SET
Name=$n,Category=$c,Size=$s,ModifiedUtc=$d,ThumbnailPath=$t,Favorite=$f,Tags=$tags,Family=$fam,Slicer=$sl,Materials=$mat,ObjectCount=$oc,Dimensions=$dim,IntelligenceScore=$score,PrintReady=$ready,DuplicateGroup=$dup,Hash=$hash,SemanticType=$stype,Subtype=$subtype,SuggestedTags=$suggested,IntelligenceReason=$reason,RiskFlags=$risk,PrintMethod=$pm,PrintMethodConfidence=$pmc,PrintMethodEvidence=$pme,SpecialType=$special,CategoryOverride=$co,PrintMethodOverride=$pmo,OriginalLanguage=$lang,TranslatedTitle=$translated,TranslationConfidence=$tc,TranslationEvidence=$te;";

            var lang = x.Parameters.Add("$lang", SqliteType.Text); var translated = x.Parameters.Add("$translated", SqliteType.Text); var tc = x.Parameters.Add("$tc", SqliteType.Integer); var te = x.Parameters.Add("$te", SqliteType.Text);
            var p = x.Parameters.Add("$p", SqliteType.Text);
            var n = x.Parameters.Add("$n", SqliteType.Text);
            var cat = x.Parameters.Add("$c", SqliteType.Text);
            var size = x.Parameters.Add("$s", SqliteType.Integer);
            var date = x.Parameters.Add("$d", SqliteType.Text);
            var thumb = x.Parameters.Add("$t", SqliteType.Text);
            var fav = x.Parameters.Add("$f", SqliteType.Integer);
            var tags = x.Parameters.Add("$tags", SqliteType.Text);
            var fam = x.Parameters.Add("$fam", SqliteType.Text);
            var sl = x.Parameters.Add("$sl", SqliteType.Text);
            var mat = x.Parameters.Add("$mat", SqliteType.Text);
            var oc = x.Parameters.Add("$oc", SqliteType.Integer);
            var dim = x.Parameters.Add("$dim", SqliteType.Text);
            var score = x.Parameters.Add("$score", SqliteType.Real);
            var ready = x.Parameters.Add("$ready", SqliteType.Integer);
            var dup = x.Parameters.Add("$dup", SqliteType.Text);
            var hash = x.Parameters.Add("$hash", SqliteType.Text);
            var stype=x.Parameters.Add("$stype",SqliteType.Text); var subtype=x.Parameters.Add("$subtype",SqliteType.Text); var suggested=x.Parameters.Add("$suggested",SqliteType.Text); var reason=x.Parameters.Add("$reason",SqliteType.Text); var risk=x.Parameters.Add("$risk",SqliteType.Text); var pm=x.Parameters.Add("$pm",SqliteType.Text); var pmc=x.Parameters.Add("$pmc",SqliteType.Real); var pme=x.Parameters.Add("$pme",SqliteType.Text); var special=x.Parameters.Add("$special",SqliteType.Text); var co=x.Parameters.Add("$co",SqliteType.Integer); var pmo=x.Parameters.Add("$pmo",SqliteType.Integer);

            foreach (var m in snapshot)
            {
                p.Value = m.Path; n.Value = m.Name; cat.Value = string.IsNullOrWhiteSpace(m.Category) ? "Uncategorized" : m.Category; size.Value = m.Size;
                date.Value = m.ModifiedUtc.ToString("O"); thumb.Value = (object?)m.ThumbnailPath ?? DBNull.Value;
                fav.Value = m.Favorite ? 1 : 0; tags.Value = m.Tags ?? ""; fam.Value = m.Family ?? ""; sl.Value = m.Slicer ?? "";
                mat.Value = m.Materials ?? ""; oc.Value = m.ObjectCount; dim.Value = m.Dimensions ?? ""; score.Value = m.IntelligenceScore;
                ready.Value = m.PrintReady ? 1 : 0; dup.Value = m.DuplicateGroup ?? ""; hash.Value = m.Hash ?? ""; stype.Value=m.SemanticType??""; subtype.Value=m.Subtype??""; suggested.Value=m.SuggestedTags??""; reason.Value=m.IntelligenceReason??""; risk.Value=m.RiskFlags??""; pm.Value=m.PrintMethod??"Unknown"; pmc.Value=m.PrintMethodConfidence; pme.Value=m.PrintMethodEvidence??""; special.Value=m.SpecialType??""; co.Value=m.CategoryOverride?1:0; pmo.Value=m.PrintMethodOverride?1:0; lang.Value=m.OriginalLanguage??"Unknown"; translated.Value=m.TranslatedTitle??""; tc.Value=m.TranslationConfidence; te.Value=m.TranslationEvidence??"";
                x.ExecuteNonQuery();
            }

            if (existingPaths is not null)
            {
                using var delete = c.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM Models WHERE Path=$p";
                var oldPath = delete.Parameters.Add("$p", SqliteType.Text);
                foreach (var old in existingPaths.Where(p0 => !snapshot.Any(m => string.Equals(m.Path, p0, StringComparison.OrdinalIgnoreCase))))
                {
                    oldPath.Value = old;
                    delete.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
    }

    /// <summary>Creates a byte-for-byte backup of the live database before a destructive library reset.</summary>
    public string BackupDatabase(string? destinationPath = null)
    {
        var destination = string.IsNullOrWhiteSpace(destinationPath)
            ? Path.Combine(Path.GetDirectoryName(db) ?? Environment.CurrentDirectory,
                $"library.backup.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db")
            : Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        lock (gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path;";
            cmd.Parameters.AddWithValue("$path", destination);
            cmd.ExecuteNonQuery();
        }
        return destination;
    }

    public void ClearAllModels()
    {
        lock (gate)
        {
            using var c = Open();
            using var x = c.CreateCommand();
            x.CommandText = "DELETE FROM Models";
            x.ExecuteNonQuery();
        }
    }

    public void Upsert(ModelRecord m) => SaveAll(new[] { m });

    public void Delete(string path)
    {
        lock (gate)
        {
            using var c = Open();
            using var x = c.CreateCommand();
            x.CommandText = "DELETE FROM Models WHERE Path=$p";
            x.Parameters.AddWithValue("$p", path);
            x.ExecuteNonQuery();
        }
    }

    public void RenamePath(string oldPath, string newPath, ModelRecord m)
    {
        lock (gate)
        {
            using var c = Open();
            using var x = c.CreateCommand();
            x.CommandText = "UPDATE Models SET Path=$new,Name=$name,ModifiedUtc=$date WHERE Path=$old";
            x.Parameters.AddWithValue("$new", newPath);
            x.Parameters.AddWithValue("$name", m.Name);
            x.Parameters.AddWithValue("$date", m.ModifiedUtc.ToString("O"));
            x.Parameters.AddWithValue("$old", oldPath);
            x.ExecuteNonQuery();
        }
    }

    private static ModelRecord Read(SqliteDataReader r)
    {
        var modifiedText = r.IsDBNull(5) ? "" : Convert.ToString(r.GetValue(5)) ?? "";
        var modified = DateTime.TryParse(modifiedText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed : DateTime.UtcNow;
        return new ModelRecord
        {
            Id = ToInt64(r, 0), Path = Text(r, 1), Name = Text(r, 2),
            Category = string.IsNullOrWhiteSpace(Text(r, 3)) ? "Uncategorized" : Text(r, 3), Size = ToInt64(r, 4),
            ModifiedUtc = modified, ThumbnailPath = r.IsDBNull(6) ? null : Text(r, 6), Favorite = ToInt64(r, 7) != 0,
            Tags = Text(r, 8), Family = Text(r, 9), Slicer = Text(r, 10),
            Materials = Text(r, 11), ObjectCount = ToInt32(r, 12),
            Dimensions = Text(r, 13), IntelligenceScore = ToDouble(r, 14),
            PrintReady = ToInt64(r, 15) != 0, DuplicateGroup = Text(r, 16), Hash = Text(r, 17), SemanticType=Text(r, 18), Subtype=Text(r, 19), SuggestedTags=Text(r, 20), IntelligenceReason=Text(r, 21), RiskFlags=Text(r, 22), PrintMethod=string.IsNullOrWhiteSpace(Text(r, 23))?"Unknown":Text(r, 23), PrintMethodConfidence=ToDouble(r, 24), PrintMethodEvidence=Text(r, 25), SpecialType=Text(r, 26), CategoryOverride=ToInt64(r, 27)!=0, PrintMethodOverride=ToInt64(r, 28)!=0,
            OriginalLanguage=string.IsNullOrWhiteSpace(Text(r, 29))?"Unknown":Text(r, 29), TranslatedTitle=Text(r, 30), TranslationConfidence=ToInt32(r, 31), TranslationEvidence=Text(r, 32)
        };
    }

    private static string Text(SqliteDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? "" : Convert.ToString(r.GetValue(ordinal)) ?? "";

    private static long ToInt64(SqliteDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal)) return 0;
        var value = r.GetValue(ordinal);
        return value switch
        {
            long l => l, int i => i, short s => s, byte b => b, double d => (long)d, float f => (long)f, decimal m => (long)m,
            _ => long.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0
        };
    }

    private static int ToInt32(SqliteDataReader r, int ordinal)
    {
        var value = ToInt64(r, ordinal);
        return value is > int.MaxValue or < int.MinValue ? 0 : (int)value;
    }

    private static double ToDouble(SqliteDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal)) return 0;
        var value = r.GetValue(ordinal);
        return value switch
        {
            double d => d, float f => f, decimal m => (double)m, long l => l, int i => i,
            _ => double.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0
        };
    }
}
