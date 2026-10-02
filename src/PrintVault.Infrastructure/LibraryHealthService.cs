using System;
using System.Collections.Generic;
using System.Linq;

using PrintVault.Core;

namespace PrintVault.Infrastructure;

public sealed class LibraryHealthService
{
    private readonly LibraryRepository repo;
    private readonly LibraryStateStore state;
    public LibraryHealthService(LibraryRepository repo, LibraryStateStore state) { this.repo = repo; this.state = state; }

    public LibraryHealthSnapshot Analyze(IReadOnlyCollection<string> legacyCategories)
    {
        var all = repo.GetAll();
        var groups = all.Where(x => !string.IsNullOrWhiteSpace(x.Hash)).GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        var duplicateBytes = groups.Sum(g => g.Skip(1).Sum(x => x.Size));
        var legacyNames = new HashSet<string>(legacyCategories, StringComparer.OrdinalIgnoreCase);
        var legacy = all.Select(x => x.Category).Where(x => legacyNames.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var emptyLegacy = legacyNames.Count(x => !all.Any(m => string.Equals(m.Category, x, StringComparison.OrdinalIgnoreCase)));
        return new(all.Count, all.Count(x => x.Favorite), groups.Count, groups.Sum(g => g.Count()), all.Count(x => !x.PrintReady || x.IntelligenceScore < .5), all.Count(x => !x.HasThumbnail), legacy, emptyLegacy, all.Sum(x => x.Size), duplicateBytes, state.Collections.Count, state.LearnedRules.Count, state.Journal.Count);
    }

    public IReadOnlyList<DuplicateGroupSnapshot> Duplicates()
        => repo.GetAll().Where(x => !string.IsNullOrWhiteSpace(x.Hash)).GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => new DuplicateGroupSnapshot(g.Key, g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), g.Sum(x => x.Size))).OrderByDescending(x => x.Models.Count).ToList();
}
