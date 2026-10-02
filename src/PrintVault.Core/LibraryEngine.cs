namespace PrintVault.Core;

public enum ScanMode
{
    Quick,
    Turbo,
    Deep
}

public sealed record ScanProgress(
    string Phase,
    int Discovered,
    int Processed,
    int Indexed,
    int Failed,
    int Percent
);

public interface ILibraryEngine
{
    Task<IReadOnlyList<ModelRecord>> ScanAsync(IEnumerable<string> roots, ScanMode mode = ScanMode.Turbo, CancellationToken token = default, IProgress<ScanProgress>? progress = null);
    Task<IntelligenceResult> AnalyzeAsync(ModelRecord model, CancellationToken token = default);
    Task<LibraryStats> GetStatsAsync(CancellationToken token = default);
}
