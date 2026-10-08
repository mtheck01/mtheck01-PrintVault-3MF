using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using Forms = System.Windows.Forms;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed class CategoryStat
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public double Percent { get; init; }
}

public partial class MainWindow : Window
{
    private readonly LibraryEngine engine = new();
    private readonly MultilingualMetadataService multilingual = new();
    private readonly MultilingualEntityService entities = new();
    private readonly ModelIntelligencePipeline intelligencePipeline = new();
    private readonly LibraryStateStore stateStore = new();
    private readonly LearningService learning;
    private OrganizationService? organization;
    public BulkObservableCollection<ModelRecord> ModelsSource { get; } = new();
    public ObservableCollection<ModelRecord> GalleryItems { get; } = new();
    public ObservableCollection<ModelRecord> RecentModels { get; } = new();
    public ObservableCollection<CategoryStat> CategoryStats { get; } = new();
    public ICollectionView ModelView { get; }

    private string? root;
    private ModelRecord? active;
    private CancellationTokenSource? scanCts;
    private Task? activeScanTask;
    private long scanGeneration;
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private int galleryPage;
    private const int GalleryPageSize = 60;
    private bool favoritesOnly;
    private readonly List<string> categories = new();
    private string? categoryFilter;
    private string? tagFilter;
    private string? sortProperty;
    private ListSortDirection sortDirection = ListSortDirection.Ascending;
    private bool uiReady;
    private static readonly string ForbiddenProductionLibrary = Path.GetFullPath(@"D:\3d print files").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public MainWindow()
    {
        learning = new LearningService(stateStore);
        InitializeComponent();
        DataContext = this;
        ModelView = CollectionViewSource.GetDefaultView(ModelsSource);
        ModelView.Filter = FilterModel;
        uiReady = true;
        LoadCategories();
        root = LoadRoot();
        if (root != null) organization = new OrganizationService(engine.Repository, root);
    }

    private bool sidebarCollapsed;

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        sidebarCollapsed = !sidebarCollapsed;
        SidebarColumn.Width = new GridLength(sidebarCollapsed ? 76 : 250);
        Sidebar.Padding = sidebarCollapsed ? new Thickness(10, 16, 10, 16) : new Thickness(14, 16, 14, 16);
        SidebarBrand.Visibility = sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarFooter.Visibility = sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleButton.Content = sidebarCollapsed ? "›" : "‹";
        SidebarToggleButton.ToolTip = sidebarCollapsed ? "Expand navigation" : "Collapse navigation";

        // Keep the navigation usable in compact mode: collapse the grouped menu and
        // expose a clear affordance to restore the full navigation. The main content
        // remains unchanged and no library state is affected.
        foreach (var child in FindVisualChildren<Expander>(Sidebar))
            child.Visibility = sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Engineering rule: startup must never auto-select a library.
        // A previously cleared/absent library leaves the application idle until
        // the user explicitly chooses a library from Settings / Library.
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            root = null;
            organization = null;
            Status.Text = "No library selected. Choose Settings / Library to select a test library.";
            return;
        }

        organization = new OrganizationService(engine.Repository, root);
        StartScan();
    }

    private string? LoadRoot()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "root.txt");
        if (!File.Exists(p)) return null;
        var saved = File.ReadAllText(p).Trim();
        if (!Directory.Exists(saved) || IsForbiddenProductionLibrary(saved)) return null;
        return saved;
    }

    private static bool IsForbiddenProductionLibrary(string path)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalized, ForbiddenProductionLibrary, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(ForbiddenProductionLibrary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveRoot()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault");
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "root.txt"), root ?? "");
    }

    private void LoadCategories()
    {
        categories.Clear();
        categories.AddRange(BuiltInCategories.All);
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "categories.txt");
        if (File.Exists(p))
            foreach (var c in File.ReadAllLines(p).Select(x => x.Trim())
                .Where(x => x.Length > 0 && !IsLegacyCategory(x) && !categories.Contains(x, StringComparer.OrdinalIgnoreCase)))
                categories.Add(c);
        foreach (var c in engine.Repository.GetAll().Select(x => x.Category)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !IsLegacyCategory(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
            if (!categories.Contains(c, StringComparer.OrdinalIgnoreCase)) categories.Add(c);
    }

    private static bool IsLegacyCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        var value = category.Trim();
        if (value is
            "02_Functional" or "02_Household" or
            "03_Automotive" or "07_Automotive" or "Automotive" or
            "03_Decor" or "06_Decorative" or "Decor" or "Decorative" or
            "04_Figures" or "Figures" or
            "05_Game_Models" or "05_Gaming" or "Gaming" or "Game Models" or
            "06_Cosplay" or "Cosplay" or
            "08_Aviation" or "09_Aircraft" or "Aviation" or "Aircraft" or
            "09_Models" or "Models" or
            "07_Multi_Color" or "10_Multi_Color" or "Multi_Color" or "Multi-Color" or
            "08_Test_Print" or "11_Test_Print" or "Test_Print" or "Test Print" or "Test Prints" or
            "99_Other" or "Other" or "Needs Review" or
            "Soap Holders" or "test eng 8.6")
            return true;

        var i = 0;
        while (i < value.Length && char.IsDigit(value[i])) i++;
        return i > 0 && i < value.Length && (value[i] == '_' || value[i] == '-' || value[i] == ' ');
    }

    private void SaveCategories()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault");
        Directory.CreateDirectory(d);
        File.WriteAllLines(Path.Combine(d, "categories.txt"), categories.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void ChooseRoot()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose your 3MF library folder (the production library is protected in this engineering build)",
            UseDescriptionForTitle = true,
            SelectedPath = root != null && Directory.Exists(root) ? root :
                (Directory.Exists(@"D:\3d print files test") ? @"D:\3d print files test" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        if (IsForbiddenProductionLibrary(dialog.SelectedPath))
        {
            MessageBox.Show(this, $"The production library is protected in PrintVault {AppVersion.Version} Engineering.\n\nUse D:\\3d print files test or another test/library copy.", "Production Library Protected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SetLibraryRoot(dialog.SelectedPath);
    }

    private void SetLibraryRoot(string selectedPath)
    {
        root = Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        SaveRoot();
        organization = new OrganizationService(engine.Repository, root);
        StartScan();
    }

    private async Task ClearLibraryAsync()
    {
        // Invalidate any in-flight scan before waiting for it. This prevents a scan
        // that finishes during Clear Library from repopulating the UI or repository.
        Interlocked.Increment(ref scanGeneration);
        scanCts?.Cancel();
        var pendingScan = activeScanTask;
        if (pendingScan != null && !pendingScan.IsCompleted)
        {
            try { await pendingScan; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error("Library clear scan shutdown failed", ex); }
        }
        scanCts?.Dispose();
        scanCts = null;
        activeScanTask = null;

        root = null;
        organization = null;
        SaveRoot();
        // Keep an automatic database recovery point before this destructive command.
        // The physical library is untouched, but catalog metadata must remain recoverable.
        engine.Repository.BackupDatabase();
        engine.Repository.ClearAllModels();
        Models.UnselectAll();
        active = null;
        ModelsSource.ReplaceAll(Array.Empty<ModelRecord>());
        GalleryItems.Clear();
        RecentModels.Clear();
        CategoryStats.Clear();
        Status.Text = "No library selected. Choose Settings / Library to select a test library.";
        Progress.Value = 0;
        ScanProgressText.Text = "Ready";
        Dashboard.Visibility = Visibility.Visible;
        Models.Visibility = Visibility.Collapsed;
        GalleryHost.Visibility = Visibility.Collapsed;
    }

    private void StartScan()
    {
        activeScanTask = ScanAsync();
    }

    private async Task ScanAsync()
    {
        var generation = Volatile.Read(ref scanGeneration);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Status.Text = "Choose a library folder in Settings.";
            return;
        }

        // Never allow two scans to update the same CollectionView concurrently.
        if (!scanGate.Wait(0))
        {
            scanCts?.Cancel();
            Status.Text = "Stopping the current scan…";
            return;
        }

        try
        {
            scanCts?.Cancel();
            scanCts?.Dispose();
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;
            var mode = GetSelectedScanMode();
            ScanModeStatus.Text = mode switch
            { ScanMode.Quick => "Quick", ScanMode.Turbo => "Turbo", _ => "Deep" };
            Progress.Value = 0;
            ScanProgressText.Text = "Discovering .3mf files…";
            Status.Text = mode switch
            {
                ScanMode.Quick => "Quick scanning: indexing new and changed files…",
                ScanMode.Turbo => "Turbo scanning: analyzing new and changed files…",
                _ => "Deep scanning: re-analyzing the entire library…"
            };
            try
            {
                var lastFailed = 0;
                var progress = new Progress<ScanProgress>(p =>
                {
                    lastFailed = p.Failed;
                    Progress.Value = p.Percent;
                    ScanProgressText.Text = p.Discovered <= 0
                        ? "No .3mf files discovered"
                        : $"Found {p.Discovered:N0} • Indexed {p.Indexed:N0} • Failed {p.Failed:N0} • {p.Percent}%";
                });
                var rows = await engine.ScanAsync(new[] { root }, mode, token, progress);
                token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref scanGeneration)) return;

                // Replace the bound collection with ONE Reset notification.
                // Do not use CollectionView.DeferRefresh() while mutating the source;
                // WPF can throw when Current is queried/changed during deferred refresh.
                var selectedModel = active ?? Models.SelectedItem as ModelRecord;
                Models.UnselectAll();
                active = null;
                ModelsSource.ReplaceAll(rows);

                var restored = selectedModel == null ? null :
                    ModelsSource.FirstOrDefault(x => string.Equals(x.Path, selectedModel.Path, StringComparison.OrdinalIgnoreCase));
                if (restored != null) Select(restored);

                UpdateDashboard();
                UpdateGalleryPage();
                ScanProgressText.Text = $"Found {ModelsSource.Count:N0} • Indexed {ModelsSource.Count:N0} • Failed {lastFailed:N0} • 100%";
                Status.Text = lastFailed == 0
                    ? $"{ModelsSource.Count:N0} models indexed • thumbnails cached • ready"
                    : $"{ModelsSource.Count:N0} models indexed • {lastFailed:N0} files failed and were preserved/retried safely";
            }
            catch (OperationCanceledException) { ScanProgressText.Text = "Scan canceled"; Status.Text = "Scan canceled"; }
            catch (Exception ex) { Error("Scan failed", ex); }
        }
        finally
        {
            scanCts?.Dispose();
            scanCts = null;
            scanGate.Release();
        }
    }

    private bool FilterModel(object obj)
    {
        if (obj is not ModelRecord m) return false;
        if (favoritesOnly && !m.Favorite) return false;
        if (!string.IsNullOrWhiteSpace(categoryFilter) && !string.Equals(m.Category, categoryFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(tagFilter) && !SplitTags(m.Tags).Any(x => string.Equals(x, tagFilter, StringComparison.OrdinalIgnoreCase))) return false;
        if (PrintMethodFilter != null && PrintMethodFilter.SelectedIndex > 0) { var wanted = ((ComboBoxItem)PrintMethodFilter.SelectedItem).Content?.ToString() ?? ""; if (!string.Equals(m.PrintMethod, wanted, StringComparison.OrdinalIgnoreCase)) return false; }
        if (SpecialFilter != null && SpecialFilter.SelectedIndex > 0) { var wanted = ((ComboBoxItem)SpecialFilter.SelectedItem).Content?.ToString() ?? ""; if (wanted == "Other" ? (string.IsNullOrWhiteSpace(m.SpecialType) || string.Equals(m.SpecialType, "HueForge", StringComparison.OrdinalIgnoreCase) || string.Equals(m.SpecialType, "Keychain", StringComparison.OrdinalIgnoreCase)) : !string.Equals(m.SpecialType, wanted, StringComparison.OrdinalIgnoreCase)) return false; }
        var q = Search.Text.Trim();
        if (q.Length == 0) return true;
        foreach (var raw in q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var term = raw;
            var colon = raw.IndexOf(':');
            if (colon > 0)
            {
                var key = raw[..colon].ToLowerInvariant(); var val = raw[(colon + 1)..];
                var matched = key switch
                {
                    "category" => m.Category.Contains(val, StringComparison.OrdinalIgnoreCase),
                    "tag" => SplitTags(m.Tags).Any(x => x.Contains(val, StringComparison.OrdinalIgnoreCase)),
                    "method" or "printmethod" => m.PrintMethod.Contains(val, StringComparison.OrdinalIgnoreCase),
                    "type" => m.SemanticType.Contains(val, StringComparison.OrdinalIgnoreCase) || m.Subtype.Contains(val, StringComparison.OrdinalIgnoreCase),
                    "special" => m.SpecialType.Contains(val, StringComparison.OrdinalIgnoreCase),
                    "material" or "materials" => m.Materials.Contains(val, StringComparison.OrdinalIgnoreCase),
                    "favorite" => bool.TryParse(val, out var fv) && m.Favorite == fv,
                    "duplicate" => bool.TryParse(val, out var dv) && (!string.IsNullOrWhiteSpace(m.DuplicateGroup)) == dv,
                    "missingthumb" => bool.TryParse(val, out var mt) && !m.HasThumbnail == mt,
                    "review" => bool.TryParse(val, out var rv) && (!m.PrintReady || m.IntelligenceScore < .5) == rv,
                    _ => true
                };
                if (!matched) return false;
                if (key is "category" or "tag" or "method" or "printmethod" or "type" or "special" or "material" or "materials" or "favorite" or "duplicate" or "missingthumb" or "review") continue;
            }
            term = raw;
            if (!m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.TranslatedTitle.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Category.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Path.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Tags.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Family.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Slicer.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Materials.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.SemanticType.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.Subtype.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.PrintMethod.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.SpecialType.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !m.IntelligenceReason.Contains(term, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private void RefreshFilter()
    {
        // WPF can raise Search/ComboBox SelectionChanged while InitializeComponent()
        // is still constructing the visual tree. ModelView is not initialized until
        // immediately after InitializeComponent, so ignore those startup events.
        if (!uiReady) return;
        ModelView.Refresh();
        ApplySort(false);
        galleryPage = 0;
        UpdateGalleryPage();
        UpdateBrowseCounts();
    }

    private static IEnumerable<string> SplitTags(string? value)
        => (value ?? "").Split(new[] { ',', ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void UpdateBrowseFilters()
    {
        if (CategoryFilter == null || TagFilter == null) return;
        var categoryItems = new List<string> { "All Categories" };
        categoryItems.AddRange(categories.Concat(ModelsSource.Select(x => x.Category))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        CategoryFilter.ItemsSource = categoryItems;
        CategoryFilter.SelectedItem = string.IsNullOrWhiteSpace(categoryFilter) ? "All Categories" : categoryFilter;

        var tagItems = new List<string> { "All Tags" };
        tagItems.AddRange(ModelsSource.SelectMany(x => SplitTags(x.Tags))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        TagFilter.ItemsSource = tagItems;
        TagFilter.SelectedItem = string.IsNullOrWhiteSpace(tagFilter) ? "All Tags" : tagFilter;
    }

    private void UpdateBrowseCounts()
    {
        if (!uiReady) return;
        var visible = ModelView.Cast<ModelRecord>().Count();
        Status.Text = categoryFilter != null || tagFilter != null
            ? $"{visible:N0} matching models • filters active"
            : Status.Text;
    }

    private void ApplySort(bool toggle)
    {
        if (!uiReady) return;
        if (string.IsNullOrWhiteSpace(sortProperty)) return;
        if (toggle) sortDirection = sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ModelView.SortDescriptions.Clear();
        ModelView.SortDescriptions.Add(new SortDescription(sortProperty, sortDirection));
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Role == GridViewColumnHeaderRole.Padding) return;
        var property = header.Content?.ToString() switch
        {
            "Name" => nameof(ModelRecord.Name),
            "Category" => nameof(ModelRecord.Category),
            "Print Method" => nameof(ModelRecord.PrintMethod),
            "Type" => nameof(ModelRecord.SemanticType),
            "Tags" => nameof(ModelRecord.Tags),
            "Score" => nameof(ModelRecord.IntelligenceScore),
            "Size" => nameof(ModelRecord.Size),
            "Modified" => nameof(ModelRecord.ModifiedUtc),
            "Path" => nameof(ModelRecord.Path),
            _ => null
        };
        if (property == null) return;
        if (string.Equals(sortProperty, property, StringComparison.Ordinal)) ApplySort(true);
        else { sortProperty = property; sortDirection = ListSortDirection.Ascending; ApplySort(false); }
        Status.Text = $"Sorted by {header.Content} {(sortDirection == ListSortDirection.Ascending ? "↑" : "↓")}";
    }

    private void BrowseFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady) return;
        categoryFilter = CategoryFilter.SelectedItem as string;
        if (string.Equals(categoryFilter, "All Categories", StringComparison.OrdinalIgnoreCase)) categoryFilter = null;
        tagFilter = TagFilter.SelectedItem as string;
        if (string.Equals(tagFilter, "All Tags", StringComparison.OrdinalIgnoreCase)) tagFilter = null;
        ModelView.Refresh();
        galleryPage = 0;
        UpdateGalleryPage();
        Status.Text = $"{ModelView.Cast<ModelRecord>().Count():N0} models match the current filters";
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        categoryFilter = null; tagFilter = null; favoritesOnly = false;
        Search.Clear();
        if (CategoryFilter != null) CategoryFilter.SelectedItem = "All Categories";
        if (TagFilter != null) TagFilter.SelectedItem = "All Tags";
        if (PrintMethodFilter != null) PrintMethodFilter.SelectedIndex = 0;
        if (SpecialFilter != null) SpecialFilter.SelectedIndex = 0;
        ModelView.Refresh();
        galleryPage = 0; UpdateGalleryPage();
        Status.Text = "Filters cleared";
    }

    private void CategoryStat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        List_Click(sender, e);
        categoryFilter = name;
        UpdateBrowseFilters();
        if (CategoryFilter != null) CategoryFilter.SelectedItem = name;
        ModelView.Refresh();
        galleryPage = 0; UpdateGalleryPage();
        Status.Text = $"Showing {ModelView.Cast<ModelRecord>().Count():N0} models in {name}";
    }

    private void UpdateDashboard()
    {
        var all = ModelsSource.ToList();
        StatModels.Text = all.Count.ToString("N0");
        StatFavorites.Text = all.Count(x => x.Favorite).ToString("N0");
        StatDuplicates.Text = all.Where(x => !string.IsNullOrEmpty(x.Hash)).GroupBy(x => x.Hash).Count(g => g.Count() > 1).ToString("N0");
        StatMissing.Text = all.Count(x => !x.HasThumbnail).ToString("N0");

        CategoryStats.Clear();
        var max = Math.Max(1, all.Count);
        // Show every known category, including custom categories with zero current files.
        // This keeps the dashboard aligned with the Categories manager instead of only
        // showing categories that happen to have records in the current result set.
        var counts = all.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in categories.Concat(counts.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => counts.TryGetValue(x, out var count) ? count == 0 : true)
                     .ThenByDescending(x => counts.TryGetValue(x, out var count) ? count : 0)
                     .ThenBy(x => x))
        {
            var count = counts.TryGetValue(name, out var value) ? value : 0;
            CategoryStats.Add(new CategoryStat { Name = name, Count = count, Percent = count * 100d / max });
        }

        RecentModels.Clear();
        foreach (var m in all.OrderByDescending(x => x.ModifiedUtc).Take(8)) RecentModels.Add(m);
        UpdateBrowseFilters();
    }

    private void UpdateGalleryPage()
    {
        if (GalleryHost.Visibility != Visibility.Visible) return;
        var items = ModelView.Cast<ModelRecord>().ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)GalleryPageSize));
        galleryPage = Math.Clamp(galleryPage, 0, totalPages - 1);
        GalleryItems.Clear();
        foreach (var m in items.Skip(galleryPage * GalleryPageSize).Take(GalleryPageSize)) GalleryItems.Add(m);
        PageText.Text = $"Page {galleryPage + 1} of {totalPages} • {items.Count:N0} results";
    }

    private ModelRecord? Current() => active ?? Models.SelectedItem as ModelRecord;

    private void Select(ModelRecord? m)
    {
        if (m == null) return;
        active = m;
        if (Models.SelectedItems.Count <= 1)
            Models.SelectedItem = m;
        InspectorName.Text = m.Name;
        var entity = entities.Recognize(m);
        var translationLine = string.IsNullOrWhiteSpace(m.TranslatedTitle) ? "" : $"{m.OriginalLanguage}: {m.TranslatedTitle} ({m.TranslationConfidence}% translation confidence)";
        InspectorTranslated.Text = entity is null
            ? translationLine
            : $"{translationLine}{(string.IsNullOrWhiteSpace(translationLine) ? "" : "\n") }Entity: {entity.EntityName} • {entity.Domain} • {entity.Subtype} ({entity.Confidence}% entity confidence)";
        InspectorMeta.Text = $"{m.Category}\n{m.SizeText}\n{m.ModifiedText}\n{m.Path}";
        InspectorIntel.Text = $"Intelligence: {m.IntelligenceScore:P0}\nCategory: {m.Category} {(m.CategoryOverride ? "[Manual]" : "[Automatic]")}\nType: {m.SemanticType}\nSubtype: {m.Subtype}\nSpecial: {(string.IsNullOrWhiteSpace(m.SpecialType) ? "None" : m.SpecialType)}\nPrint method: {m.PrintMethod} ({m.PrintMethodConfidence:P0})\nPrint evidence: {m.PrintMethodEvidence}\nFamily: {m.Family}\nEvidence: {m.IntelligenceReason}\nRisk: {(string.IsNullOrWhiteSpace(m.RiskFlags)?"None":m.RiskFlags)}\nSlicer: {m.Slicer}\nObjects: {m.ObjectCount}\nDimensions: {m.Dimensions}\nMaterials: {m.Materials}\nSuggested tags: {m.SuggestedTags}\nPrint ready: {(m.PrintReady ? "Yes" : "Review")}";
        InspectorTags.Text = string.IsNullOrWhiteSpace(m.Tags) ? "No tags" : m.Tags;
        InspectorImage.Source = null;
        InspectorPlaceholder.Visibility = Visibility.Visible;
        try
        {
            if (m.HasThumbnail)
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(m.ThumbnailPath!); image.EndInit(); image.Freeze();
                InspectorImage.Source = image;
                InspectorPlaceholder.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
    }

    private void Models_RightDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(Models, (DependencyObject)e.OriginalSource) as ListViewItem;
        if (item?.DataContext is ModelRecord m)
        {
            // Preserve an existing multi-selection when right-clicking one of its items.
            if (!Models.SelectedItems.Contains(m))
            {
                Models.SelectedItems.Clear();
                Models.SelectedItems.Add(m);
            }
            active = m;
            Select(m);
            e.Handled = true;
            ListMenu.PlacementTarget = item;
            ListMenu.IsOpen = true;
        }
    }

    private void Models_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Models.SelectedItem is ModelRecord m)
            Select(m);
        else if (Models.SelectedItems.Count == 0)
            active = null;
    }

    private void Models_DoubleClick(object sender, MouseButtonEventArgs e) => Open(Models.SelectedItem as ModelRecord ?? Current());

    private void GallerySelect_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement f && f.DataContext is ModelRecord m)
        {
            Select(m);
            if (e.ClickCount >= 2) Open(m);
        }
    }

    private static ContextMenu? FindContext(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is MenuItem mi && mi.Parent is ContextMenu cm) return cm;
            if (d is ContextMenu c) return c;
            d = d is FrameworkElement fe ? fe.Parent : null;
        }
        return null;
    }

    private void GalleryAction(object sender, Action<ModelRecord?> action)
    {
        var cm = FindContext(sender as DependencyObject);
        if (cm?.PlacementTarget is FrameworkElement fe && fe.DataContext is ModelRecord m) { Select(m); action(m); }
    }

    private void Open(ModelRecord? m)
    {
        if (m == null || !File.Exists(m.Path)) return;
        try { Process.Start(new ProcessStartInfo(m.Path) { UseShellExecute = true }); } catch (Exception ex) { Error("Open failed", ex); }
    }

    private void Location(ModelRecord? m)
    {
        if (m == null || !File.Exists(m.Path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{m.Path}\"") { UseShellExecute = true }); } catch (Exception ex) { Error("Location failed", ex); }
    }

    private void CopyPath(ModelRecord? m) { if (m != null) Clipboard.SetText(m.Path); }

    private static bool IsCustomCategory(string? category)
        => !string.IsNullOrWhiteSpace(category) &&
           !BuiltInCategories.All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeTags(string category, string existingTags, string? previousCategory = null)
    {
        var values = existingTags
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, previousCategory, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x, category, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Length <= 80)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        values.Insert(0, category);
        return string.Join(", ", values);
    }

    private void EditTags(ModelRecord? m)
    {
        if (m == null) return;
        var existing = m.Tags
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, m.Category, StringComparison.OrdinalIgnoreCase));
        var w = new TagsWindow(m.Category, existing) { Owner = this };
        if (w.ShowDialog() != true) return;
        m.Tags = NormalizeTags(m.Category, w.Value, m.Category);
        engine.Repository.Upsert(m);
        Select(m);
        RefreshFilter();
        Status.Text = "Tags updated";
    }

    private void ChangeCategory(ModelRecord? m)
    {
        if (m == null || organization == null) return;
        var selected = Models.Visibility == Visibility.Visible
            ? Models.SelectedItems.OfType<ModelRecord>().Distinct().ToList()
            : new List<ModelRecord> { m };
        if (!selected.Contains(m)) selected.Insert(0, m);

        var w = new CategoryWindow(m.Category, categories) { Owner = this };
        if (w.ShowDialog() != true) return;
        if (!categories.Contains(w.Value, StringComparer.OrdinalIgnoreCase)) { categories.Add(w.Value); SaveCategories(); }
        try
        {
            foreach (var item in selected.ToList())
            {
                item.CategoryOverride = true;
                var result = organization.MoveToCategory(item, w.Value);
                learning.RecordCorrection(item, w.Value);
                ModelsSource.Remove(item);
                ModelsSource.Add(result.Model);
            }
            Select(m);
            UpdateDashboard(); RefreshFilter();
            Status.Text = selected.Count == 1 ? "Category updated" : $"Moved {selected.Count:N0} models to {w.Value}";
        }
        catch (Exception ex) { Error("Category move failed", ex); }
    }

    private async void Analyze(ModelRecord? m)
    {
        if (m == null || !File.Exists(m.Path)) return;
        try
        {
            Status.Text = "Analyzing model…";
            var a = await engine.AnalyzeAsync(m);
            // A custom category is explicit user intent. Manual/deep analysis may update
            // intelligence fields, but it must never replace that category.
            var preserveCustomCategory = m.CategoryOverride || IsCustomCategory(m.Category);
            if (!preserveCustomCategory) m.Category = a.Category;
            m.IntelligenceScore = a.Confidence; m.Family = a.Family;
            if (!m.PrintMethodOverride) { m.PrintMethod = a.PrintMethod; m.PrintMethodConfidence = a.PrintMethodConfidence; m.PrintMethodEvidence = a.PrintMethodEvidence; }
            m.SpecialType = a.SpecialType; m.ObjectCount = a.ObjectCount;
            m.Dimensions = a.Dimensions; m.Slicer = a.Slicer; m.Materials = a.Materials; m.PrintReady = a.PrintReady; m.SemanticType=a.SemanticType; m.Subtype=a.Subtype; m.SuggestedTags=a.SuggestedTags; m.IntelligenceReason=a.Reason; m.RiskFlags=a.RiskFlags; m.Tags = NormalizeTags(m.Category, m.Tags);
            multilingual.Apply(m);
            var pipelineApplied = intelligencePipeline.Apply(m);
            engine.Repository.Upsert(m); UpdateDashboard(); RefreshFilter(); Select(m);
            Status.Text = pipelineApplied ? "Analysis and modular intelligence classification complete" : "Analysis and multilingual metadata complete";
        }
        catch (Exception ex) { Error("Analysis failed", ex); }
    }

    private void TranslateCurrent_Click(object sender, RoutedEventArgs e)
    {
        var m = Current();
        if (m == null) return;
        multilingual.Apply(m);
        engine.Repository.Upsert(m);
        Select(m);
        RefreshFilter();
        Status.Text = m.OriginalLanguage == "Unknown" ? "No multilingual text detected" : $"Translated metadata: {m.TranslatedTitle}";
    }

    private async void BuildMultilingualMetadata_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var all = engine.Repository.GetAll();
            Status.Text = $"Building multilingual metadata for {all.Count:N0} models…";
            var result = await Task.Run(() =>
            {
                var detected = 0;
                foreach (var m in all)
                {
                    var before = m.OriginalLanguage;
                    multilingual.Apply(m);
                    if (!string.Equals(m.OriginalLanguage, "Unknown", StringComparison.OrdinalIgnoreCase)) detected++;
                    if (!string.Equals(before, m.OriginalLanguage, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(m.TranslatedTitle))
                        engine.Repository.Upsert(m);
                }
                return (Total: all.Count, Detected: detected);
            });
            UpdateDashboard(); RefreshFilter();
            if (active != null) Select(active);
            Status.Text = $"Multilingual metadata complete • {result.Detected:N0} multilingual filenames detected";
        }
        catch (Exception ex) { Error("Multilingual metadata failed", ex); }
    }

    private async void RefreshThumbnail(ModelRecord? m)
    {
        if (m == null || !File.Exists(m.Path)) return;
        m.ThumbnailPath = await ThumbnailService.ExtractAsync(m.Path);
        engine.Repository.Upsert(m); Select(m); UpdateDashboard(); UpdateGalleryPage();
    }

    private void Rename(ModelRecord? m)
    {
        if (m == null) return;
        var proposed = Path.GetFileNameWithoutExtension(m.Name);
        var w = new TextInputWindow("Rename 3MF", "New file name", proposed) { Owner = this };
        if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(w.Value)) return;

        var raw = w.Value.Trim();
        if (raw.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || raw.Contains(Path.DirectorySeparatorChar) || raw.Contains(Path.AltDirectorySeparatorChar))
        {
            Error("Rename failed", new ArgumentException("The new name contains characters that Windows does not allow in file names."));
            return;
        }
        var name = raw.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".3mf";
        var dest = Path.Combine(Path.GetDirectoryName(m.Path)!, name);
        if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(m.Path), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(dest))
        {
            Error("Rename failed", new IOException("A file with that name already exists in the folder."));
            return;
        }
        try
        {
            var old = m.Path;
            File.Move(old, dest);
            m.Path = dest; m.Name = Path.GetFileName(dest); m.ModifiedUtc = File.GetLastWriteTimeUtc(dest);
            engine.Repository.RenamePath(old, dest, m); engine.Repository.Upsert(m); RefreshFilter(); Select(m);
        }
        catch (Exception ex) { Error("Rename failed", ex); }
    }

    private IReadOnlyList<ModelRecord> SelectedModels(ModelRecord? fallback)
    {
        if (Models.Visibility == Visibility.Visible && Models.SelectedItems.Count > 0)
            return Models.SelectedItems.OfType<ModelRecord>().ToList();
        return fallback == null ? Array.Empty<ModelRecord>() : new[] { fallback };
    }

    private void Delete(ModelRecord? m)
    {
        var selected = SelectedModels(m).Where(x => File.Exists(x.Path)).ToList();
        if (selected.Count == 0) return;
        var prompt = selected.Count == 1
            ? $"Move '{selected[0].Name}' to the Recycle Bin?"
            : $"Move {selected.Count:N0} selected models to the Recycle Bin?";
        if (MessageBox.Show(this, prompt, "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            foreach (var item in selected)
            {
                RecycleBin.Delete(item.Path);
                engine.Repository.Delete(item.Path);
                ModelsSource.Remove(item);
            }
            active = null;
            Models.SelectedItems.Clear();
            UpdateDashboard(); RefreshFilter();
            Status.Text = selected.Count == 1 ? "Model deleted" : $"Deleted {selected.Count:N0} models";
        }
        catch (Exception ex) { Error("Delete failed", ex); }
    }

    private void Favorite(ModelRecord? m)
    {
        var selected = SelectedModels(m);
        if (selected.Count == 0) return;
        var target = selected.Count == 1 ? !selected[0].Favorite : selected.Any(x => !x.Favorite);
        foreach (var item in selected) { item.Favorite = target; engine.Repository.Upsert(item); }
        UpdateDashboard(); RefreshFilter();
        if (selected.Count == 1) Select(selected[0]);
    }

    private void Similar(ModelRecord? m)
    {
        if (m == null) return;
        var terms = Path.GetFileNameWithoutExtension(m.Name).Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray();
        var matches = ModelsSource.Where(x => x != m && terms.Any(t => x.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).Take(20).ToList();
        MessageBox.Show(this, matches.Count == 0 ? "No similar models found." : string.Join(Environment.NewLine, matches.Select(x => x.Name)), "Similar Names");
    }

    private void MoveCategory(ModelRecord? m) => ChangeCategory(m);

    private void UndoMove()
    {
        if (organization == null) return;
        try
        {
            var r = organization.UndoLast();
            if (r == null) return;
            var old = ModelsSource.FirstOrDefault(x => x.Path == r.OldPath || x.Path == r.NewPath);
            if (old != null) ModelsSource.Remove(old);
            ModelsSource.Add(r.Model); Select(r.Model); UpdateDashboard(); RefreshFilter();
        }
        catch (Exception ex) { Error("Undo failed", ex); }
    }

    private void ShowProperties(ModelRecord? m)
    {
        if (m == null) return;
        MessageBox.Show(this, $"{m.Name}\n\nCategory: {m.Category} (override: {(m.CategoryOverride ? "Yes" : "No")})\nPrint Method: {m.PrintMethod} ({m.PrintMethodConfidence:P0})\nPrint Method Evidence: {m.PrintMethodEvidence}\nSpecial Type: {(string.IsNullOrWhiteSpace(m.SpecialType) ? "None" : m.SpecialType)}\nConfidence: {m.IntelligenceScore:P0}\nType: {m.SemanticType}\nSubtype: {m.Subtype}\nSpecial: {(string.IsNullOrWhiteSpace(m.SpecialType) ? "None" : m.SpecialType)}\nPrint method: {m.PrintMethod} ({m.PrintMethodConfidence:P0})\nPrint evidence: {m.PrintMethodEvidence}\nFamily: {m.Family}\nSuggested tags: {m.SuggestedTags}\nEvidence: {m.IntelligenceReason}\nRisk flags: {(string.IsNullOrWhiteSpace(m.RiskFlags)?"None":m.RiskFlags)}\nObjects: {m.ObjectCount}\nDimensions: {m.Dimensions}\nSlicer: {m.Slicer}\nMaterials: {m.Materials}\nPrint ready: {(m.PrintReady ? "Yes" : "Review")}\n\n{m.Path}", "Model Intelligence");
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();
    private void IntelligenceFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshFilter();
    private ScanMode GetSelectedScanMode()
    {
        return ScanModeSelector.SelectedIndex switch
        {
            0 => ScanMode.Quick,
            2 => ScanMode.Deep,
            _ => ScanMode.Turbo
        };
    }

    private async void RebuildLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Status.Text = "Choose a library folder in Settings.";
            return;
        }
        if (MessageBox.Show(this,
            "Rebuild Library will re-index every .3MF file, re-run the newest intelligence rules, rebuild hashes/duplicate groups, and refresh missing thumbnails. Custom categories, favorites, and user tags will be preserved. Continue?",
            "Rebuild Library", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (!scanGate.Wait(0))
        {
            scanCts?.Cancel();
            Status.Text = "Stopping the current scan before rebuild…";
            return;
        }

        try
        {
            scanCts?.Cancel();
            scanCts?.Dispose();
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;
            Progress.Value = 0;
            ScanModeStatus.Text = "Rebuild";
            Status.Text = "Rebuilding library: re-analyzing every 3MF…";
            ScanProgressText.Text = "Discovering .3mf files…";
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress.Value = p.Percent;
                ScanProgressText.Text = p.Discovered <= 0
                    ? "No .3mf files discovered"
                    : $"Found {p.Discovered:N0} • Indexed {p.Indexed:N0} • Failed {p.Failed:N0} • {p.Percent}%";
            });
            var rebuilt = await engine.RebuildAsync(new[] { root }, token, progress, preserveMetadata: false);
            token.ThrowIfCancellationRequested();

            var rows = engine.Repository.GetAll().Where(x => File.Exists(x.Path)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Models.UnselectAll();
            active = null;
            ModelsSource.ReplaceAll(rows);

            // Reconcile category definitions with the rebuilt records so custom categories
            // created or previously persisted in the database are available immediately.
            var changedCategories = false;
            foreach (var c in rows.Select(x => x.Category).Where(IsCustomCategory).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!categories.Contains(c, StringComparer.OrdinalIgnoreCase))
                {
                    categories.Add(c);
                    changedCategories = true;
                }
            }
            if (changedCategories) SaveCategories();

            UpdateDashboard();
            UpdateGalleryPage();
            ScanProgressText.Text = $"Found {rebuilt.Discovered:N0} • Indexed {rebuilt.Models:N0} • Failed {rebuilt.Failed:N0} • 100%";
            Status.Text = $"Rebuild complete • {rebuilt.Models:N0}/{rebuilt.Discovered:N0} models • {rebuilt.Failed:N0} failed • {rebuilt.Reclassified:N0} reclassified • {rebuilt.PreservedCustomCategories:N0} custom-category assignments preserved";
        }
        catch (OperationCanceledException) { Status.Text = "Rebuild canceled"; }
        catch (Exception ex) { Error("Library rebuild failed", ex); }
        finally { scanGate.Release(); }
    }

    private async void ResetAndRebuildLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Status.Text = "Choose a library folder in Settings.";
            return;
        }

        var answer = MessageBox.Show(this,
            "RESET & REBUILD LIBRARY\n\nThis will:\n• Back up the current PrintVault database first.\n• Clear ALL stored library records and derived intelligence from the database.\n• Re-scan every .3MF from the physical library.\n• Rebuild classifications, semantic metadata, hashes, duplicates, and thumbnails from the files themselves.\n\nIt will NOT delete, move, rename, or modify any .3MF files.\n\nFavorites, tags, manual category overrides, and other database-only library metadata will be cleared because this is a true clean rebuild. The backup can restore the previous database if needed.\n\nContinue?",
            "Reset & Rebuild Library", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (!scanGate.Wait(0))
        {
            scanCts?.Cancel();
            Status.Text = "Stopping the current scan before reset…";
            return;
        }

        try
        {
            scanCts?.Cancel();
            scanCts?.Dispose();
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;
            Progress.Value = 0;
            ScanModeStatus.Text = "RESET & REBUILD";
            Status.Text = "Backing up library database…";
            ScanProgressText.Text = "Creating safety backup…";

            var backup = engine.Repository.BackupDatabase();
            Status.Text = $"Backup created: {Path.GetFileName(backup)}";

            // RebuildAsync now builds a complete candidate and commits it only after
            // every file succeeds. Do not clear the live catalog first: doing so created
            // a catastrophic empty-catalog window if rebuild failed or was canceled.
            Models.UnselectAll();
            active = null;
            ModelsSource.ReplaceAll(Array.Empty<ModelRecord>());
            GalleryItems.Clear();
            RecentModels.Clear();
            CategoryStats.Clear();

            var progress = new Progress<ScanProgress>(p =>
            {
                Progress.Value = p.Percent;
                ScanProgressText.Text = p.Discovered <= 0
                    ? "No .3mf files discovered"
                    : $"Found {p.Discovered:N0} • Indexed {p.Indexed:N0} • Failed {p.Failed:N0} • {p.Percent}%";
            });

            var rebuilt = await engine.RebuildAsync(new[] { root }, token, progress);
            token.ThrowIfCancellationRequested();

            var rows = engine.Repository.GetAll().Where(x => File.Exists(x.Path)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            ModelsSource.ReplaceAll(rows);
            UpdateDashboard();
            UpdateGalleryPage();
            ScanProgressText.Text = $"Found {rebuilt.Discovered:N0} • Indexed {rebuilt.Models:N0} • Failed {rebuilt.Failed:N0} • 100%";
            Status.Text = $"RESET COMPLETE • {rebuilt.Models:N0}/{rebuilt.Discovered:N0} models rebuilt • {rebuilt.Failed:N0} failed • Backup: {Path.GetFileName(backup)}";
        }
        catch (OperationCanceledException) { Status.Text = "Reset & rebuild canceled"; }
        catch (Exception ex) { Error("Library reset & rebuild failed", ex); }
        finally { scanGate.Release(); }
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => StartScan();
    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        favoritesOnly = false;
        Dashboard.Visibility = Visibility.Visible;
        Models.Visibility = Visibility.Collapsed;
        GalleryHost.Visibility = Visibility.Collapsed;
        Status.Text = $"{ModelsSource.Count:N0} models • Dashboard";
        UpdateDashboard();
    }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        // Always return to the complete library, regardless of Favorites/search state.
        favoritesOnly = false;
        Search.Clear();
        Dashboard.Visibility = Visibility.Collapsed;
        Models.Visibility = Visibility.Visible;
        GalleryHost.Visibility = Visibility.Collapsed;
        galleryPage = 0;
        RefreshFilter();
        Status.Text = $"{ModelView.Cast<ModelRecord>().Count():N0} models • All Models";
    }

    private void Gallery_Click(object sender, RoutedEventArgs e)
    {
        favoritesOnly = false;
        Dashboard.Visibility = Visibility.Collapsed;
        Models.Visibility = Visibility.Collapsed;
        GalleryHost.Visibility = Visibility.Visible;
        galleryPage = 0;
        RefreshFilter();
        Status.Text = $"{ModelView.Cast<ModelRecord>().Count():N0} models • Gallery";
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        favoritesOnly = true;
        Dashboard.Visibility = Visibility.Collapsed;
        Models.Visibility = Visibility.Visible;
        GalleryHost.Visibility = Visibility.Collapsed;
        RefreshFilter();
        Status.Text = $"{ModelView.Cast<ModelRecord>().Count():N0} favorites • Favorites";
    }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { galleryPage--; UpdateGalleryPage(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { galleryPage++; UpdateGalleryPage(); }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var current = root ?? "No library selected";
        var dialog = new LibrarySettingsWindow(current) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.ClearRequested)
        {
            await ClearLibraryAsync();
            return;
        }
        if (!string.IsNullOrWhiteSpace(dialog.SelectedLibrary))
        {
            if (IsForbiddenProductionLibrary(dialog.SelectedLibrary))
            {
                MessageBox.Show(this, $"The production library is protected in this {AppVersion.Version} Engineering build.", "Production Library Protected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SetLibraryRoot(dialog.SelectedLibrary);
        }
    }
    private void SmartCategoryReview_Click(object sender, RoutedEventArgs e) => SmartCategoryReview();
    private void EntitySemanticRegressionChallenge_Click(object sender, RoutedEventArgs e) => new EntitySemanticRegressionChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void ModelIntelligenceHueForgeChallenge_Click(object sender, RoutedEventArgs e) => new ModelIntelligenceHueForgeChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void RelationshipPrecisionChallenge_Click(object sender, RoutedEventArgs e) => new RelationshipPrecisionChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void SemanticRelationshipIntegrationChallenge_Click(object sender, RoutedEventArgs e) => new SemanticRelationshipIntegrationChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void FullLibraryIntelligenceChallenge_Click(object sender, RoutedEventArgs e) => new FullLibraryIntelligenceChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void SemanticCoverageChallenge_Click(object sender, RoutedEventArgs e) => new SemanticCoverageChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private void SemanticEvidenceFusionChallenge_Click(object sender, RoutedEventArgs e) => new SemanticEvidenceFusionChallengeWindow(engine.Repository) { Owner = this }.ShowDialog();
    private async void WholeLibraryRootCause_Click(object sender, RoutedEventArgs e)
    {
        var gateHeld = false;
        try
        {
            // Root-cause analysis must run against the same catalog the dashboard is showing.
            // Wait for any scan/rebuild to finish, capture the UI catalog on the UI thread,
            // then hand that immutable snapshot to the forensic worker.
            await scanGate.WaitAsync();
            gateHeld = true;
            Status.Text = "Running whole-library root-cause analysis…";
            var activeCatalogSnapshot = ModelsSource.ToList();
            var service = new WholeLibraryRootCauseAnalysisService(engine.Repository);
            var result = await Task.Run(() => service.Run(activeCatalogSnapshot: activeCatalogSnapshot));
            var causeText = string.Join("\n", result.Causes.Select(x => $"{x.Key}: {x.Value:N0}"));
            MessageBox.Show(this, $"WHOLE-LIBRARY ROOT-CAUSE PASS\n\nCatalog: {result.Catalog:N0}\nProcessed: {result.Processed:N0}\nStored conflicts: {result.Conflicts:N0}\nConflict/review rows: {result.AnalyzedConflicts:N0}\nFailures: {result.Failed:N0}\n\nROOT CAUSES\n{causeText}\n\nReport: {result.ReportPath}\nCSV: {result.CsvPath}\nJSON: {result.JsonPath}", "Whole-Library Root Cause", MessageBoxButton.OK, MessageBoxImage.Information);
            Status.Text = $"Root-cause pass complete • {result.Processed:N0} records • {result.Conflicts:N0} stored conflicts • {result.Failed:N0} failures";
        }
        catch (Exception ex) { Error("Whole-library root-cause analysis failed", ex); }
        finally
        {
            if (gateHeld) scanGate.Release();
        }
    }

    private void SmartCategoryReview()
    {
        var service = new SmartCategoryReconciliationService(engine.Repository);
        var plan = service.Preview();
        if (plan.Candidates == 0)
        {
            MessageBox.Show(this, $"PrintVault reviewed {plan.Scanned:N0} catalog records and found no high-confidence category corrections in Terrain/Props or related models. Manual category overrides were preserved.", "Smart Category Review", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new SmartCategoryReviewWindow(plan, this);
        if (window.ShowDialog() != true) return;
        var selected = window.SelectedSuggestions();
        if (selected.Count == 0) return;

        var result = service.Apply(
            selected,
            target => { if (!categories.Contains(target, StringComparer.OrdinalIgnoreCase)) categories.Add(target); },
            (name, category) => learning.RecordCorrection(new ModelRecord { Name = name }, category));
        SaveCategories();
        ModelsSource.ReplaceAll(engine.Repository.GetAll().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase));
        UpdateDashboard();
        RefreshFilter();
        UpdateGalleryPage();
        Status.Text = $"Smart category review applied to {result.Applied:N0} models";

        var message = $"Applied: {result.Applied:N0}\nProtected/manual skipped: {result.ProtectedSkipped:N0}";
        if (result.Warnings.Count > 0) message += $"\nWarnings: {result.Warnings.Count}";
        MessageBox.Show(this, message, "Smart Category Review Complete", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void UndoSmartCategoryReview_Click(object sender, RoutedEventArgs e)
    {
        var service = new SmartCategoryReconciliationService(engine.Repository);
        if (!service.CanUndo)
        {
            MessageBox.Show(this, "There is no completed smart category review available to undo.", "Undo Smart Category Review", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, "Undo the most recent smart category review? Files will remain in their current folders; only PrintVault category metadata and tags will be restored.", "Undo Smart Category Review", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = service.UndoLast();
        ModelsSource.ReplaceAll(engine.Repository.GetAll().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase));
        UpdateDashboard(); RefreshFilter(); UpdateGalleryPage();
        Status.Text = $"Restored {result.Applied:N0} smart category changes";
        var message = $"Restored previous categories for: {result.Applied:N0} models";
        if (result.Warnings.Count > 0) message += $"\nWarnings: {result.Warnings.Count}";
        MessageBox.Show(this, message, "Undo Smart Category Review", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ReconcileCategories_Click(object sender, RoutedEventArgs e) => ReconcileCategories(showIfClean: true);
    private void Categories_Click(object sender, RoutedEventArgs e)
    {
        var w = new CategoryManagerWindow(categories) { Owner = this };
        if (w.ShowDialog() != true) return;

        SaveCategories();
        RefreshFilter();
        UpdateDashboard();

        if (!string.IsNullOrWhiteSpace(w.SelectedCategory))
        {
            List_Click(sender, e);
            categoryFilter = w.SelectedCategory;
            UpdateBrowseFilters();
            if (CategoryFilter != null) CategoryFilter.SelectedItem = categoryFilter;
            ModelView.Refresh();
            galleryPage = 0;
            UpdateGalleryPage();
            Status.Text = $"Showing {ModelView.Cast<ModelRecord>().Count():N0} models in {categoryFilter}";
        }
        else
        {
            Status.Text = "Categories updated";
        }
    }
    private void Duplicates_Click(object sender, RoutedEventArgs e)
    {
        var d = new DuplicateManagerWindow(engine.Repository, new LibraryHealthService(engine.Repository, stateStore), () =>
        {
            ModelsSource.ReplaceAll(engine.Repository.GetAll().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase));
            UpdateDashboard(); RefreshFilter(); UpdateGalleryPage();
        }) { Owner = this };
        d.ShowDialog();
    }
    private void EnsureCanonicalCategoryTargets()
    {
        var targets = categories
            .Select(CategoryReconciliationService.PreferredTarget)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var target in targets)
            if (!categories.Contains(target, StringComparer.OrdinalIgnoreCase)) categories.Add(target);
    }

    private void ReconcileCategories(bool showIfClean = true)
    {
        EnsureCanonicalCategoryTargets();
        var service = new CategoryReconciliationService(engine.Repository);
        var plan = service.Preview(categories);
        if (plan.Items.Count == 0)
        {
            SaveCategories();
            if (showIfClean)
                MessageBox.Show(this, "Your PrintVault categories are already reconciled. No legacy categories were found.", "Category Reconciliation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new CategoryReconciliationWindow(plan, this);
        if (window.ShowDialog() != true || !window.ApplyChanges) return;

        var selected = window.SelectedSourceNames();
        var result = service.Apply(plan, selected, value => categories.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)));
        SaveCategories();
        StartScan();

        var message = $"Reclassified: {result.FilesReclassified:N0} files\nRetired empty legacy categories: {result.CategoriesRetired:N0}\nProtected/manual files: {result.ProtectedFiles:N0}";
        if (result.Warnings.Count > 0) message += $"\n\nWarnings: {result.Warnings.Count}";
        MessageBox.Show(this, message, "Category Reconciliation Complete", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void UndoCategoryReconciliation_Click(object sender, RoutedEventArgs e)
    {
        var service = new CategoryReconciliationService(engine.Repository);
        if (!service.CanUndo)
        {
            MessageBox.Show(this, "There is no completed category reconciliation available to undo.", "Undo Category Reconciliation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, "Undo the most recent category reconciliation? Files will keep their current locations, but their previous categories and tags will be restored.", "Undo Category Reconciliation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = service.UndoLast();
        StartScan();
        var message = $"Restored previous categories for: {result.FilesReclassified:N0} files";
        if (result.Warnings.Count > 0) message += $"\n\nWarnings: {result.Warnings.Count}";
        MessageBox.Show(this, message, "Undo Complete", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { MessageBox.Show(this, "Choose a library folder first.", "Smart Cleanup", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var service = new LibraryConsolidationService(engine.Repository, root);
        var plan = service.Preview(categories);
        if (plan.Moves.Count == 0)
        {
            if (plan.IgnoredCount > 0)
                MessageBox.Show(this, $"No safe folder moves were found. {plan.IgnoredCount:N0} files/folders were left untouched. PrintVault will now check the catalog for legacy categories.", "Smart Cleanup", MessageBoxButton.OK, MessageBoxImage.Information);
            ReconcileCategories(showIfClean: true);
            return;
        }
        var cleanup = new ConsolidationWindow(plan, this);
        if (cleanup.ShowDialog() != true) return;
        var selected = cleanup.SelectedGroups();
        var selectedKeys = selected.Select(x => $"{x.SourceFolder}\0{x.TargetCategory}\0{x.Confidence}\0{x.Reason}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredMoves = plan.Moves.Where(m => selectedKeys.Contains($"{Path.GetFileName(Path.GetDirectoryName(m.SourcePath))}\0{m.TargetCategory}\0{m.Confidence}\0{m.Reason}")).ToList();
        var selectedPlan = new ConsolidationPlan(filteredMoves, selected, plan.ReviewCount, plan.IgnoredCount);
        var result = service.Apply(selectedPlan, removeEmptyFolders: true, includeReview: cleanup.IncludeReview);
        StartScan();
        var message = $"Moved: {result.FilesMoved:N0} 3MF files\nRemoved empty folders: {result.FoldersRemoved:N0}\nSpace moved: {result.BytesMoved / 1024d / 1024d:0.0} MB\n\nUndo is available from the Smart Cleanup menu.";
        if (result.Warnings.Count > 0) message += $"\n\nWarnings: {result.Warnings.Count}";
        stateStore.RecordJournal("Consolidation", message, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault", "last_consolidation.json"), result.FilesMoved > 0);
        MessageBox.Show(this, message, "Consolidation Complete", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        ReconcileCategories(showIfClean: true);
    }

    private void UndoConsolidation_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var service = new LibraryConsolidationService(engine.Repository, root);
        if (!service.CanUndo)
        {
            MessageBox.Show(this, "There is no completed consolidation available to undo.", "Undo Consolidation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(this, "Undo the most recent Smart Library Consolidation? Files will be moved back to their recorded original locations when those paths are available.", "Undo Consolidation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        var result = service.UndoLast();
        StartScan();
        var message = $"Restored: {result.FilesMoved:N0} 3MF files\nSpace restored: {result.BytesMoved / 1024d / 1024d:0.0} MB";
        if (result.Warnings.Count > 0) message += $"\n\nWarnings: {result.Warnings.Count}";
        MessageBox.Show(this, message, "Undo Complete", MessageBoxButton.OK, result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void LibraryHealth_Click(object sender, RoutedEventArgs e)
    {
        var w = new LibraryHealthWindow(new LibraryHealthService(engine.Repository, stateStore), stateStore,
            () => new DuplicateManagerWindow(engine.Repository, new LibraryHealthService(engine.Repository, stateStore), RefreshFromTools) { Owner = this }.ShowDialog(),
            () => new TagManagerWindow(ModelsSource.ToList(), engine.Repository, RefreshFromTools) { Owner = this }.ShowDialog(),
            () => new CollectionsWindow(stateStore, ModelsSource.ToList(), SelectedModels(active), RefreshFromTools) { Owner = this }.ShowDialog()) { Owner = this };
        w.ShowDialog();
    }

    private void DuplicateManager_Click(object sender, RoutedEventArgs e)
        => new DuplicateManagerWindow(engine.Repository, new LibraryHealthService(engine.Repository, stateStore), RefreshFromTools) { Owner = this }.ShowDialog();

    private void ModelIntelligence_Click(object sender, RoutedEventArgs e)
        => new ModelIntelligenceWindow(ModelsSource.ToList(), Current()) { Owner = this }.ShowDialog();

    private void TagManager_Click(object sender, RoutedEventArgs e)
        => new TagManagerWindow(ModelsSource.ToList(), engine.Repository, RefreshFromTools) { Owner = this }.ShowDialog();

    private void Collections_Click(object sender, RoutedEventArgs e)
        => new CollectionsWindow(stateStore, ModelsSource.ToList(), SelectedModels(active), RefreshFromTools) { Owner = this }.ShowDialog();

    private void RefreshFromTools()
    {
        ModelsSource.ReplaceAll(engine.Repository.GetAll());
        UpdateBrowseFilters(); UpdateDashboard(); RefreshFilter(); UpdateGalleryPage();
    }

    private void ApplyLearnedRules_Click(object sender, RoutedEventArgs e)
    {
        var changed = 0;
        foreach (var m in ModelsSource.ToList())
        {
            if (m.CategoryOverride) continue;
            var suggested = learning.Suggest(m, categories);
            if (suggested == null || string.Equals(suggested, m.Category, StringComparison.OrdinalIgnoreCase)) continue;
            m.Category = suggested; m.Tags = NormalizeTags(m.Category, m.Tags); engine.Repository.Upsert(m); changed++;
        }
        UpdateDashboard(); RefreshFilter(); Status.Text = changed == 0 ? "No high-confidence learned corrections to apply" : $"Applied {changed:N0} learned category corrections";
        stateStore.RecordJournal("Learned Rules", $"Applied {changed:N0} high-confidence learned category corrections", null, false);
    }

    private async void Stats_Click(object sender, RoutedEventArgs e)
    {
        var s = await engine.GetStatsAsync();
        MessageBox.Show(this, $"Models: {s.Models:N0}\nFavorites: {s.Favorites:N0}\nDuplicate groups: {s.Duplicates:N0}\nNeeds review: {s.NeedsReview:N0}\nMissing thumbnails: {s.MissingThumbnails:N0}\nStorage: {s.Bytes / 1024d / 1024d / 1024d:0.00} GB\nDatabase: {engine.Repository.DatabasePath}", "Statistics");
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F2) { Rename(Current()); e.Handled = true; }
        else if (e.Key == Key.Delete) { Delete(Current()); e.Handled = true; }
        else if (e.Key == Key.Enter && Current() != null) { Open(Current()); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            Search.Clear();
            if (favoritesOnly)
            {
                favoritesOnly = false;
                RefreshFilter();
                Status.Text = $"{ModelView.Cast<ModelRecord>().Count():N0} models • All Models";
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Search.Focus(); Search.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && Models.Visibility == Visibility.Visible) { Models.SelectAll(); e.Handled = true; }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var m = Current();
        if (m == null)
        {
            Status.Text = "Select a model first.";
            return;
        }
        Open(m);
    }
    private void Location_Click(object sender, RoutedEventArgs e) => Location(Current());
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyPath(Current());
    private void Category_Click(object sender, RoutedEventArgs e) => ChangeCategory(Current());
    private void Reapply_Click(object sender, RoutedEventArgs e) => Analyze(Current());
    private void Rename_Click(object sender, RoutedEventArgs e) => Rename(Current());
    private void Delete_Click(object sender, RoutedEventArgs e) => Delete(Current());
    private void DeepAnalyze_Click(object sender, RoutedEventArgs e) => Analyze(Current());
    private void RefreshThumbnail_Click(object sender, RoutedEventArgs e) => RefreshThumbnail(Current());
    private void Similar_Click(object sender, RoutedEventArgs e) => Similar(Current());
    private void Favorite_Click(object sender, RoutedEventArgs e) => Favorite(Current());
    private void MoveCategory_Click(object sender, RoutedEventArgs e) => MoveCategory(Current());
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoMove();
    private void EditTags_Click(object sender, RoutedEventArgs e) => EditTags(Current());
    private void Properties_Click(object sender, RoutedEventArgs e) => ShowProperties(Current());

    private void GalleryOpen_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Open);
    private void GalleryLocation_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Location);
    private void GalleryCopy_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, CopyPath);
    private void GalleryCategory_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, ChangeCategory);
    private void GalleryReapply_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Analyze);
    private void GalleryRename_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Rename);
    private void GalleryDelete_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Delete);
    private void GalleryDeepAnalyze_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Analyze);
    private void GalleryRefreshThumbnail_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, RefreshThumbnail);
    private void GallerySimilar_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Similar);
    private void GalleryFavorite_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, Favorite);
    private void GalleryMoveCategory_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, MoveCategory);
    private void GalleryEditTags_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, EditTags);
    private void GalleryProperties_Click(object sender, RoutedEventArgs e) => GalleryAction(sender, ShowProperties);

    private void Error(string title, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintVault"); Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:O} {title}: {ex}\r\n");
        }
        catch { }
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

public sealed class TextInputWindow : Window
{
    public string Value { get; private set; }
    public TextInputWindow(string title, string prompt, string initial)
    {
        Title = title; Width = 480; Height = 185; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = prompt, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Muted"], Margin = new Thickness(0, 0, 0, 7) };
        var box = new TextBox { Text = initial, Height = 34, Padding = new Thickness(8), Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"], Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"], BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"] };
        var apply = new Button { Content = "Apply", Height = 36, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(label, 0); Grid.SetRow(box, 1); Grid.SetRow(apply, 2); grid.Children.Add(label); grid.Children.Add(box); grid.Children.Add(apply); Content = grid; Value = initial;
        apply.Click += (_, _) => { Value = box.Text.Trim(); DialogResult = !string.IsNullOrWhiteSpace(Value); };
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}

public sealed class CategoryWindow : Window
{
    public string Value { get; private set; }
    private readonly TextBox box;
    private string selectedCategory;
    private readonly System.Windows.Media.Brush textBrush = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
    private readonly System.Windows.Media.Brush mutedBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
    private readonly System.Windows.Media.Brush panelBrush = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
    private readonly System.Windows.Media.Brush panel2Brush = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];

    public CategoryWindow(string current, IReadOnlyList<string> categories)
    {
        Value = current;
        selectedCategory = string.IsNullOrWhiteSpace(current) ? "Uncategorized" : current;
        Title = "Change Category";
        Width = 500; Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = panelBrush;
        Foreground = textBrush;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Choose an existing category or create a new one",
            Foreground = mutedBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // Deliberately use a Button + Popup + ListBox instead of a custom ComboBox
        // ControlTemplate. The previous template could open its Popup while the
        // ItemsPresenter measured to an empty visual area on the target Windows/WPF
        // runtime. This selector uses normal WPF controls all the way through.
        var selectorGrid = new Grid { Height = 36 };
        var selectorButton = new Button
        {
            Height = 36,
            Padding = new Thickness(10, 0, 34, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = panel2Brush,
            Foreground = textBrush,
            BorderBrush = mutedBrush,
            BorderThickness = new Thickness(1),
            Content = selectedCategory
        };
        var arrow = new TextBlock
        {
            Text = "▼",
            FontSize = 11,
            Foreground = textBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            IsHitTestVisible = false
        };
        selectorGrid.Children.Add(selectorButton);
        selectorGrid.Children.Add(arrow);

        var popupBorder = new Border
        {
            Background = panelBrush,
            BorderBrush = mutedBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Width = 460
        };
        var categoryList = new ListBox
        {
            ItemsSource = categories,
            Background = panelBrush,
            Foreground = textBrush,
            BorderThickness = new Thickness(0),
            MaxHeight = 280,
            MinHeight = 40
        };
        categoryList.ItemContainerStyle = new Style(typeof(ListBoxItem));
        categoryList.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
        categoryList.ItemContainerStyle.Setters.Add(new Setter(Control.BackgroundProperty, panelBrush));
        categoryList.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        categoryList.SelectedItem = selectedCategory;
        popupBorder.Child = categoryList;

        var popup = new Popup
        {
            PlacementTarget = selectorGrid,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = popupBorder
        };

        selectorButton.Click += (_, _) =>
        {
            categoryList.SelectedItem = selectedCategory;
            popup.IsOpen = true;
        };
        categoryList.SelectionChanged += (_, _) =>
        {
            if (categoryList.SelectedItem is string value && !string.IsNullOrWhiteSpace(value))
            {
                selectedCategory = value;
                selectorButton.Content = selectedCategory;
                popup.IsOpen = false;
            }
        };

        var selectorHost = new Grid();
        selectorHost.Children.Add(selectorGrid);
        selectorHost.Children.Add(popup);

        box = new TextBox
        {
            Height = 36,
            Padding = new Thickness(8),
            Foreground = textBrush,
            Background = panel2Brush,
            BorderBrush = mutedBrush,
            ToolTip = "Type a new custom category"
        };
        var hint = new TextBlock
        {
            Text = "Custom categories are saved permanently and can be used for future scans.",
            Foreground = mutedBrush,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var apply = new Button
        {
            Content = "Apply / Move File",
            Height = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        };

        Grid.SetRow(label, 0);
        Grid.SetRow(selectorHost, 1);
        Grid.SetRow(box, 2);
        Grid.SetRow(hint, 3);
        Grid.SetRow(apply, 4);
        grid.Children.Add(label);
        grid.Children.Add(selectorHost);
        grid.Children.Add(box);
        grid.Children.Add(hint);
        grid.Children.Add(apply);
        Content = grid;

        apply.Click += (_, _) =>
        {
            var typed = box.Text.Trim();
            Value = string.IsNullOrWhiteSpace(typed) ? selectedCategory : typed;
            if (string.IsNullOrWhiteSpace(Value)) Value = "Uncategorized";
            if (Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Value is "." or "..")
            {
                MessageBox.Show(this, "That category name is not valid for a Windows folder.", "Invalid Category", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };
    }
}

public sealed class SmartCategoryReviewWindow : Window
{
    private readonly SmartCategoryReviewPlan plan;
    private readonly ListBox list = new();
    private readonly TextBlock summary = new();

    public SmartCategoryReviewWindow(SmartCategoryReviewPlan plan, Window owner)
    {
        this.plan = plan;
        Owner = owner;
        Title = "PrintVault • Smart Category Review";
        Width = 1080; Height = 760;
        MinWidth = 900; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "SMART CATEGORY REVIEW", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Review suggested metadata categories before accepting them. No files or folders are moved.", Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Muted"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
        summary.Text = $"{plan.Scanned:N0} catalog records • {plan.Candidates:N0} candidate corrections • {plan.HighConfidence:N0} high-confidence • {plan.Protected:N0} protected/manual";
        summary.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
        header.Children.Add(summary);
        Grid.SetRow(header, 0); grid.Children.Add(header);

        list.Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];
        list.BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
        list.SelectionMode = SelectionMode.Multiple;
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        foreach (var suggestion in plan.Suggestions)
        {
            var row = new Grid { Margin = new Thickness(4), Opacity = suggestion.Protected ? .55 : 1.0 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            var name = new StackPanel();
            name.Children.Add(new TextBlock { Text = suggestion.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            name.Children.Add(new TextBlock { Text = suggestion.Evidence, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Muted"], FontSize = 11, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(name, 0); row.Children.Add(name);
            var current = new TextBlock { Text = suggestion.CurrentCategory, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Muted"], TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(current, 1); row.Children.Add(current);
            var target = new TextBlock { Text = suggestion.SuggestedCategory, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Accent"], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(target, 2); row.Children.Add(target);
            var confidence = new TextBlock { Text = suggestion.Protected ? "PROTECTED" : $"{suggestion.ConfidencePercent}%", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Foreground = suggestion.Protected ? (System.Windows.Media.Brush)Application.Current.Resources["Muted"] : (System.Windows.Media.Brush)Application.Current.Resources["Accent"], FontWeight = FontWeights.Bold };
            Grid.SetColumn(confidence, 3); row.Children.Add(confidence);
            var decision = new TextBlock { Text = suggestion.Decision, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = suggestion.Protected ? (System.Windows.Media.Brush)Application.Current.Resources["Muted"] : (suggestion.Decision == "AUTO" ? (System.Windows.Media.Brush)Application.Current.Resources["Accent"] : (System.Windows.Media.Brush)Application.Current.Resources["Muted"]), FontWeight = FontWeights.Bold };
            Grid.SetColumn(decision, 4); row.Children.Add(decision);
            var item = new ListBoxItem { Tag = suggestion, Content = row, Padding = new Thickness(8, 8, 8, 8) };
            if (!suggestion.Protected && suggestion.Decision == "AUTO") item.IsSelected = true;
            if (suggestion.Protected) item.IsEnabled = false;
            list.Items.Add(item);
        }
        Grid.SetRow(list, 2); grid.Children.Add(list);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selectHigh = Button("Select AUTO Only", (_, _) => SelectHigh());
        var selectReview = Button("Select REVIEW", (_, _) => SelectReview());
        var selectAll = Button("Select All", (_, _) => SelectAll());
        var apply = Button("Apply Selected", (_, _) => { if (list.SelectedItems.Count == 0) return; DialogResult = true; });
        var cancel = Button("Cancel", (_, _) => DialogResult = false);
        Grid.SetColumn(selectHigh, 0); Grid.SetColumn(selectReview, 1); Grid.SetColumn(selectAll, 2); Grid.SetColumn(apply, 4); Grid.SetColumn(cancel, 3);
        footer.Children.Add(selectHigh); footer.Children.Add(selectReview); footer.Children.Add(selectAll); footer.Children.Add(cancel); footer.Children.Add(apply);
        Grid.SetRow(footer, 3); grid.Children.Add(footer);
        Content = grid;
    }

    private Button Button(string text, RoutedEventHandler handler)
    {
        var b = new Button { Content = text, Height = 38, MinWidth = 130, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += handler; return b;
    }

    private void SelectHigh()
    {
        foreach (var item in list.Items.OfType<ListBoxItem>())
        {
            var s = item.Tag as SmartCategorySuggestion;
            item.IsSelected = s is not null && !s.Protected && s.Decision == "AUTO";
        }
    }


    private void SelectReview()
    {
        foreach (var item in list.Items.OfType<ListBoxItem>())
        {
            var s = item.Tag as SmartCategorySuggestion;
            item.IsSelected = s is not null && !s.Protected && s.Decision == "REVIEW";
        }
    }

    private void SelectAll()
    {
        foreach (var item in list.Items.OfType<ListBoxItem>()) item.IsSelected = item.IsEnabled;
    }

    public IReadOnlyList<SmartCategorySuggestion> SelectedSuggestions()
        => list.SelectedItems.OfType<ListBoxItem>().Select(x => x.Tag).OfType<SmartCategorySuggestion>().Where(x => !x.Protected).ToList();
}

public sealed class CategoryManagerWindow : Window
{
    private readonly List<string> categories;
    public string? SelectedCategory { get; private set; }
    private readonly ListBox list;
    private readonly TextBox input;
    private readonly System.Windows.Media.Brush textBrush = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
    private readonly System.Windows.Media.Brush mutedBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
    private readonly System.Windows.Media.Brush panelBrush = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
    private readonly System.Windows.Media.Brush panel2Brush = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];

    public CategoryManagerWindow(List<string> source)
    {
        categories = source;
        Title = "Category Manager";
        Width = 560; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = panelBrush;
        Foreground = textBrush;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Categories", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = textBrush, Margin = new Thickness(0,0,0,10) });
        list = new ListBox { ItemsSource = categories, Foreground = textBrush, Background = panel2Brush, BorderBrush = mutedBrush, SelectionMode = SelectionMode.Single };
        Grid.SetRow(list, 1); root.Children.Add(list);

        var addPanel = new Grid { Margin = new Thickness(0,12,0,0) };
        addPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input = new TextBox { Height = 36, Padding = new Thickness(8), Foreground = textBrush, Background = panel2Brush, BorderBrush = mutedBrush, ToolTip = "New custom category" };
        var add = new Button { Content = "Add Category", Width = 125, Height = 36, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(10,0,0,0) };
        add.Click += (_, _) => AddCategory(); input.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddCategory(); };
        addPanel.Children.Add(input); addPanel.Children.Add(add); Grid.SetColumn(add, 1);
        Grid.SetRow(addPanel, 2); root.Children.Add(addPanel);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var show = new Button { Content = "Show Models", Height = 36, Margin = new Thickness(0,0,10,0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var remove = new Button { Content = "Remove Custom Category", Height = 36, Margin = new Thickness(0,0,10,0) };
        var close = new Button { Content = "Done", Width = 90, Height = 36, HorizontalContentAlignment = HorizontalAlignment.Center };
        show.Click += (_, _) => ShowSelectedModels();
        remove.Click += (_, _) => RemoveCategory();
        close.Click += (_, _) => { DialogResult = true; };
        list.MouseDoubleClick += (_, _) => ShowSelectedModels();
        actions.Children.Add(show); actions.Children.Add(remove); actions.Children.Add(close); Grid.SetRow(actions, 3); root.Children.Add(actions);
        Content = root;
    }

    private void AddCategory()
    {
        var value = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.TrimEnd(' ', '.');
        if (value.Length == 0 || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "That category name is not valid for a Windows folder.", "Invalid Category", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (categories.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That category already exists.", "Category", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        categories.Add(value);
        input.Clear(); list.Items.Refresh(); list.SelectedItem = value;
    }

    private void ShowSelectedModels()
    {
        if (list.SelectedItem is not string value) return;
        SelectedCategory = value;
        DialogResult = true;
    }

    private void RemoveCategory()
    {
        if (list.SelectedItem is not string value) return;
        if (BuiltInCategories.All.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Built-in categories cannot be removed.", "Category", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Remove '{value}' from the category list? Existing files are not deleted.", "Remove Category", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        categories.Remove(value); list.Items.Refresh();
    }
}

public sealed class TagsWindow : Window
{
    public string Value { get; private set; } = "";
    private readonly TextBox box;
    private readonly System.Windows.Media.Brush textBrush = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
    private readonly System.Windows.Media.Brush mutedBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
    private readonly System.Windows.Media.Brush panelBrush = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
    private readonly System.Windows.Media.Brush panel2Brush = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];

    public TagsWindow(string category, IEnumerable<string> tags)
    {
        Value = string.Join(", ", tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
        Title = "Edit Tags"; Width = 560; Height = 260; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = panelBrush; Foreground = textBrush; ResizeMode = ResizeMode.NoResize;
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "Tags", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = textBrush };
        var hint = new TextBlock { Text = $"Category tag: {category}. Add other tags separated by commas.", Foreground = mutedBrush, Margin = new Thickness(0,5,0,10) };
        box = new TextBox { Text = Value, Height = 40, Padding = new Thickness(8), Foreground = textBrush, Background = panel2Brush, BorderBrush = mutedBrush };
        var apply = new Button { Content = "Save Tags", Height = 38, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,12,0,0) };
        Grid.SetRow(title,0); Grid.SetRow(hint,1); Grid.SetRow(box,2); Grid.SetRow(apply,3); grid.Children.Add(title); grid.Children.Add(hint); grid.Children.Add(box); grid.Children.Add(apply); Content=grid;
        apply.Click += (_, _) => { Value = string.Join(", ", box.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length <= 80).Distinct(StringComparer.OrdinalIgnoreCase)); DialogResult=true; };
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}

internal static class RecycleBin
{
    private const int FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHFILEOPSTRUCT
    {
        public nint hwnd; public int wFunc; public string pFrom; public string? pTo; public ushort fFlags; public int fAnyOperationsAborted; public nint hNameMappings; public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    public static void Delete(string path)
    {
        var op = new SHFILEOPSTRUCT { wFunc = FO_DELETE, pFrom = path + '\0', fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT };
        var result = SHFileOperation(ref op); if (result != 0) throw new IOException($"Windows could not move the file to the Recycle Bin. Error {result}.");
    }
}

public sealed class CategoryReconciliationWindow : Window
{
    private readonly CategoryReconciliationPlan plan;
    private readonly ListBox list;
    private readonly System.Windows.Media.Brush textBrush = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
    private readonly System.Windows.Media.Brush mutedBrush = (System.Windows.Media.Brush)Application.Current.Resources["Muted"];
    private readonly System.Windows.Media.Brush panelBrush = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
    private readonly System.Windows.Media.Brush panel2Brush = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];
    public bool ApplyChanges { get; private set; }
    
    public CategoryReconciliationWindow(CategoryReconciliationPlan plan, Window owner) : base()
    {
        this.plan = plan;
        Owner = owner;
        Title = "PrintVault • Category Reconciliation";
        Width = 820; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = panelBrush; Foreground = textBrush;

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Category Reconciliation", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"{plan.LegacyUsedCount:N0} legacy categories with files • {plan.EmptyLegacyCount:N0} empty legacy categories • {plan.ReviewCount:N0} requiring review • {plan.ProtectedCount:N0} protected files", Foreground = mutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 12) });
        header.Children.Add(new TextBlock { Text = "This reconciles the PrintVault catalog, not just folders. Empty legacy categories can be retired safely. Used legacy categories are mapped to canonical names only when the mapping is unambiguous. Protected/manual categories are never overwritten.", Foreground = mutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        grid.Children.Add(header);

        list = new ListBox { Background = panel2Brush, BorderBrush = mutedBrush, Foreground = textBrush, SelectionMode = SelectionMode.Multiple };
        foreach (var item in plan.Items)
        {
            var target = string.IsNullOrWhiteSpace(item.TargetCategory) ? "REVIEW" : item.TargetCategory;
            var row = new ListBoxItem { Tag = item, Padding = new Thickness(10, 8, 10, 8), Content = new TextBlock { Text = $"{item.SourceCategory}  →  {target}    • {item.FileCount:N0} files    • {item.Status}\n{item.Reason}", TextWrapping = TextWrapping.Wrap } };
            list.Items.Add(row);
            if (!string.Equals(item.Status, "Review", StringComparison.OrdinalIgnoreCase)) row.IsSelected = true;
        }
        // The ListBox displays items as ListBoxItems, so expose the underlying model through selection.
        list.SelectionChanged += (_, _) => { };
        Grid.SetRow(list, 1); grid.Children.Add(list);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new TextBlock { Text = "Nothing is deleted. Retired categories are removed from the category registry only. File changes are recorded for undo.", Foreground = mutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "Apply Reconciliation", Width = 170, Height = 38, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 100, Height = 38 };
        apply.Click += (_, _) => { ApplyChanges = true; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(apply); buttons.Children.Add(cancel);
        footer.Children.Add(buttons); Grid.SetRow(footer, 2); grid.Children.Add(footer);
        Content = grid;
    }

    public IReadOnlySet<string> SelectedSourceNames()
    {
        return list.SelectedItems.OfType<ListBoxItem>().Select(x => x.Tag).OfType<CategoryReconciliationItem>().Select(x => x.SourceCategory).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
