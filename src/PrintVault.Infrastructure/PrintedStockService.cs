using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>Validates and coordinates changes to physical printed stock.</summary>
public sealed class PrintedStockService
{
    public static readonly IReadOnlyList<string> Conditions = ["New", "Good", "Used", "Needs repair"];
    private readonly InventoryRepository repository;

    public PrintedStockService(InventoryRepository repository)
        => this.repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public IReadOnlyList<PrintedStockItem> GetItems(bool includeArchived = false) => repository.GetAll(includeArchived);
    public IReadOnlyList<StockMovement> GetMovements(long id) => repository.GetMovements(id);

    public PrintedStockItem Add(long? linkedModelId, string modelName, string variant, string location,
        string condition, int quantity, int lowStockThreshold, string notes)
    {
        Validate(modelName, condition, quantity, lowStockThreshold);
        return repository.Add(new PrintedStockItem(0, linkedModelId, Clean(modelName), Clean(variant), Clean(location),
            condition, quantity, lowStockThreshold, Clean(notes), DateTime.UtcNow, false));
    }

    public void UpdateDetails(PrintedStockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Validate(item.ModelName, item.Condition, item.Quantity, item.LowStockThreshold);
        repository.UpdateDetails(item with
        {
            ModelName = Clean(item.ModelName), Variant = Clean(item.Variant), Location = Clean(item.Location), Notes = Clean(item.Notes)
        });
    }

    public PrintedStockItem Adjust(long id, int delta, string reason) => repository.Adjust(id, delta, reason);
    public void Archive(long id) => repository.Archive(id);

    private static void Validate(string modelName, string condition, int quantity, int lowStockThreshold)
    {
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Enter a model name.", nameof(modelName));
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "On-hand quantity cannot be negative.");
        if (lowStockThreshold < 0) throw new ArgumentOutOfRangeException(nameof(lowStockThreshold), "Low-stock threshold cannot be negative.");
        if (!Conditions.Contains(condition, StringComparer.Ordinal)) throw new ArgumentException("Choose a valid condition.", nameof(condition));
    }

    private static string Clean(string? value) => (value ?? "").Trim();
}
