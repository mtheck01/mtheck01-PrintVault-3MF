using System.Text.Json;
using PrintVault.Core;
using PrintVault.Infrastructure;

public sealed record RepairReportDocument(
    int? Catalog,
    int? Processed,
    int? RecordsAnalyzed,
    int? Failed,
    RootCauseRow[]? Rows);

public static class RepairReportContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RepairReportDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<RepairReportDocument>(json, JsonOptions)
        ?? throw new JsonException("Whole-library report deserialized to null.");

    public static string? Validate(RepairReportDocument report, IReadOnlyCollection<ModelRecord> models, int? expectedCatalog)
    {
        var databaseCatalog = models.Count;
        if (report.Catalog != databaseCatalog || report.Processed != databaseCatalog)
            return "REPORT_CATALOG_MISMATCH";
        if (expectedCatalog.HasValue && databaseCatalog != expectedCatalog.Value)
            return "EXPECTED_CATALOG_MISMATCH";
        if (report.RecordsAnalyzed != databaseCatalog)
            return "REPORT_ANALYSIS_COUNT_MISMATCH";
        if (report.Failed != 0)
            return "REPORT_CONTAINS_ANALYSIS_FAILURES";
        if (report.Rows is null)
            return "REPORT_ROWS_MISSING";

        var byId = models.ToDictionary(model => model.Id);
        var seenIds = new HashSet<long>();
        foreach (var row in report.Rows)
        {
            if (row is null || row.Id <= 0 || string.IsNullOrWhiteSpace(row.Path) ||
                row.CandidateCategory is null || row.StoredCategory is null ||
                row.Disposition is null || row.ArbitrationDecision is null ||
                row.Basis is null || row.Evidence is null)
                return "REPORT_ROW_INVALID";
            if (!seenIds.Add(row.Id))
                return "REPORT_ROW_DUPLICATE_ID";
            if (!byId.TryGetValue(row.Id, out var model))
                return "REPORT_ROW_UNKNOWN_ID";

            try
            {
                var reportPath = Path.GetFullPath(row.Path);
                var modelPath = Path.GetFullPath(model.Path);
                if (!string.Equals(reportPath, modelPath, StringComparison.OrdinalIgnoreCase))
                    return "REPORT_ROW_PATH_MISMATCH";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "REPORT_ROW_INVALID_PATH";
            }

            if (!string.Equals(row.StoredCategory, model.Category, StringComparison.OrdinalIgnoreCase))
                return "STALE_REPORT_RECORD";
        }

        return null;
    }
}

public static class RepairReportContractSelfTest
{
    public static int Run()
    {
        var expectedRow = new RootCauseRow(
            17, "test model", "D:\\3d print files test\\test.3mf", "Uncategorized", "Vehicles",
            "semantic candidate", "RESOLUTION_CANDIDATE", 30, 91, false,
            "Object role evidence: aircraft", "Lexical cue: bomber", "Vehicles", "ACTIONABLE");

        // Match the producer's anonymous-object JSON contract: root names are camelCase,
        // while positional record fields such as CandidateCategory retain PascalCase.
        var sourceJson = JsonSerializer.Serialize(new
        {
            catalog = 1,
            processed = 1,
            recordsAnalyzed = 1,
            failed = 0,
            rows = new[] { expectedRow }
        });

        var parsed = RepairReportContract.Deserialize(sourceJson);
        var models = new[]
        {
            new ModelRecord { Id = expectedRow.Id, Path = expectedRow.Path, Category = "Uncategorized" }
        };
        var validation = RepairReportContract.Validate(parsed, models, expectedCatalog: 1);
        if (validation is not null || parsed.Rows?.Length != 1 ||
            parsed.Rows[0].Id != expectedRow.Id ||
            parsed.Rows[0].CandidateCategory != expectedRow.CandidateCategory ||
            parsed.Rows[0].ArbitrationDecision != "ACTIONABLE")
        {
            Console.Error.WriteLine($"FAIL: repair report contract round-trip; validation={validation}");
            return 1;
        }

        var mismatch = RepairReportContract.Validate(parsed, new[]
        {
            models[0], new ModelRecord { Id = 18, Path = "D:\\3d print files test\\other.3mf", Category = "Uncategorized" }
        }, expectedCatalog: 2);
        if (mismatch != "REPORT_CATALOG_MISMATCH")
        {
            Console.Error.WriteLine("FAIL: repair report catalog mismatch must fail closed");
            return 1;
        }

        var failedReport = parsed with { Failed = 1 };
        if (RepairReportContract.Validate(failedReport, models, expectedCatalog: 1) != "REPORT_CONTAINS_ANALYSIS_FAILURES")
        {
            Console.Error.WriteLine("FAIL: repair report with analysis failures must fail closed");
            return 1;
        }

        var wrongPathReport = parsed with { Rows = new[] { expectedRow with { Path = "D:\\3d print files test\\wrong.3mf" } } };
        if (RepairReportContract.Validate(wrongPathReport, models, expectedCatalog: 1) != "REPORT_ROW_PATH_MISMATCH")
        {
            Console.Error.WriteLine("FAIL: report rows must target the same database record path");
            return 1;
        }

        var staleCategoryReport = parsed with { Rows = new[] { expectedRow with { StoredCategory = "Figures & Characters" } } };
        if (RepairReportContract.Validate(staleCategoryReport, models, expectedCatalog: 1) != "STALE_REPORT_RECORD")
        {
            Console.Error.WriteLine("FAIL: report rows must match the current stored category before backup/writeback");
            return 1;
        }

        Console.WriteLine("PASS: whole-library report camelCase envelope and CandidateCategory row round-trip");
        Console.WriteLine("PASS: repair report coverage, row identity, path, stale category, and failure gates fail closed");
        Console.WriteLine("REPAIR_REPORT_CONTRACT_SELFTEST=PASS");
        return 0;
    }
}
