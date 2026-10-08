using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.Json;
using PrintVault.Core;

namespace PrintVault.Infrastructure;

/// <summary>Persistent local state for collections, learned corrections and operation history.</summary>
public sealed class LibraryStateStore
{
    private sealed class State
    {
        public Dictionary<string,List<string>> Collections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<LearnedRule> LearnedRules { get; set; } = new();
        public List<OperationJournalEntry> Journal { get; set; } = new();
    }

    private readonly string path;
    private readonly object gate = new();
    private State state;

    public LibraryStateStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault");
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "library_state.json");
        state = Load();
    }

    public IReadOnlyList<CollectionRecord> Collections
    {
        get
        {
            lock (gate)
                return state.Collections
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new CollectionRecord(x.Key, x.Value.ToList()))
                    .ToList();
        }
    }

    public IReadOnlyList<LearnedRule> LearnedRules
    {
        get { lock (gate) return state.LearnedRules.ToList(); }
    }

    public IReadOnlyList<OperationJournalEntry> Journal
    {
        get { lock (gate) return state.Journal.ToList(); }
    }

    public void CreateCollection(string name)
    {
        name = NormalizeName(name);
        lock (gate) { if (!state.Collections.ContainsKey(name)) state.Collections[name] = new(); Save(); }
    }

    public void DeleteCollection(string name)
    {
        lock (gate) { state.Collections.Remove(name); Save(); }
    }

    public void SetCollectionMembers(string name, IEnumerable<string> paths)
    {
        name = NormalizeName(name);
        lock (gate) { state.Collections[name] = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Save(); }
    }

    public IReadOnlySet<string> GetCollectionMembers(string name)
    {
        lock (gate)
            return state.Collections.TryGetValue(name, out var p)
                ? p.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void RecordLearnedRule(string token, string category)
    {
        token = NormalizeToken(token); if (token.Length < 3 || string.IsNullOrWhiteSpace(category)) return;
        lock (gate)
        {
            var i = state.LearnedRules.FindIndex(x => string.Equals(x.Token, token, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { var old = state.LearnedRules[i]; state.LearnedRules[i] = old with { Uses = old.Uses + 1, LastUsedUtc = DateTime.UtcNow }; }
            else state.LearnedRules.Add(new LearnedRule(token, category.Trim(), 1, DateTime.UtcNow));
            Save();
        }
    }

    public void RecordJournal(string operation, string summary, string? manifestPath = null, bool undoAvailable = false)
    {
        lock (gate)
        {
            state.Journal.Insert(0, new OperationJournalEntry(Guid.NewGuid(), DateTime.UtcNow, operation, summary, manifestPath, undoAvailable));
            if (state.Journal.Count > 200) state.Journal.RemoveRange(200, state.Journal.Count - 200);
            Save();
        }
    }

    public void RemoveJournal(Guid id) { lock (gate) { state.Journal.RemoveAll(x => x.Id == id); Save(); } }

    private State Load()
    {
        if (!File.Exists(path)) return new();

        try
        {
            return JsonSerializer.Deserialize<State>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (Exception ex)
        {
            // Never silently replace user state with an empty state. Preserve the
            // corrupt artifact so collections, learned rules and operation history
            // remain recoverable while allowing PrintVault to start cleanly.
            var recovery = path + $".corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json";
            try
            {
                File.Move(path, recovery, false);
            }
            catch (Exception moveEx)
            {
                throw new InvalidDataException(
                    $"PrintVault state is corrupt and could not be preserved for recovery: {path}",
                    new AggregateException(ex, moveEx));
            }

            return new();
        }
    }

    private void Save()
    {
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    private static string NormalizeName(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid collection name.");
        return value;
    }
    private static string NormalizeToken(string value) => new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed class LearningService
{
    private readonly LibraryStateStore state;
    public LearningService(LibraryStateStore state) => this.state = state;

    public void RecordCorrection(ModelRecord model, string category)
    {
        foreach (var token in Tokenize(model.Name)) state.RecordLearnedRule(token, category);
    }

    public string? Suggest(ModelRecord model, IReadOnlyCollection<string> validCategories)
    {
        var tokens = Tokenize(model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = state.LearnedRules
            .Where(r => validCategories.Contains(r.Category, StringComparer.OrdinalIgnoreCase) && tokens.Contains(r.Token))
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Category = g.Key, Score = g.Sum(x => Math.Min(5, x.Uses)) })
            .OrderByDescending(x => x.Score).ToList();
        return candidates.Count > 0 && (candidates.Count == 1 || candidates[0].Score > candidates[1].Score + 2) ? candidates[0].Category : null;
    }

    private static IEnumerable<string> Tokenize(string name)
        => Path.GetFileNameWithoutExtension(name).Split(new[] {' ', '-', '_', '.', '(', ')', '[', ']', '+'}, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => new string(x.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())).Where(x => x.Length >= 3);
}
