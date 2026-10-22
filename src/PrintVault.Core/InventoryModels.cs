namespace PrintVault.Core;

/// <summary>A physical printed item held in stock, optionally linked to a library model.</summary>
public sealed record PrintedStockItem(
    long Id,
    long? LinkedModelId,
    string ModelName,
    string Variant,
    string Location,
    string Condition,
    int Quantity,
    int LowStockThreshold,
    string Notes,
    DateTime UpdatedUtc,
    bool IsArchived)
{
    public bool IsLowStock => !IsArchived && LowStockThreshold > 0 && Quantity <= LowStockThreshold;
    public string StockStatus => IsArchived ? "Archived" : IsLowStock ? "LOW STOCK" : "In stock";
}

/// <summary>An immutable, auditable change to a printed item's on-hand quantity.</summary>
public sealed record StockMovement(
    long Id,
    long StockItemId,
    int QuantityDelta,
    int QuantityAfter,
    string Reason,
    DateTime OccurredUtc);
