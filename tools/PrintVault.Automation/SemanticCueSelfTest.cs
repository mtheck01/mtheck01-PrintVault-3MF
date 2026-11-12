using System.IO.Compression;
using System.Text;
using PrintVault.Core;
using PrintVault.Infrastructure;
using Microsoft.Data.Sqlite;

internal static class SemanticCueSelfTest
{
    private sealed record Case(string Name, string ExpectedCategory, string ExpectedSubtype,
        IReadOnlyList<SourceSemanticEvidenceItem>? SourceItems = null,
        bool RequiresDescriptionRoleEvidence = false);

    public static int Run()
    {
        var cases = new[]
        {
            new Case("67 Buddy articulated shelf sitter", "Figures & Characters", "Tabletop / Articulated Figure"),
            new Case("67.3mf", "Figures & Characters", "Tabletop / Articulated Figure",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "‘67’ – The Number That Broke The Internet") }),
            new Case("conejo+1+color.3mf", "Figures & Characters", "Tabletop / Articulated Figure",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Flexi little bunnies (no AMS needed)") }),
            new Case("Ladies 0.08 tabletop gaming figures", "Figures & Characters", "Tabletop / Articulated Figure"),
            new Case("Old Testament 2 HueForge", "HueForge", "Religious HueForge"),
            // Match the actual reviewed filename: adding generic words here can trigger
            // an entity match that the real filename does not contain.
            new Case("s3viking", "Vehicles", "Aircraft / Bomber"),
            new Case("Unicorn Chomper earrings", "Art & Decor", "Wearable / Earrings"),
            new Case("Candy Ghost candy bowl", "Household", "Candy Bowl"),
            new Case("20+mm+dvojča.3mf", "Toys & Games", "Fidget Spinner / Dual Ring",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "ANTISTRESS DOUBLE RING") }),
            new Case("ALL PARTS BOB (BIG)_ANYCUBIC.3mf", "Figures & Characters", "Big Ghost"),
            new Case("ArmySkullBulletholesKS1.3mf", "Art & Decor", "Skull Bullethole Helmet"),
            new Case("Cancer+Ribbon.3mf", "Household", "Drinkware Accessory / Straw Topper",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Breast Cancer Awareness Ribbon Stanley StrawTopper") }),
            new Case("ChefRat.3mf", "Figures & Characters", "Ratatouille Character",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Ratatouille rat mini flexi") }),
            new Case("Clickel+the+Clicking+Pickle+v1.3mf", "Toys & Games", "Fidget Clicker",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Clickel the clicking pickle") }),
            new Case("CoilbladeWyrm.3mf", "Toys & Games", "Dragon Figure"),
            new Case("Cute+Corn.3mf", "Figures & Characters", "Mini Corn Figure"),
            new Case("Dale+Earnhardt+3+White.3mf", "Art & Decor", "Light Box",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Dale Earnhardt 3 Stand Up") }),
            new Case("Death+Star+Final.3mf", "Art & Decor", "Decorative Model",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Death Star") }),
            new Case("Dispenserfull.3mf", "Toys & Games", "TF2 Dispenser Model",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Desk Dispenser TF2") }),
            new Case("EE-3 3MF (For Bambu Printers).3mf", "Props & Accessories", "Blaster / Sci-Fi Prop"),
            new Case("F+Bomb+Explosion+H,+P,+A1.3mf", "Art & Decor", "Novelty Explosion Figure",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Dropping F Bomb Explosion") }),
            new Case("KC-135R+Stratotanker.3mf", "Vehicles", "Aircraft / Tanker"),
            new Case("harley+magnet.3mf", "Art & Decor", "Decorative Magnet",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Harley Davidson Magnet") }),
            new Case("gridwallhooks_bottom_5cm.3mf", "Tools & Workshop", "Gridwall Hanger"),
            new Case("Maritimer_Bilderrahmen.3mf", "Art & Decor", "Picture Frame / Shadowbox",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Maritimer Bilderrahmen mit Motiven") }),
            new Case("opaque-design-a.3mf", "Art & Decor", "Light Box",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "The Intimidator, #3 Dale Earnhardt. Lightbox I designed. Enjoy.") }, true),
            new Case("opaque-design-b.3mf", "Art & Decor", "Picture Frame / Shadowbox",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "Here is my frame for an ultrasound photo. The leg fits into the base.") }, true),
            new Case("opaque-design-c.3mf", "Art & Decor", "Decorative Magnet",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "Simple refrigerator style magnet I created for display.") }, true),
            new Case("opaque-design-d.3mf", "Art & Decor", "Art / Display",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "Memorial plaque made for the community event.") }, true),
            new Case("opaque-design-e.3mf", "Tools & Workshop", "Gridwall Hanger",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "I was unhappy with existing grid wall pegs so I designed these.") }, true),
            new Case("opaque-ultrasound-frame.3mf", "Art & Decor", "Picture Frame / Shadowbox",
                new[] { new SourceSemanticEvidenceItem("metadata.description", "Here is my frame for an ultrasound photo.") }, true),
            new Case("Piper PA-28 Plane Kit Card", "Vehicles", "Vehicle"),
            new Case("P-40 Warhawk - Flying Tigers", "Vehicles", "Aircraft"),
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
            new Case("BB-9E.3mf", "Figures & Characters", "Character",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Big Brick BB-9E 500% no support") }),
            new Case("C3PO.3mf", "Figures & Characters", "Character",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Star Wars C-3PO Protocol Droid Edition AMS Ready") }),
            new Case("pyro+TF2+normal.3mf", "Figures & Characters", "Character",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Pyro TF2") }),
            new Case("67 mm sample", "Uncategorized", ""),
            new Case("Internet connection test", "Uncategorized", ""),
            new Case("Lions Hype Chain.3mf", "Uncategorized", "",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Detroit Lions Spinner Chain") }),
            new Case("Bees on Petunias_200x200.3mf", "Uncategorized", "",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Bees on Petunias") }),
            new Case("TF2+TELEPORTER.3mf", "Uncategorized", "",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "TF2 Teleporter") }),
            new Case("sandvich.3mf", "Uncategorized", "",
                new[] { new SourceSemanticEvidenceItem("metadata.title", "Sandvich TF2") }),
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
            var descriptionEvidencePass = !test.RequiresDescriptionRoleEvidence ||
                result.Fusion.Basis.StartsWith("Description artifact-role evidence:", StringComparison.OrdinalIgnoreCase) &&
                result.Fusion.Evidence.Any(e => e.Contains("metadata.description", StringComparison.OrdinalIgnoreCase));
            var pass = categoryPass && subtypePass && descriptionEvidencePass;
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {test.Name} => {result.Model.Category} / {result.Model.Subtype}; expected {test.ExpectedCategory}{(test.ExpectedSubtype.Length == 0 ? "" : " / " + test.ExpectedSubtype)}{(test.RequiresDescriptionRoleEvidence ? $"; description evidence={descriptionEvidencePass}" : "")}");
            if (!pass && result.Fusion is not null)
                Console.WriteLine("  " + string.Join(" | ", result.Fusion.Diagnostics));
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

        var evidenceNegatives = new[]
        {
            new ModelRecord { Name = "Big Bob.3mf", Category = "Uncategorized" },
            new ModelRecord { Name = "TF2 Teleporter.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.title", "TF2 Teleporter") } },
            new ModelRecord { Name = "Sandvich TF2.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.title", "Sandvich TF2") } },
            new ModelRecord { Name = "Frau+Rahmen.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.title", "Schwangere Frau – 3D Druck Deko mit Fotoeinsatz") } },
            new ModelRecord { Name = "Death+Star_200x200.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.title", "Star Wars Death Star") } },
        };
        foreach (var negative in evidenceNegatives)
        {
            var result = pipeline.Analyze(negative);
            var pass = string.Equals(result.Model.Category, "Uncategorized", StringComparison.OrdinalIgnoreCase);
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: insufficient or nearby evidence stays unresolved for {negative.Name}; category={result.Model.Category}");
        }
        foreach (var negative in roleNegatives)
        {
            var result = pipeline.Analyze(negative);
            var pass = !string.Equals(result.Model.Category, "Functional", StringComparison.OrdinalIgnoreCase);
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: role evidence does not override primary title for {negative.Name}; category={result.Model.Category}");
        }

        var descriptionRoleNegatives = new[]
        {
            new ModelRecord { Name = "opaque-background.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.description", "I saw a lightbox in the background at the exhibition.") } },
            new ModelRecord { Name = "opaque-magnet-use.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.description", "Use a refrigerator magnet to attach the photo to the fridge.") } },
            new ModelRecord { Name = "opaque-gridwall-mention.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.description", "I saw grid wall pegs at the market and designed a small display sign.") } },
            new ModelRecord { Name = "opaque-frame-instructions.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.description", "This organizer has a frame for an ultrasound photo in the assembly instructions.") } },
            new ModelRecord { Name = "opaque-mixed-roles.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[] { new SourceSemanticEvidenceItem("metadata.description", "Lightbox I designed. I was unhappy with existing grid wall pegs so I designed these.") } },
            new ModelRecord { Name = "character-keychain.3mf", Category = "Uncategorized",
                SourceSemanticEvidenceItems = new[]
                {
                    new SourceSemanticEvidenceItem("metadata.title", "Dragon Keychain"),
                    new SourceSemanticEvidenceItem("metadata.description", "Memorial plaque made for the community event.")
                } }
        };
        foreach (var negative in descriptionRoleNegatives)
        {
            var result = pipeline.Analyze(negative);
            var expected = negative.Name == "character-keychain.3mf" ? "Keychains" : "Uncategorized";
            var pass = string.Equals(result.Model.Category, expected, StringComparison.OrdinalIgnoreCase);
            if (!pass) failed++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: description role stays bounded for {negative.Name}; category={result.Model.Category}; expected={expected}");
        }

        var descriptionExtractionPass = VerifyDescriptionEvidenceExtraction(pipeline);
        Console.WriteLine($"{(descriptionExtractionPass ? "PASS" : "FAIL")}: 3MF description provenance survives extraction and produces a bounded role result");
        if (!descriptionExtractionPass) failed++;

        var analyzerConflictPass = VerifyAnalyzerConflictArbitration(pipeline);
        Console.WriteLine($"{(analyzerConflictPass ? "PASS" : "FAIL")}: conflicting analyzer and fusion categories are held for review while explicit artifact roles retain precedence");
        if (!analyzerConflictPass) failed++;

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

    private static bool VerifyAnalyzerConflictArbitration(ModelIntelligencePipeline pipeline)
    {
        var conflict = pipeline.Analyze(new ModelRecord
        {
            Name = "frog-model.3mf",
            Category = "Vehicles",
            IntelligenceScore = 0.90,
            IntelligenceReason = "Vehicles: legacy analyzer title match",
            SemanticType = "Vehicle",
            SourceSemanticEvidenceItems = new[]
            {
                new SourceSemanticEvidenceItem("metadata.title", "frog")
            }
        });
        var typedConflict = conflict.Fusion.Conflicts.Any(item =>
            item.Kind == SemanticEvidenceConflictKind.SourceAnalyzerCategoryDisagreement &&
            item.SelectedCategory == "Figures & Characters" && item.ConflictingCategory == "Vehicles");
        var reviewDecision = conflict.StageEvidence.Any(item => item.EndsWith(":REVIEW_CHANNEL_CONFLICT", StringComparison.OrdinalIgnoreCase));
        var preservedCategory = string.Equals(conflict.Model.Category, "Vehicles", StringComparison.OrdinalIgnoreCase);

        var roleOverride = pipeline.Analyze(new ModelRecord
        {
            Name = "opaque-role.3mf",
            Category = "Uncategorized",
            IntelligenceScore = 0.90,
            IntelligenceReason = "Vehicles: legacy analyzer proposal",
            SemanticType = "Vehicle",
            SourceSemanticEvidenceItems = new[]
            {
                new SourceSemanticEvidenceItem("metadata.description", "Lightbox I designed.")
            }
        });
        var roleWins = string.Equals(roleOverride.Model.Category, "Art & Decor", StringComparison.OrdinalIgnoreCase) &&
                       roleOverride.Fusion.Conflicts.Count == 0 &&
                       roleOverride.Fusion.Basis.StartsWith("Description artifact-role evidence:", StringComparison.OrdinalIgnoreCase);

        return typedConflict && reviewDecision && preservedCategory && roleWins;
    }

    private static bool VerifyDescriptionEvidenceExtraction(ModelIntelligencePipeline pipeline)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PrintVaultDescriptionEvidence-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "opaque-design.3mf");
        try
        {
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry("3D/3dmodel.model");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write("<model unit=\"millimeter\"><metadata name=\"title\">Archive marker</metadata><metadata name=\"description\">&lt;p&gt;Lightbox I designed.&lt;/p&gt;</metadata><resources><object id=\"1\" type=\"model\" name=\"archive marker\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"0\"/></vertices><triangles/></mesh></object></resources><build><item objectid=\"1\"/></build></model>");
            }

            var analysis = new ThreeMfAnalyzer().Analyze(path, Path.GetFileName(path), out _);
            var description = analysis.SourceSemanticEvidenceItems.FirstOrDefault(item =>
                item.Field.Equals("metadata.description", StringComparison.OrdinalIgnoreCase));
            if (description is null || description.Text.Contains("<p>", StringComparison.OrdinalIgnoreCase) ||
                !description.Text.Contains("Lightbox I designed", StringComparison.OrdinalIgnoreCase)) return false;

            var result = pipeline.Analyze(new ModelRecord
            {
                Name = Path.GetFileName(path),
                Category = analysis.Category,
                Family = analysis.Family,
                SemanticType = analysis.SemanticType,
                Subtype = analysis.Subtype,
                IntelligenceScore = analysis.Confidence,
                IntelligenceReason = analysis.Reason,
                SourceSemanticEvidence = analysis.SourceSemanticEvidence,
                SourceSemanticEvidenceItems = analysis.SourceSemanticEvidenceItems
            });

            return string.Equals(result.Model.Category, "Art & Decor", StringComparison.OrdinalIgnoreCase) &&
                   result.Fusion.Basis.StartsWith("Description artifact-role evidence:", StringComparison.OrdinalIgnoreCase) &&
                   result.Fusion.Evidence.Any(e => e.Contains("metadata.description", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
