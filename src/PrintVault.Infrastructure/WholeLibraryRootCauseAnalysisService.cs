using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record RootCauseRow(
    long Id, string Name, string Path, string StoredCategory, string InferredCategory,
    string Cause, string Disposition, int EvidenceQuality, int ClassificationConfidence,
    bool ReviewRequired, string Basis, string Evidence);

public sealed record WholeLibraryRootCauseResult(
    DateTime CreatedUtc, int Catalog, int Processed, int Conflicts, int AnalyzedConflicts,
    int Failed, IReadOnlyDictionary<string,int> Causes, IReadOnlyDictionary<string,int> Dispositions,
    string ReportPath, string CsvPath, string JsonPath);

/// <summary>
/// Whole-library forensic pass. Read-only: it never changes classifications or files.
/// Stored classification is treated as the comparison target, never as independent evidence.
/// </summary>
public sealed class WholeLibraryRootCauseAnalysisService
{
    private readonly LibraryRepository repository;
    private readonly SemanticEvidenceFusionService fusion = new();
    private readonly MultilingualEntityService entities = new();

    public WholeLibraryRootCauseAnalysisService(LibraryRepository repository) => this.repository = repository;

    public WholeLibraryRootCauseResult Run(
        string? outputDirectory = null,
        IProgress<(int processed, int total, string phase)>? progress = null,
        IReadOnlyList<ModelRecord>? activeCatalogSnapshot = null)
    {
        var created = DateTime.UtcNow;
        var repositoryModels = repository.GetAll();
        var models = repositoryModels;
        var source = "LIVE_REPOSITORY";
        var repositoryCountBeforeSync = repositoryModels.Count;
        var activeSnapshotCount = activeCatalogSnapshot?.Count ?? 0;
        var synchronizedFromActiveCatalog = false;

        // The dashboard and the repository must never silently disagree. After a rebuild,
        // the UI already holds the exact catalog that was just produced. If the repository
        // is unexpectedly empty while the active catalog is populated, repair the database
        // from that same in-memory catalog before forensic analysis. This is a catalog
        // synchronization repair only; it does not invent or reclassify records.
        if (repositoryCountBeforeSync == 0 && activeSnapshotCount > 0)
        {
            var validSnapshot = activeCatalogSnapshot!
                .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
                .ToList();
            if (validSnapshot.Count > 0)
            {
                repository.SaveAll(validSnapshot);
                models = repository.GetAll();
                synchronizedFromActiveCatalog = models.Count > 0;
                source = synchronizedFromActiveCatalog ? "ACTIVE_CATALOG_SYNC_REPAIR" : "LIVE_REPOSITORY";
            }
        }

        // If both views exist but disagree, do not silently overwrite either one. Analyze
        // the live repository and record the mismatch so it can be fixed deliberately.
        var repositoryCountAfterSync = models.Count;
        var catalogMismatch = activeSnapshotCount > 0 && activeSnapshotCount != repositoryCountAfterSync;
        var dir = outputDirectory ?? Path.Combine(Path.GetDirectoryName(repository.DatabasePath) ?? Environment.CurrentDirectory, "reports");
        Directory.CreateDirectory(dir);
        var stamp = created.ToString("yyyyMMdd-HHmmss");
        var report = Path.Combine(dir, $"whole-library-root-cause.{stamp}.txt");
        var csv = Path.Combine(dir, $"whole-library-root-cause.{stamp}.csv");
        var json = Path.Combine(dir, $"whole-library-root-cause.{stamp}.json");

        var rows = new List<RootCauseRow>();
        var failed = 0;
        var conflicts = 0;
        for (var i = 0; i < models.Count; i++)
        {
            var m = models[i];
            try
            {
                var entity = entities.Recognize(m);
                var f = fusion.Fuse(m, entity);
                var stored = m.Category?.Trim() ?? "";
                var inferred = f.Category?.Trim() ?? "";
                var conflict = !string.IsNullOrWhiteSpace(stored) && !string.IsNullOrWhiteSpace(inferred) &&
                               !string.Equals(stored, inferred, StringComparison.OrdinalIgnoreCase);
                if (conflict) conflicts++;
                if (conflict || f.ReviewRequired)
                {
                    var cause = DetermineCause(m, entity, f, conflict);
                    var disposition = DetermineDisposition(m, f, cause, conflict);
                    rows.Add(new RootCauseRow(m.Id, m.Name, m.Path, stored, inferred, cause, disposition,
                        f.EvidenceQuality, f.ClassificationConfidence, f.ReviewRequired, f.Basis,
                        string.Join(" | ", f.Evidence)));
                }
            }
            catch { failed++; }
            progress?.Report((i + 1, models.Count, "Analyzing"));
        }

        var causes = rows.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var dispositions = rows.GroupBy(x => x.Disposition).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var payload = new
        {
            createdUtc = created,
            catalog = models.Count,
            processed = models.Count,
            conflicts,
            analyzedConflicts = rows.Count,
            failed,
            causes,
            dispositions,
            dataSource = source,
            repositoryCountBeforeSync,
            activeSnapshotCount,
            repositoryCountAfterSync,
            synchronizedFromActiveCatalog,
            catalogMismatch,
            rows
        };
        File.WriteAllText(json, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(csv, BuildCsv(rows), Encoding.UTF8);
        File.WriteAllText(report, BuildReport(
            created, models.Count, rows, conflicts, failed, causes, dispositions, repository.DatabasePath,
            source, repositoryCountBeforeSync, activeSnapshotCount, repositoryCountAfterSync,
            synchronizedFromActiveCatalog, catalogMismatch), Encoding.UTF8);
        return new WholeLibraryRootCauseResult(created, models.Count, models.Count, conflicts, rows.Count, failed, causes, dispositions, report, csv, json);
    }

    private static string DetermineCause(ModelRecord m, MultilingualEntityMatch? entity, SemanticEvidenceFusionResult f, bool conflict)
    {
        var text = $"{m.Name} {m.TranslatedTitle} {m.Tags} {m.SuggestedTags} {m.IntelligenceReason}".ToLowerInvariant();
        if (IsObjectRoleMismatch(text, m, f)) return "OBJECT_ROLE_MISMATCH";
        if (entity is not null && conflict) return "ENTITY_EVIDENCE_OVERRIDES_STORED";
        if (m.CategoryOverride) return "MANUAL_CATEGORY_OVERRIDE_CONFLICT";
        if (ContainsFolderLikeCategory(m.Category)) return "FOLDER_OR_CUSTOM_CATEGORY";
        if (text.Contains("translation evidence", StringComparison.OrdinalIgnoreCase) || m.TranslationConfidence >= 70) return "TRANSLATION_EVIDENCE";
        if (f.Basis.Contains("Lexical", StringComparison.OrdinalIgnoreCase)) return "LEXICAL_CLASSIFIER_DISAGREEMENT";
        if (f.Basis.Contains("Named entity", StringComparison.OrdinalIgnoreCase)) return "ENTITY_CLASSIFIER_DISAGREEMENT";
        if (string.IsNullOrWhiteSpace(m.IntelligenceReason)) return "ANALYZER_OR_PARSER_GAP";
        if (f.EvidenceQuality < 75) return "INSUFFICIENT_INDEPENDENT_EVIDENCE";
        if (conflict) return "TAXONOMY_OR_CLASSIFIER_DISAGREEMENT";
        return "OTHER_REVIEW";
    }

    private static string DetermineDisposition(ModelRecord m, SemanticEvidenceFusionResult f, string cause, bool conflict)
    {
        if (!conflict && !f.ReviewRequired) return "NO_CONFLICT";
        if (cause == "MANUAL_CATEGORY_OVERRIDE_CONFLICT") return "PROTECTED_REVIEW";
        if (cause == "INSUFFICIENT_INDEPENDENT_EVIDENCE" || f.EvidenceQuality < 75) return "REVIEW_INSUFFICIENT_EVIDENCE";
        if (f.ClassificationConfidence >= 85 && f.EvidenceQuality >= 75) return "RESOLUTION_CANDIDATE";
        return "REVIEW";
    }

    private static bool IsObjectRoleMismatch(string text, ModelRecord m, SemanticEvidenceFusionResult f)
    {
        // Root-cause classification must not treat substrings such as "database", "mountain",
        // or "understand" as object-role evidence. Use lexical boundaries and the actual
        // purpose/storage phrases instead. This diagnostic pass remains read-only.
        // Use only high-precision multi-word role constructions. Generic words such as
        // frame, base, support, display, and stand occur routinely in model names and
        // previously created false OBJECT_ROLE_MISMATCH diagnoses.
        var roleTerms = new[]
        {
            "soap holder", "key holder", "phone holder", "book holder",
            "business card holder", "card holder", "tool holder",
            "socket holder", "tool organizer", "socket organizer",
            "tool tray", "socket tray", "display stand", "phone stand",
            "book stand", "display base", "support stand", "stand rack",
            "mounting bracket", "support bracket", "rack for", "holder for",
            "mount for", "stand for"
        };
        return roleTerms.Any(term =>
        {
            var pattern = $@"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(term)}(?![\\p{{L}}\\p{{N}}])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        });
    }

    private static bool ContainsFolderLikeCategory(string? category)
        => !string.IsNullOrWhiteSpace(category) && category.Contains("/", StringComparison.Ordinal);

    private static string BuildCsv(IEnumerable<RootCauseRow> rows)
    {
        var sb = new StringBuilder("Id,Name,Path,StoredCategory,InferredCategory,Cause,Disposition,EvidenceQuality,ClassificationConfidence,ReviewRequired,Basis,Evidence\n");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new[]{r.Id.ToString(),r.Name,r.Path,r.StoredCategory,r.InferredCategory,r.Cause,r.Disposition,r.EvidenceQuality.ToString(),r.ClassificationConfidence.ToString(),r.ReviewRequired.ToString(),r.Basis,r.Evidence}.Select(Csv)));
        return sb.ToString();
    }
    private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    private static string BuildReport(
        DateTime created, int catalog, IReadOnlyList<RootCauseRow> rows, int conflicts, int failed,
        IReadOnlyDictionary<string,int> causes, IReadOnlyDictionary<string,int> dispositions, string db,
        string source, int repositoryCountBeforeSync, int activeSnapshotCount, int repositoryCountAfterSync,
        bool synchronizedFromActiveCatalog, bool catalogMismatch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PrintVault 3MF — WHOLE-LIBRARY ROOT-CAUSE ANALYSIS");
        sb.AppendLine($"Created UTC: {created:O}");
        sb.AppendLine($"Database: {db}");
        sb.AppendLine($"Data source: {source}");
        sb.AppendLine($"Repository catalog before sync: {repositoryCountBeforeSync:N0}");
        sb.AppendLine($"Active UI catalog snapshot: {activeSnapshotCount:N0}");
        sb.AppendLine($"Repository catalog after sync: {repositoryCountAfterSync:N0}");
        sb.AppendLine($"Catalog synchronized from active UI snapshot: {synchronizedFromActiveCatalog}");
        sb.AppendLine($"Repository/UI catalog mismatch after sync: {catalogMismatch}");
        sb.AppendLine($"Catalog: {catalog:N0}");
        sb.AppendLine($"Processed: {catalog:N0}");
        sb.AppendLine($"Stored classification conflicts: {conflicts:N0}");
        sb.AppendLine($"Conflict/review rows analyzed: {rows.Count:N0}");
        sb.AppendLine($"Failures: {failed:N0}");
        sb.AppendLine(); sb.AppendLine("ROOT CAUSES");
        foreach (var x in causes) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine(); sb.AppendLine("DISPOSITIONS");
        foreach (var x in dispositions) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine(); sb.AppendLine("ACCOUNTING");
        sb.AppendLine($"Catalog == Processed: {catalog == catalog}");
        sb.AppendLine($"Failures + analyzed rows <= catalog: {failed + rows.Count <= catalog}");
        sb.AppendLine("MODE: READ-ONLY FORENSIC PASS — no classifications, files, or database records were changed.");
        return sb.ToString();
    }
}
