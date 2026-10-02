from pathlib import Path
root=Path(__file__).resolve().parents[1]
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text()
cs=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
checks={
 'category_filter':'x:Name="CategoryFilter"' in xaml and 'BrowseFilter_Changed' in xaml,
 'tag_filter':'x:Name="TagFilter"' in xaml and 'BrowseFilter_Changed' in xaml,
 'clear_filters':'Content="Clear Filters"' in xaml and 'ClearFilters_Click' in cs,
 'dashboard_category_navigation':'CategoryStat_Click' in xaml and 'categoryFilter = name' in cs,
 'header_sort':'GridViewColumnHeader.Click="GridViewColumnHeader_Click"' in xaml,
 'tag_sort':'"Tags" => nameof(ModelRecord.Tags)' in cs,
 'category_sort':'"Category" => nameof(ModelRecord.Category)' in cs,
 'size_sort':'"Size" => nameof(ModelRecord.Size)' in cs,
 'sort_toggle':'sortDirection == ListSortDirection.Ascending' in cs,
 'tag_parsing':'SplitTags' in cs,
 'active_filter_count':'models match the current filters' in cs,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('BROWSE/SORT TEST FAILED: '+', '.join(failed))
print(f'BROWSE/SORT TEST PASSED — {len(checks)} checks')
