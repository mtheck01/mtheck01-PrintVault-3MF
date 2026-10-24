using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed record RootCauseRow(
    long Id, string Name, string Path, string StoredCategory, string CandidateCategory,
    string Cause, string Disposition, int EvidenceQuality, int ClassificationConfidence,
    bool ReviewRequired, string Basis, string Evidence, string AppliedCategory, string ArbitrationDecision);

public sealed record RootCauseOutcome(
    long Id, string Name, string Path, string StoredCategory, string CandidateCategory,
    string SemanticType, string Subtype, string Family, int ClassificationConfidence,
    int EvidenceQuality, bool ReviewRequired, string Basis, string Evidence, string Outcome,
    string AppliedCategory, string ArbitrationDecision);

public sealed record WholeLibraryEvidenceTrace(
    long Id, string Name, string Path, string StoredCategory,
    string InputTitle, string TranslatedTitle, string OriginalLanguage, int TranslationConfidence,
    long SourceAnalyzerElapsedMilliseconds, IntelligenceResult? SourceAnalyzer,
    ThreeMfAnalysisTrace? SourceDetails, IReadOnlyList<ModelIntelligenceStageTrace> IntelligenceStages,
    IReadOnlyList<string> FusionDiagnostics);

public sealed record WholeLibraryRootCauseResult(
    DateTime CreatedUtc, int Catalog, int Processed, int Conflicts, int AnalyzedConflicts,
    int Failed, IReadOnlyDictionary<string,int> Causes, IReadOnlyDictionary<string,int> Dispositions,
    string ReportPath, string CsvPath, string JsonPath,
    int RecordsAnalyzed, int ResolvedInferences, int UnresolvedInferences,
    int EvidenceBearingResults, int ReviewRequiredResults, int MissingInferenceResults,
    IReadOnlyDictionary<string,int> AppliedCategories, string OutcomeFingerprint,
    string OutcomesCsvPath, IReadOnlyDictionary<string,int> CandidateCategories);

/// <summary>
/// Whole-library forensic pass. Read-only: it never changes classifications or files.
/// Stored classification is treated as the comparison target, never as independent evidence.
/// </summary>
public sealed class WholeLibraryRootCauseAnalysisService
{
    private sealed record ForensicSourceTrace(
        IReadOnlyList<string> Evidence, IntelligenceResult? Analysis, ThreeMfAnalysisTrace? Details);

    private readonly LibraryRepository repository;

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

        // Conflict/review rows preserve the legacy gate semantics. Root-cause rows are a
        // separate population and MUST include every unresolved inference; otherwise the
        // diagnostic reports 1,099 unresolved records but an empty ROOT CAUSES section.
        var rows = new ConcurrentBag<RootCauseRow>();
        var rootCauseRows = new ConcurrentBag<RootCauseRow>();
        var outcomes = new ConcurrentBag<RootCauseOutcome>();
        var evidenceTraces = new ConcurrentBag<WholeLibraryEvidenceTrace>();
        var failureDetails = new ConcurrentBag<string>();
        var failed = 0;
        var conflicts = 0;
        var recordsAnalyzed = 0;
        var resolvedInferences = 0;
        var unresolvedInferences = 0;
        var evidenceBearingResults = 0;
        var reviewRequiredResults = 0;
        var missingInferenceResults = 0;
        var sourceAnalyzerInvocations = 0;
        var sourceAnalyzerSkipped = 0;
        long sourceForensicsElapsedMilliseconds = 0;
        var progressCount = 0;
        var progressLock = new object();

        // Each record is analyzed independently and the stages are read-only. Bounded
        // parallelism removes the single-threaded 40-minute bottleneck without creating
        // unbounded archive/CPU pressure on the self-hosted Windows runner. Concurrent
        // collections plus atomic accounting preserve every record and failure.
        var degree = Math.Clamp(Environment.ProcessorCount, 2, 4);
        // Pipeline stages and analyzer helpers are worker-local. This avoids relying on
        // undocumented thread-safety of mutable stage/context state while still parallelizing
        // independent records. Each worker reuses its instances for its assigned records.
        Parallel.ForEach(
            models,
            new ParallelOptions { MaxDegreeOfParallelism = degree },
            () => (Intelligence: new ModelIntelligencePipeline(), SourceAnalyzer: new ThreeMfAnalyzer()),
            (m, _, worker) =>
        {
            try
            {
                var sourceStarted = Stopwatch.GetTimestamp();
                var sourceModel = AnalyzeSourceForForensics(m, worker.SourceAnalyzer, out var sourceTrace);
                var sourceElapsedMs = (long)Stopwatch.GetElapsedTime(sourceStarted).TotalMilliseconds;
                Interlocked.Add(ref sourceForensicsElapsedMilliseconds,
                    sourceElapsedMs);
                if (sourceTrace.Evidence.Any(e => e.StartsWith("SOURCE_ANALYZER:COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                                         e.StartsWith("SOURCE_ANALYZER:FAILED:", StringComparison.OrdinalIgnoreCase)))
                    Interlocked.Increment(ref sourceAnalyzerInvocations);
                else
                    Interlocked.Increment(ref sourceAnalyzerSkipped);
                var stage = worker.Intelligence.Analyze(sourceModel);
                var entity = stage.Entity;
                var f = stage.Fusion;
                var evidenceItems = (f.Evidence ?? Array.Empty<string>())
                    .Concat(sourceTrace.Evidence)
                    .Concat(stage.StageEvidence)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var stored = m.Category?.Trim() ?? "";
                var inferred = f.Category?.Trim() ?? "";
                var inferenceMissing = string.IsNullOrWhiteSpace(inferred);
                var candidateUnresolved = IsUnresolvedCategory(inferred);
                var arbitrationDecision = GetArbitrationDecision(stage);
                // stage.Model.Category is the final effective value after the shared
                // arbitration stage, including protected and rejected decisions.
                var applied = stage.Model.Category?.Trim() ?? stored;
                var appliedUnresolved = IsUnresolvedCategory(applied);
                var conflict = !IsUnresolvedCategory(stored) && !candidateUnresolved &&
                               !string.Equals(stored, inferred, StringComparison.OrdinalIgnoreCase);
                var rejectedActionableCandidate = string.Equals(arbitrationDecision, "REVIEW", StringComparison.OrdinalIgnoreCase) &&
                    !candidateUnresolved && !string.Equals(applied, inferred, StringComparison.OrdinalIgnoreCase);
                var needsReview = f.ReviewRequired || rejectedActionableCandidate;
                if (conflict || needsReview || inferenceMissing || candidateUnresolved || appliedUnresolved)
                {
                    evidenceTraces.Add(new WholeLibraryEvidenceTrace(
                        m.Id, m.Name, m.Path, stored, sourceModel.Name ?? string.Empty,
                        sourceModel.TranslatedTitle ?? string.Empty, sourceModel.OriginalLanguage ?? string.Empty,
                        sourceModel.TranslationConfidence, sourceElapsedMs, sourceTrace.Analysis, sourceTrace.Details,
                        stage.StageTrace, f.Diagnostics));
                }
                if (conflict || needsReview || inferenceMissing || candidateUnresolved || appliedUnresolved)
                {
                    var cause = DetermineCause(m, sourceModel, entity, f, conflict);
                    var disposition = DetermineDisposition(m, f, cause, conflict, arbitrationDecision);
                    var row = new RootCauseRow(m.Id, m.Name, m.Path, stored, inferred, cause, disposition,
                        f.EvidenceQuality, f.ClassificationConfidence, f.ReviewRequired, f.Basis,
                        string.Join(" | ", evidenceItems), applied, arbitrationDecision);
                    rootCauseRows.Add(row);
                    if (conflict || needsReview) rows.Add(row);
                }

                var outcome = inferenceMissing ? "MISSING_INFERENCE"
                    : needsReview ? "REVIEW_REQUIRED"
                    : appliedUnresolved ? "UNRESOLVED"
                    : conflict ? "CATEGORY_CONFLICT"
                    : "RESOLVED_NO_CONFLICT";
                outcomes.Add(new RootCauseOutcome(
                    m.Id, m.Name, m.Path, stored, inferred, f.SemanticType, f.Subtype, f.Family,
                    f.ClassificationConfidence, f.EvidenceQuality, needsReview, f.Basis,
                    string.Join(" | ", evidenceItems), outcome, applied, arbitrationDecision));
                if (conflict) Interlocked.Increment(ref conflicts);
                Interlocked.Increment(ref recordsAnalyzed);
                if (inferenceMissing) Interlocked.Increment(ref missingInferenceResults);
                if (appliedUnresolved) Interlocked.Increment(ref unresolvedInferences);
                else Interlocked.Increment(ref resolvedInferences);
                if (f.EvidenceQuality > 0 && evidenceItems.Length > 0)
                    Interlocked.Increment(ref evidenceBearingResults);
                if (needsReview) Interlocked.Increment(ref reviewRequiredResults);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                var detail = $"path=\"{m.Path}\" exception={ex.GetType().FullName}: {ex.Message}";
                failureDetails.Add(detail + Environment.NewLine + ex.StackTrace);
                Console.Error.WriteLine("ROOT_CAUSE_ANALYSIS_FAILURE " + detail);
                Console.Error.WriteLine(ex.StackTrace);
            }

            var completed = Interlocked.Increment(ref progressCount);
            // Serialize progress callbacks because the UI/runner progress sink is not
            // guaranteed to be thread-safe; still emit a heartbeat at least every 10 rows.
            if (completed == models.Count || completed % 10 == 0)
            {
                lock (progressLock) progress?.Report((completed, models.Count, "Analyzing"));
                Console.WriteLine($"ROOT_CAUSE_PROGRESS={completed}/{models.Count} DEGREE={degree}");
            }
            return worker;
        },
        _ => { });

        // Stable ordering keeps report diffs and outcome fingerprints reproducible even
        // though execution is concurrent.
        var orderedRootCauseRows = rootCauseRows.OrderBy(x => x.Id).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedRows = rows.OrderBy(x => x.Id).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedOutcomes = outcomes.OrderBy(x => x.Id).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedEvidenceTraces = evidenceTraces.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedFailures = failureDetails.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var causes = orderedRootCauseRows.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var dispositions = orderedRootCauseRows.GroupBy(x => x.Disposition).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var appliedCategories = orderedOutcomes
            .GroupBy(x => string.IsNullOrWhiteSpace(x.AppliedCategory) ? "<MISSING>" : x.AppliedCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var candidateCategories = orderedOutcomes
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CandidateCategory) ? "<MISSING>" : x.CandidateCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var outcomeFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(orderedOutcomes))));
        var outcomesCsv = Path.Combine(dir, $"whole-library-outcomes.{stamp}.csv");
        var payload = new
        {
            createdUtc = created,
            catalog = models.Count,
            processed = recordsAnalyzed,
            recordsAnalyzed,
            conflicts,
            analyzedConflicts = rows.Count,
            rootCauseRecords = rootCauseRows.Count,
            failed,
            resolvedInferences,
            unresolvedInferences,
            evidenceBearingResults,
            reviewRequiredResults,
            missingInferenceResults,
            sourceAnalyzerInvocations,
            sourceAnalyzerSkipped,
            sourceForensicsElapsedMilliseconds,
            appliedCategories,
            candidateCategories,
            outcomeFingerprint,
            failureDetails = orderedFailures,
            causes,
            dispositions,
            dataSource = source,
            repositoryCountBeforeSync,
            activeSnapshotCount,
            repositoryCountAfterSync,
            synchronizedFromActiveCatalog,
            catalogMismatch,
            evidenceTraceSchemaVersion = 3,
            evidenceTraces = orderedEvidenceTraces,
            rows = orderedRootCauseRows,
            conflictReviewRows = orderedRows,
            outcomes = orderedOutcomes
        };
        File.WriteAllText(json, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(csv, BuildCsv(orderedRootCauseRows), Encoding.UTF8);
        File.WriteAllText(outcomesCsv, BuildOutcomesCsv(orderedOutcomes), Encoding.UTF8);
        File.WriteAllText(report, BuildReport(
            created, models.Count, orderedRootCauseRows, orderedRows.Count, conflicts, failed, orderedFailures, causes, dispositions, repository.DatabasePath,
            source, repositoryCountBeforeSync, activeSnapshotCount, repositoryCountAfterSync,
            synchronizedFromActiveCatalog, catalogMismatch, recordsAnalyzed, resolvedInferences,
            unresolvedInferences, evidenceBearingResults, reviewRequiredResults, missingInferenceResults,
            appliedCategories, outcomeFingerprint, sourceAnalyzerInvocations, sourceAnalyzerSkipped,
            sourceForensicsElapsedMilliseconds, candidateCategories), Encoding.UTF8);
        return new WholeLibraryRootCauseResult(
            created, models.Count, recordsAnalyzed, conflicts, orderedRows.Count, failed, causes, dispositions,
            report, csv, json, recordsAnalyzed, resolvedInferences, unresolvedInferences,
            evidenceBearingResults, reviewRequiredResults, missingInferenceResults,
            appliedCategories, outcomeFingerprint, outcomesCsv, candidateCategories);
    }

    /// <summary>
    /// Produces an isolated source-enriched copy for forensic analysis. It never persists
    /// changes to the repository and preserves manual/custom category decisions.
    /// </summary>
    private ModelRecord AnalyzeSourceForForensics(
        ModelRecord source, ThreeMfAnalyzer sourceAnalyzer, out ForensicSourceTrace trace)
    {
        var model = CloneForForensics(source);
        var details = new List<string>();
        IntelligenceResult? sourceAnalysis = null;
        ThreeMfAnalysisTrace? sourceDetails = null;
        if (string.IsNullOrWhiteSpace(source.Path) || !File.Exists(source.Path))
        {
            details.Add("SOURCE_ANALYZER:SKIPPED_FILE_MISSING");
            trace = new ForensicSourceTrace(details, sourceAnalysis, sourceDetails);
            return model;
        }

        // Source-level 3MF inspection is the expensive stage: it opens and parses each
        // archive. Re-running it for every already-classified record made the forensic
        // pass scale with the entire archive corpus even when the stored record already
        // contains usable semantic metadata. Inspect source files when it can change the
        // diagnostic: unresolved categories or missing core semantic metadata. Every
        // catalog record still passes through the intelligence fusion and accounting
        // stages below, so the audit remains whole-library rather than a partial scan.
        var category = source.Category?.Trim() ?? string.Empty;
        var unresolvedCategory = IsUnresolvedCategory(category);
        // IntelligenceReason is explanatory text, not a semantic feature. Re-parsing a
        // large 3MF solely because this note is blank adds archive I/O without improving
        // category/type inference when the actual semantic fields are already populated.
        var missingSemanticMetadata =
            string.IsNullOrWhiteSpace(source.SemanticType) ||
            string.IsNullOrWhiteSpace(source.Subtype) ||
            string.IsNullOrWhiteSpace(source.Family);
        if (!unresolvedCategory && !missingSemanticMetadata)
        {
            details.Add("SOURCE_ANALYZER:SKIPPED_EXISTING_SEMANTIC_METADATA");
            trace = new ForensicSourceTrace(details, sourceAnalysis, sourceDetails);
            return model;
        }

        var analyzerStarted = Stopwatch.GetTimestamp();
        Console.WriteLine($"ROOT_CAUSE_SOURCE_ANALYZER_START id={source.Id} path={JsonSerializer.Serialize(source.Path)}");
        try
        {
            // Preserve semantic/provenance tracing without enumerating every reachable
            // vertex and triangle. Exact component counts are unrelated to category
            // inference and made this pass repeatedly parse large geometry payloads.
            var analysis = sourceAnalyzer.AnalyzeForensicFastGeometry(source.Path, source.Name, out var analysisTrace);
            model.SourceSemanticEvidence = analysis.SourceSemanticEvidence;
            model.SourceSemanticEvidenceItems = analysis.SourceSemanticEvidenceItems;
            sourceAnalysis = analysis;
            sourceDetails = analysisTrace;
            var analyzerElapsedMs = (long)Stopwatch.GetElapsedTime(analyzerStarted).TotalMilliseconds;
            Console.WriteLine($"ROOT_CAUSE_SOURCE_ANALYZER_DONE id={source.Id} elapsed_ms={analyzerElapsedMs} xml_ms={analysisTrace.MainModelXmlLoadMilliseconds} xml_uncompressed_bytes={analysisTrace.MainModelXmlUncompressedBytes} streamed_xml={analysisTrace.StreamedMainModelXml} metadata_ms={analysisTrace.PackageMetadataReadMilliseconds} semantic_score_ms={analysisTrace.SemanticScoringMilliseconds} geometry_ms={analysisTrace.GeometryResolutionMilliseconds} vertices={analysisTrace.VertexCount} category={JsonSerializer.Serialize(analysis.Category)}");
            // Keep the repository's category intact. Source analysis is a candidate channel;
            // the same arbitration stage used by production decides whether it is accepted.
            // This avoids a forensic-only confidence shortcut that could bypass the gates.
            model.IntelligenceScore = analysis.Confidence;
            model.IntelligenceReason = analysis.Reason ?? string.Empty;
            model.SemanticType = analysis.SemanticType ?? string.Empty;
            model.Subtype = analysis.Subtype ?? string.Empty;
            model.Family = analysis.Family ?? string.Empty;
            model.SuggestedTags = analysis.SuggestedTags ?? string.Empty;
            model.RiskFlags = analysis.RiskFlags ?? string.Empty;
            model.PrintMethod = analysis.PrintMethod ?? model.PrintMethod;
            model.PrintMethodConfidence = analysis.PrintMethodConfidence;
            model.PrintMethodEvidence = analysis.PrintMethodEvidence ?? model.PrintMethodEvidence;
            model.SpecialType = analysis.SpecialType ?? model.SpecialType;
            model.ObjectCount = analysis.ObjectCount;
            model.Dimensions = analysis.Dimensions ?? model.Dimensions;
            model.Slicer = analysis.Slicer ?? model.Slicer;
            model.Materials = analysis.Materials ?? model.Materials;
            model.PrintReady = analysis.PrintReady;

            details.Add($"SOURCE_ANALYZER:COMPLETED:{analysis.Category}:{analysis.Confidence:0.00}");
            details.Add($"SOURCE_ANALYZER_CATEGORY_CANDIDATE:{(IsUnresolvedCategory(analysis.Category) ? "NONE" : "FORWARDED_TO_ARBITRATION")}");
            if (string.Equals(analysis.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
                details.Add("SOURCE_ANALYZER:UNRESOLVED_LOW_OR_MISSING_SEMANTIC_SIGNAL");
        }
        catch (Exception ex)
        {
            // A per-file analyzer exception is explicit evidence, not a silent no-signal result.
            var analyzerElapsedMs = (long)Stopwatch.GetElapsedTime(analyzerStarted).TotalMilliseconds;
            Console.Error.WriteLine($"ROOT_CAUSE_SOURCE_ANALYZER_FAILED id={source.Id} elapsed_ms={analyzerElapsedMs} exception={ex.GetType().Name} message={JsonSerializer.Serialize(ex.Message)}");
            details.Add($"SOURCE_ANALYZER:FAILED:{ex.GetType().Name}:{ex.Message}");
        }

        trace = new ForensicSourceTrace(details, sourceAnalysis, sourceDetails);
        return model;
    }

    private static ModelRecord CloneForForensics(ModelRecord source) => new()
    {
        Id = source.Id, Path = source.Path, Name = source.Name, Category = source.Category,
        Size = source.Size, ModifiedUtc = source.ModifiedUtc, ThumbnailPath = source.ThumbnailPath,
        Favorite = source.Favorite, Tags = source.Tags, Family = source.Family, Slicer = source.Slicer,
        Materials = source.Materials, ObjectCount = source.ObjectCount, Dimensions = source.Dimensions,
        IntelligenceScore = source.IntelligenceScore, PrintReady = source.PrintReady, DuplicateGroup = source.DuplicateGroup,
        Hash = source.Hash, SemanticType = source.SemanticType, Subtype = source.Subtype,
        SuggestedTags = source.SuggestedTags, IntelligenceReason = source.IntelligenceReason,
        RiskFlags = source.RiskFlags, PrintMethod = source.PrintMethod, PrintMethodConfidence = source.PrintMethodConfidence,
        PrintMethodEvidence = source.PrintMethodEvidence, SpecialType = source.SpecialType,
        CategoryOverride = source.CategoryOverride, PrintMethodOverride = source.PrintMethodOverride,
        OriginalLanguage = source.OriginalLanguage, TranslatedTitle = source.TranslatedTitle,
        TranslationConfidence = source.TranslationConfidence, TranslationEvidence = source.TranslationEvidence,
        SourceSemanticEvidence = source.SourceSemanticEvidence,
        SourceSemanticEvidenceItems = source.SourceSemanticEvidenceItems
    };

    private static bool IsUnresolvedCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ||
           string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(category, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static string GetArbitrationDecision(ModelIntelligenceStageResult stage)
    {
        var arbitration = stage.StageTrace.FirstOrDefault(x =>
            string.Equals(x.Stage, "EVIDENCE_ARBITRATION", StringComparison.OrdinalIgnoreCase));
        var diagnostic = arbitration?.Diagnostics.FirstOrDefault(x =>
            x.StartsWith("decision=", StringComparison.OrdinalIgnoreCase));
        if (diagnostic is null) return "NO_DIAGNOSTIC";
        var value = diagnostic["decision=".Length..].Split(';', 2)[0].Trim();
        return value.Length == 0 ? "NO_DIAGNOSTIC" : value.ToUpperInvariant();
    }

    private static string DetermineCause(ModelRecord original, ModelRecord analyzed, MultilingualEntityMatch? entity, SemanticEvidenceFusionResult f, bool conflict)
    {
        // Use the enriched forensic copy for analyzer/translation evidence, but preserve
        // original stored fields for override and custom-category diagnostics.
        var text = $"{original.Name} {analyzed.TranslatedTitle} {original.Tags} {original.SuggestedTags} {analyzed.IntelligenceReason}".ToLowerInvariant();
        if (IsUnresolvedCategory(f.Category))
        {
            // Diagnose unresolved outcomes before conflict heuristics. The previous
            // implementation only created rows for conflicts/review, silently excluding
            // the entire unresolved population from the root-cause report.
            if (entity is not null) return "UNRESOLVED_ENTITY_MATCH_NOT_PROMOTED";
            if (f.Evidence.Any(e => e.StartsWith("Context-only signals:", StringComparison.OrdinalIgnoreCase)))
                return "UNRESOLVED_CONTEXT_ONLY_SIGNALS";
            if (analyzed.TranslationConfidence >= 70 || f.Evidence.Any(e => e.StartsWith("Translation evidence:", StringComparison.OrdinalIgnoreCase)))
                return "UNRESOLVED_TRANSLATION_NOT_PROMOTED";
            if (IsObjectRoleMismatch(text, original, f)) return "UNRESOLVED_ARTIFACT_ROLE_NOT_PROMOTED";
            if (string.IsNullOrWhiteSpace(analyzed.IntelligenceReason) ||
                analyzed.IntelligenceReason.Contains("No strong semantic signal", StringComparison.OrdinalIgnoreCase))
                return "UNRESOLVED_NO_STRONG_ANALYZER_SIGNAL";
            if (f.EvidenceQuality < 50) return "UNRESOLVED_LOW_EVIDENCE_QUALITY";
            return "UNRESOLVED_OTHER_CLASSIFICATION_GAP";
        }
        if (IsObjectRoleMismatch(text, original, f)) return "OBJECT_ROLE_MISMATCH";
        if (entity is not null && conflict) return "ENTITY_EVIDENCE_OVERRIDES_STORED";
        if (original.CategoryOverride) return "MANUAL_CATEGORY_OVERRIDE_CONFLICT";
        if (ContainsFolderLikeCategory(original.Category)) return "FOLDER_OR_CUSTOM_CATEGORY";
        if (text.Contains("translation evidence", StringComparison.OrdinalIgnoreCase) || analyzed.TranslationConfidence >= 70) return "TRANSLATION_EVIDENCE";
        if (f.Basis.Contains("Lexical", StringComparison.OrdinalIgnoreCase)) return "LEXICAL_CLASSIFIER_DISAGREEMENT";
        if (f.Basis.Contains("Named entity", StringComparison.OrdinalIgnoreCase)) return "ENTITY_CLASSIFIER_DISAGREEMENT";
        if (string.IsNullOrWhiteSpace(analyzed.IntelligenceReason)) return "ANALYZER_OR_PARSER_GAP";
        if (f.EvidenceQuality < 75) return "INSUFFICIENT_INDEPENDENT_EVIDENCE";
        if (conflict) return "TAXONOMY_OR_CLASSIFIER_DISAGREEMENT";
        return "OTHER_REVIEW";
    }

    private static string DetermineDisposition(ModelRecord m, SemanticEvidenceFusionResult f, string cause, bool conflict, string arbitrationDecision)
    {
        if (string.Equals(arbitrationDecision, "REVIEW", StringComparison.OrdinalIgnoreCase)) return "ARBITRATION_REJECTED_CANDIDATE_REVIEW";
        if (IsUnresolvedCategory(f.Category)) return "UNRESOLVED_ENGINE_GAP";
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
        var sb = new StringBuilder("Id,Name,Path,StoredCategory,CandidateCategory,AppliedCategory,ArbitrationDecision,Cause,Disposition,EvidenceQuality,ClassificationConfidence,ReviewRequired,Basis,Evidence\n");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new[]{r.Id.ToString(),r.Name,r.Path,r.StoredCategory,r.CandidateCategory,r.AppliedCategory,r.ArbitrationDecision,r.Cause,r.Disposition,r.EvidenceQuality.ToString(),r.ClassificationConfidence.ToString(),r.ReviewRequired.ToString(),r.Basis,r.Evidence}.Select(Csv)));
        return sb.ToString();
    }

    private static string BuildOutcomesCsv(IEnumerable<RootCauseOutcome> outcomes)
    {
        var sb = new StringBuilder("Id,Name,Path,StoredCategory,CandidateCategory,AppliedCategory,ArbitrationDecision,SemanticType,Subtype,Family,ClassificationConfidence,EvidenceQuality,ReviewRequired,Basis,Evidence,Outcome\n");
        foreach (var r in outcomes)
            sb.AppendLine(string.Join(",", new[]{
                r.Id.ToString(), r.Name, r.Path, r.StoredCategory, r.CandidateCategory, r.AppliedCategory, r.ArbitrationDecision, r.SemanticType,
                r.Subtype, r.Family, r.ClassificationConfidence.ToString(), r.EvidenceQuality.ToString(),
                r.ReviewRequired.ToString(), r.Basis, r.Evidence, r.Outcome
            }.Select(Csv)));
        return sb.ToString();
    }
    private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    private static string BuildReport(
        DateTime created, int catalog, IReadOnlyList<RootCauseRow> rootCauseRows, int conflictReviewRowCount,
        int conflicts, int failed, IReadOnlyList<string> failureDetails,
        IReadOnlyDictionary<string,int> causes, IReadOnlyDictionary<string,int> dispositions, string db,
        string source, int repositoryCountBeforeSync, int activeSnapshotCount, int repositoryCountAfterSync,
        bool synchronizedFromActiveCatalog, bool catalogMismatch,
        int recordsAnalyzed, int resolvedInferences, int unresolvedInferences, int evidenceBearingResults,
        int reviewRequiredResults, int missingInferenceResults, IReadOnlyDictionary<string,int> appliedCategories,
        string outcomeFingerprint, int sourceAnalyzerInvocations, int sourceAnalyzerSkipped,
        long sourceForensicsElapsedMilliseconds, IReadOnlyDictionary<string,int> candidateCategories)
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
        sb.AppendLine($"Processed: {catalog - failed:N0}");
        sb.AppendLine($"Stored classification conflicts: {conflicts:N0}");
        sb.AppendLine($"Records analyzed: {recordsAnalyzed:N0}");
        sb.AppendLine($"Root-cause records: {rootCauseRows.Count:N0}");
        sb.AppendLine($"Conflict/review rows: {conflictReviewRowCount:N0}");
        sb.AppendLine($"Resolved applied categories: {resolvedInferences:N0}");
        sb.AppendLine($"Unresolved after arbitration: {unresolvedInferences:N0}");
        sb.AppendLine($"Evidence-bearing results: {evidenceBearingResults:N0}");
        sb.AppendLine($"Review-required results: {reviewRequiredResults:N0}");
        sb.AppendLine($"Missing inference results: {missingInferenceResults:N0}");
        sb.AppendLine($"Outcome fingerprint: {outcomeFingerprint}");
        sb.AppendLine($"Source analyzer invocations: {sourceAnalyzerInvocations:N0}");
        sb.AppendLine($"Source analyzer skipped: {sourceAnalyzerSkipped:N0}");
        sb.AppendLine($"Source forensic preparation elapsed (ms): {sourceForensicsElapsedMilliseconds:N0}");
        sb.AppendLine($"SOURCE_ANALYZER_INVOCATIONS={sourceAnalyzerInvocations}");
        sb.AppendLine($"SOURCE_ANALYZER_SKIPPED={sourceAnalyzerSkipped}");
        sb.AppendLine($"SOURCE_FORENSICS_ELAPSED_MS={sourceForensicsElapsedMilliseconds}");
        sb.AppendLine("APPLIED CATEGORY DISTRIBUTION (post-arbitration; protected stored categories remain applied)");
        foreach (var x in appliedCategories) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine("CANDIDATE CATEGORY DISTRIBUTION (fusion proposals; not necessarily applied)");
        foreach (var x in candidateCategories) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine($"Failures: {failed:N0}");
        if (failureDetails.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("FAILURE DETAILS");
            foreach (var detail in failureDetails) sb.AppendLine(detail);
        }
        sb.AppendLine(); sb.AppendLine("ROOT CAUSES");
        foreach (var x in causes) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine(); sb.AppendLine("DISPOSITIONS");
        foreach (var x in dispositions) sb.AppendLine($"{x.Key}: {x.Value:N0}");
        sb.AppendLine(); sb.AppendLine("ACCOUNTING");
        sb.AppendLine($"Catalog == Processed + Failures: {catalog == recordsAnalyzed + failed}");
        sb.AppendLine($"Catalog == Resolved + Unresolved + Failures: {catalog == resolvedInferences + unresolvedInferences + failed}");
        sb.AppendLine($"Records analyzed == outcomes exported: {recordsAnalyzed == appliedCategories.Values.Sum()}");
        sb.AppendLine($"Missing inference results == 0: {missingInferenceResults == 0}");
        sb.AppendLine($"Failures + conflict/review rows <= catalog: {failed + conflictReviewRowCount <= catalog}");
        sb.AppendLine($"Unresolved applied categories have root-cause rows: {rootCauseRows.Count(x => IsUnresolvedCategory(x.AppliedCategory)) == unresolvedInferences}");
        sb.AppendLine("MODE: READ-ONLY FORENSIC PASS — no classifications, files, or database records were changed.");
        return sb.ToString();
    }
}
