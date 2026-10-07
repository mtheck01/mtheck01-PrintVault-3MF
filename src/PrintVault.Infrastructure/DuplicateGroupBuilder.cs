namespace PrintVault.Infrastructure;

/// <summary>
/// Owns duplicate-group derivation so duplicate reconciliation has one deterministic implementation.
/// </summary>
public sealed class DuplicateGroupBuilder
{
    public void Rebuild(IList<ModelRecord> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        foreach (var model in models) model.DuplicateGroup = "";
        foreach (var group in models.Where(x => !string.IsNullOrEmpty(x.Hash))
                     .GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var id = group.Key[..Math.Min(12, group.Key.Length)];
            foreach (var model in group) model.DuplicateGroup = id;
        }
    }
}
