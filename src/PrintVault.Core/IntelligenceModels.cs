using System;
using System.Collections.Generic;

namespace PrintVault.Core;

public sealed record LibraryHealthSnapshot(
    int Models,
    int Favorites,
    int DuplicateGroups,
    int DuplicateFiles,
    int NeedsReview,
    int MissingThumbnails,
    int LegacyCategories,
    int EmptyLegacyCategories,
    long Bytes,
    long DuplicateBytes,
    int Collections,
    int LearnedRules,
    int JournalEntries);

public sealed record DuplicateGroupSnapshot(string Hash, IReadOnlyList<ModelRecord> Models, long Bytes);
public sealed record LearnedRule(string Token, string Category, int Uses, DateTime LastUsedUtc);
public sealed record CollectionRecord(string Name, IReadOnlyList<string> Paths);
public sealed record OperationJournalEntry(Guid Id, DateTime Utc, string Operation, string Summary, string? ManifestPath = null, bool UndoAvailable = false);
