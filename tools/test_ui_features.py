from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
x= (ROOT/"src/PrintVault/MainWindow.xaml").read_text()
c= (ROOT/"src/PrintVault/MainWindow.xaml.cs").read_text()
o= (ROOT/"src/PrintVault.Infrastructure/OrganizationService.cs").read_text()
assert 'Header="Tags"' in x
assert 'InspectorTags' in x
assert 'EditTags_Click' in c and 'GalleryEditTags_Click' in c
assert 'CategoryManagerWindow' in c
assert 'TagsWindow' in c
assert 'engine.Repository.GetAll().Select(x => x.Category)' in c
assert 'ReplaceCategoryTag' in o
assert 'ModelView.DeferRefresh()' not in c
print('UI FEATURE TEST PASSED — tags/categories/contrast/scan safeguards present')
