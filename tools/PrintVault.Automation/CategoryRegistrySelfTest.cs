using PrintVault.Infrastructure;

internal static class CategoryRegistrySelfTest
{
    public static int Run()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PrintVault-CategoryRegistry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            var registryPath = Path.Combine(testRoot, "categories.txt");
            var registry = new CategoryRegistryStore(registryPath);

            Require(registry.Load().Count == 0, "A missing registry should load as empty.");

            registry.Save(new[] { " Functional ", "Custom", "custom", "  " });
            Require(registry.Load().SequenceEqual(new[] { "Functional", "Custom" }),
                "Saving should trim names and deduplicate case-insensitively while preserving order.");

            var legacy = new HashSet<string>(new[] { "LEGACY" }, StringComparer.OrdinalIgnoreCase);
            registry.Save(new[] { "Functional", "Legacy", "Custom" });
            Require(registry.Count(legacy) == 1, "Registry count should match legacy categories case-insensitively.");
            Require(registry.Remove(legacy) == 1, "Registry removal should report the removed entry.");
            Require(registry.Load().SequenceEqual(new[] { "Functional", "Custom" }),
                "Registry removal should retain unrelated category names and their order.");
            Require(registry.Remove(legacy) == 0, "Removing absent categories should report zero.");

            RequireThrows<InvalidOperationException>(() => registry.Save(ThrowDuringEnumeration()),
                "A failed save should surface the original write error.");
            Require(registry.Load().SequenceEqual(new[] { "Functional", "Custom" }),
                "A failed save must leave the previous registry intact.");
            Require(!Directory.EnumerateFiles(testRoot, "*.tmp").Any(),
                "A failed save must remove its temporary file.");

            Console.WriteLine("CATEGORY_REGISTRY_SELFTEST=PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CATEGORY_REGISTRY_SELFTEST=FAIL " + ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }

    private static IEnumerable<string> ThrowDuringEnumeration()
    {
        yield return "Replacement";
        throw new InvalidOperationException("Intentional self-test failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
