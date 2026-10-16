using PrintVault.Core;
using PrintVault.Infrastructure;

internal static class SemanticCueSelfTest
{
    private sealed record Case(string Name, string ExpectedCategory, string ExpectedSubtype);

    public static int Run()
    {
        var cases = new[]
        {
            new Case("67 Buddy articulated shelf sitter", "Figures & Characters", "Tabletop / Articulated Figure"),
            new Case("Ladies 0.08 tabletop gaming figures", "Figures & Characters", "Tabletop / Articulated Figure"),
            new Case("Old Testament 2 HueForge", "HueForge", "Religious HueForge"),
            // Match the actual reviewed filename: adding generic words here can trigger
            // an entity match that the real filename does not contain.
            new Case("s3viking", "Vehicles", "Aircraft / Bomber"),
            new Case("Unicorn Chomper earrings", "Art & Decor", "Wearable / Earrings"),
            new Case("Candy Ghost candy bowl", "Household", "Candy Bowl"),
            // Near-matches must not inherit the highly specific reviewed cue.
            new Case("Old Testament 3D printed frame", "Uncategorized", ""),
            // A role suffix after an action connector describes use instructions, not the artifact.
            new Case("Harley Wall Decor small drops of glue required to mount", "Art & Decor", "Art / Display"),
        };

        var pipeline = new ModelIntelligencePipeline();
        var failed = 0;
        foreach (var test in cases)
        {
            var result = pipeline.Analyze(new ModelRecord
            {
                Name = test.Name,
                Category = "Uncategorized",
                SemanticType = "Unknown",
                IntelligenceReason = "",
                SourceSemanticEvidence = ""
            });
            var categoryPass = string.Equals(result.Model.Category, test.ExpectedCategory, StringComparison.OrdinalIgnoreCase);
            var subtypePass = test.ExpectedSubtype.Length == 0 || string.Equals(result.Model.Subtype, test.ExpectedSubtype, StringComparison.OrdinalIgnoreCase);
            var pass = categoryPass && subtypePass;
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {test.Name} => {result.Model.Category} / {result.Model.Subtype}; expected {test.ExpectedCategory}{(test.ExpectedSubtype.Length == 0 ? "" : " / " + test.ExpectedSubtype)}");
        }

        Console.WriteLine($"SEMANTIC_CUE_SELFTEST={((failed == 0) ? "PASS" : "FAIL")} CASES={cases.Length} FAILED={failed}");
        return failed == 0 ? 0 : 1;
    }
}
