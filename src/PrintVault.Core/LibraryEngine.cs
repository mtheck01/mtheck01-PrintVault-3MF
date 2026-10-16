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
    int Percent,
    string? CurrentPath = null,
    string? CurrentStage = null,
    long LastFileElapsedMilliseconds = 0,
    long AnalysisElapsedMilliseconds = 0,
    long HashElapsedMilliseconds = 0,
    long ThumbnailElapsedMilliseconds = 0,
    long PersistenceElapsedMilliseconds = 0,
    long CheckpointCount = 0,
    long LastFileAnalysisMilliseconds = 0,
    long LastFileHashMilliseconds = 0,
    long LastFileThumbnailMilliseconds = 0
);

public interface ILibraryEngine
{
    Task<IReadOnlyList<ModelRecord>> ScanAsync(IEnumerable<string> roots, ScanMode mode = ScanMode.Turbo, CancellationToken token = default, IProgress<ScanProgress>? progress = null);
    Task<IntelligenceResult> AnalyzeAsync(ModelRecord model, CancellationToken token = default);
    Task<LibraryStats> GetStatsAsync(CancellationToken token = default);
}
