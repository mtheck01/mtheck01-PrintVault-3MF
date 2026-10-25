using PrintVault.Core;
using PrintVault.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class SemanticCueSelfTest
{
    private sealed record Case(string Name, string ExpectedCategory, string ExpectedSubtype,
        IReadOnlyList<SourceSemanticEvidenceItem>? SourceItems = null);

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
            new Case("Piper PA-28 Plane Kit Card", "Vehicles", "Vehicle"),
            new Case("KPop Demon Hunters Shadow Box", "Art & Decor", "Art / Display"),
            new Case("Scream Knife display prop", "Props & Accessories", "Cosplay / Prop"),
            new Case("M416突击步枪", "Props & Accessories", "Cosplay / Prop"),
            new Case("A10攻击机", "Vehicles", "Aircraft / Airplane"),
            new Case("红桃K国王3D浮雕卡牌冰箱贴", "Art & Decor", "Art / Display"),
            new Case("可活动美西螈钥匙扣", "Keychains", "Keychain / Key Tag"),
            new Case("木乃伊", "Figures & Characters", "Creature"),
            new Case("大号龟屋A款", "Household", "Pet Habitat / Hide",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "乌龟躲避屋（爬行动物躲避处）") }),
            new Case("Fries Cup BAMBU", "Functional", "Functional Artifact / Role",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "French Fries Bag Clip") }),
            new Case("hdx tote clicker", "Toys & Games", "Board Game / Toy"),
            new Case("A100", "Uncategorized", ""),
            // Near-matches must not inherit the highly specific reviewed cue.
            new Case("Old Testament 3D printed frame", "Uncategorized", ""),
            new Case("Shadow of a cardboard box", "Uncategorized", ""),
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
                SourceSemanticEvidence = "",
                SourceSemanticEvidenceItems = test.SourceItems ?? Array.Empty<SourceSemanticEvidenceItem>()
            });
            var categoryPass = string.Equals(result.Model.Category, test.ExpectedCategory, StringComparison.OrdinalIgnoreCase);
            var subtypePass = test.ExpectedSubtype.Length == 0 || string.Equals(result.Model.Subtype, test.ExpectedSubtype, StringComparison.OrdinalIgnoreCase);
            var pass = categoryPass && subtypePass;
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {test.Name} => {result.Model.Category} / {result.Model.Subtype}; expected {test.ExpectedCategory}{(test.ExpectedSubtype.Length == 0 ? "" : " / " + test.ExpectedSubtype)}");
        }

        var roleNegatives = new[]
        {
            new ModelRecord
            {
                Name = "Dale+Earnhardt+3+White.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[]
                {
                    new SourceSemanticEvidenceItem("metadata.title", "Dale Earnhardt 3 Stand Up"),
                    new SourceSemanticEvidenceItem("metadata.description", "It can be stood up anywhere in your home or man cave.")
                }
            },
            new ModelRecord
            {
                Name = "STORK&STAND.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[]
                {
                    new SourceSemanticEvidenceItem("metadata.title", "Ultrasound Picture Frame"),
                    new SourceSemanticEvidenceItem("metadata.description", "The leg fits tightly into the base.")
                }
            }
        };
        foreach (var negative in roleNegatives)
        {
            var result = pipeline.Analyze(negative);
            var pass = !string.Equals(result.Model.Category, "Functional", StringComparison.OrdinalIgnoreCase);
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: role evidence does not override primary title for {negative.Name}; category={result.Model.Category}");
        }

        var languageService = new LanguageIntelligenceService();
        var untranslated = languageService.Analyze("未支持词语内容");
        var translationSeparationPass = untranslated.Language == "Chinese" && untranslated.LanguageConfidence >= 70 &&
                                        untranslated.TranslationCoverage == 0 && !untranslated.TranslationApplied;
        Console.WriteLine($"{(translationSeparationPass ? "PASS" : "FAIL")}: language identification remains separate from unchanged-title translation coverage; language={untranslated.LanguageConfidence} coverage={untranslated.TranslationCoverage} applied={untranslated.TranslationApplied}");
        if (!translationSeparationPass) failed++;

        var translated = languageService.Analyze("M416突击步枪");
        var translatedPass = translated.TranslationApplied && translated.TranslationCoverage >= 70 &&
                             translated.TranslatedText.Contains("assault rifle", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"{(translatedPass ? "PASS" : "FAIL")}: mixed-script weapon title is translated with measured coverage; language={translated.LanguageConfidence} coverage={translated.TranslationCoverage} translated={translated.TranslatedText}");
        if (!translatedPass) failed++;

        var repositoryPass = VerifyLanguageConfidencePersistence();
        Console.WriteLine($"{(repositoryPass ? "PASS" : "FAIL")}: language confidence persists independently from translation coverage");
        if (!repositoryPass) failed++;

        Console.WriteLine($"SEMANTIC_CUE_SELFTEST={((failed == 0) ? "PASS" : "FAIL")} CASES={cases.Length} FAILED={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static bool VerifyLanguageConfidencePersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PrintVaultLanguageEvidence-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "library.db");
        try
        {
            var repository = new LibraryRepository(path);
            repository.Upsert(new ModelRecord
            {
                Path = Path.Combine(directory, "model.3mf"), Name = "model.3mf",
                OriginalLanguage = "Chinese", LanguageConfidence = 88,
                TranslatedTitle = "assault rifle", TranslationConfidence = 76,
                TranslationEvidence = "translation_applied=true; translation_coverage_pct=76"
            });
            var restored = repository.Get(Path.Combine(directory, "model.3mf"));
            return restored is not null && restored.LanguageConfidence == 88 && restored.TranslationConfidence == 76;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
