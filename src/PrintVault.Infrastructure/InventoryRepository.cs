using Microsoft.Data.Sqlite;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>Persists physical stock separately from the digital model catalog.</summary>
public sealed class InventoryRepository
{
    private readonly string databasePath;
    private readonly object gate = new();

    public InventoryRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        this.databasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(this.databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        Initialize();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, @"
CREATE TABLE IF NOT EXISTS PrintedStockItems(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LinkedModelId INTEGER NULL,
    ModelName TEXT NOT NULL,
    Variant TEXT NOT NULL DEFAULT '',
    Location TEXT NOT NULL DEFAULT '',
    Condition TEXT NOT NULL DEFAULT 'Good',
    Quantity INTEGER NOT NULL DEFAULT 0 CHECK(Quantity >= 0),
    LowStockThreshold INTEGER NOT NULL DEFAULT 0 CHECK(LowStockThreshold >= 0),
    Notes TEXT NOT NULL DEFAULT '',
    UpdatedUtc TEXT NOT NULL,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(LinkedModelId) REFERENCES Models(Id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS IX_PrintedStockItems_Active ON PrintedStockItems(IsArchived, ModelName COLLATE NOCASE);
CREATE TABLE IF NOT EXISTS PrintedStockMovements(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    StockItemId INTEGER NOT NULL,
    QuantityDelta INTEGER NOT NULL CHECK(QuantityDelta <> 0),
    QuantityAfter INTEGER NOT NULL CHECK(QuantityAfter >= 0),
    Reason TEXT NOT NULL,
    OccurredUtc TEXT NOT NULL,
    FOREIGN KEY(StockItemId) REFERENCES PrintedStockItems(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_PrintedStockMovements_Item ON PrintedStockMovements(StockItemId, Id DESC);");
        transaction.Commit();
    }

    public IReadOnlyList<PrintedStockItem> GetAll(bool includeArchived = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT s.Id,s.LinkedModelId,COALESCE(NULLIF(m.Name,''),s.ModelName),s.Variant,s.Location,s.Condition,
       s.Quantity,s.LowStockThreshold,s.Notes,s.UpdatedUtc,s.IsArchived
FROM PrintedStockItems s
LEFT JOIN Models m ON m.Id=s.LinkedModelId
WHERE ($includeArchived=1 OR s.IsArchived=0)
ORDER BY s.IsArchived,s.ModelName COLLATE NOCASE,s.Variant COLLATE NOCASE,s.Location COLLATE NOCASE,s.Id;";
        command.Parameters.AddWithValue("$includeArchived", includeArchived ? 1 : 0);
        using var reader = command.ExecuteReader();
        var result = new List<PrintedStockItem>();
        while (reader.Read()) result.Add(ReadStockItem(reader));
        return result;
    }

    public PrintedStockItem Add(PrintedStockItem item)
    {
        lock (gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO PrintedStockItems(LinkedModelId,ModelName,Variant,Location,Condition,Quantity,LowStockThreshold,Notes,UpdatedUtc)
VALUES($modelId,$modelName,$variant,$location,$condition,$quantity,$threshold,$notes,$updated);
SELECT last_insert_rowid();";
            BindDetails(command, item);
            command.Parameters.AddWithValue("$quantity", item.Quantity);
            var id = Convert.ToInt64(command.ExecuteScalar());

            if (item.Quantity > 0)
            {
                using var movement = connection.CreateCommand();
                movement.Transaction = transaction;
                movement.CommandText = @"INSERT INTO PrintedStockMovements(StockItemId,QuantityDelta,QuantityAfter,Reason,OccurredUtc)
VALUES($id,$delta,$after,'Initial count',$occurred);";
                movement.Parameters.AddWithValue("$id", id);
                movement.Parameters.AddWithValue("$delta", item.Quantity);
                movement.Parameters.AddWithValue("$after", item.Quantity);
                movement.Parameters.AddWithValue("$occurred", DateTime.UtcNow.ToString("O"));
                movement.ExecuteNonQuery();
            }

            transaction.Commit();
            return Get(id) ?? throw new InvalidOperationException("The new printed stock item could not be read back.");
        }
    }

    public void UpdateDetails(PrintedStockItem item)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE PrintedStockItems SET LinkedModelId=$modelId,ModelName=$modelName,Variant=$variant,
Location=$location,Condition=$condition,LowStockThreshold=$threshold,Notes=$notes,UpdatedUtc=$updated
WHERE Id=$id AND IsArchived=0;";
            BindDetails(command, item);
            command.Parameters.AddWithValue("$id", item.Id);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The printed stock item no longer exists or has been archived.");
        }
    }

    public PrintedStockItem Adjust(long id, int quantityDelta, string reason)
    {
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "An adjustment must change the quantity.");
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) throw new ArgumentException("An adjustment reason is required.", nameof(reason));

        lock (gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            int current;
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT Quantity FROM PrintedStockItems WHERE Id=$id AND IsArchived=0;";
                read.Parameters.AddWithValue("$id", id);
                var value = read.ExecuteScalar();
                if (value is null or DBNull) throw new InvalidOperationException("The printed stock item no longer exists or has been archived.");
                current = Convert.ToInt32(value);
            }

            int updated;
            try { updated = checked(current + quantityDelta); }
            catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(quantityDelta), quantityDelta, "The adjusted quantity is too large."); }
            if (updated < 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "On-hand quantity cannot be negative.");

            var now = DateTime.UtcNow.ToString("O");
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE PrintedStockItems SET Quantity=$quantity,UpdatedUtc=$updated WHERE Id=$id;";
                update.Parameters.AddWithValue("$quantity", updated);
                update.Parameters.AddWithValue("$updated", now);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
            using (var movement = connection.CreateCommand())
            {
                movement.Transaction = transaction;
                movement.CommandText = @"INSERT INTO PrintedStockMovements(StockItemId,QuantityDelta,QuantityAfter,Reason,OccurredUtc)
VALUES($id,$delta,$after,$reason,$occurred);";
                movement.Parameters.AddWithValue("$id", id);
                movement.Parameters.AddWithValue("$delta", quantityDelta);
                movement.Parameters.AddWithValue("$after", updated);
                movement.Parameters.AddWithValue("$reason", reason);
                movement.Parameters.AddWithValue("$occurred", now);
                movement.ExecuteNonQuery();
            }
            transaction.Commit();
            return Get(id) ?? throw new InvalidOperationException("The adjusted printed stock item could not be read back.");
        }
    }

    public void Archive(long id)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PrintedStockItems SET IsArchived=1,UpdatedUtc=$updated WHERE Id=$id AND IsArchived=0;";
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The printed stock item no longer exists or has been archived.");
        }
    }

    public IReadOnlyList<StockMovement> GetMovements(long stockItemId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,StockItemId,QuantityDelta,QuantityAfter,Reason,OccurredUtc FROM PrintedStockMovements WHERE StockItemId=$id ORDER BY Id DESC;";
        command.Parameters.AddWithValue("$id", stockItemId);
        using var reader = command.ExecuteReader();
        var result = new List<StockMovement>();
        while (reader.Read())
            result.Add(new StockMovement(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4), ParseUtc(reader.GetString(5))));
        return result;
    }

    private PrintedStockItem? Get(long id)
        => GetAll(includeArchived: true).FirstOrDefault(x => x.Id == id);

    private static PrintedStockItem ReadStockItem(SqliteDataReader reader)
        => new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8),
            ParseUtc(reader.GetString(9)), reader.GetInt64(10) != 0);

    private static DateTime ParseUtc(string value)
        => DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed.ToUniversalTime() : DateTime.UnixEpoch;

    private static void BindDetails(SqliteCommand command, PrintedStockItem item)
    {
        command.Parameters.AddWithValue("$modelId", (object?)item.LinkedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelName", item.ModelName);
        command.Parameters.AddWithValue("$variant", item.Variant);
        command.Parameters.AddWithValue("$location", item.Location);
        command.Parameters.AddWithValue("$condition", item.Condition);
        command.Parameters.AddWithValue("$threshold", item.LowStockThreshold);
        command.Parameters.AddWithValue("$notes", item.Notes);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
