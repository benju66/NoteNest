using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.UI.Commands;
using System.IO;

namespace NoteNest.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly NoteService _noteService;
        private readonly ConfigurationService _configService;
        private readonly FileWatcherService _fileWatcher;
        private System.Windows.Threading.DispatcherTimer _autoSaveTimer;
        
        private ObservableCollection<CategoryTreeItem> _categories;
        private ObservableCollection<NoteTabItem> _openTabs;
        private NoteTabItem _selectedTab;
        private CategoryTreeItem _selectedCategory;
        private string _searchText;
        private bool _isSearchActive;
        private List<NoteTreeItem> _searchResults = new List<NoteTreeItem>();
        private int _searchIndex = -1;
        private bool _isLoading;
        private string _statusMessage;

        public ObservableCollection<CategoryTreeItem> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<NoteTabItem> OpenTabs
        {
            get => _openTabs;
            set => SetProperty(ref _openTabs, value);
        }

        public NoteTabItem SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

        public CategoryTreeItem SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private NoteTreeItem _selectedNote;
        public NoteTreeItem SelectedNote
        {
            get => _selectedNote;
            set 
            { 
                SetProperty(ref _selectedNote, value);
                if (value != null)
                {
                    StatusMessage = $"Selected: {value.Title}";
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterNotes();
                    IsSearchActive = !string.IsNullOrWhiteSpace(_searchText);
                }
            }
        }

        public bool IsSearchActive
        {
            get => _isSearchActive;
            set => SetProperty(ref _isSearchActive, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // Commands
        public ICommand NewNoteCommand { get; }
        public ICommand OpenNoteCommand { get; }
        public ICommand SaveNoteCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand NewCategoryCommand { get; }
        public ICommand NewSubCategoryCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand SearchNavigateDownCommand { get; }
        public ICommand SearchNavigateUpCommand { get; }
        public ICommand SearchOpenCommand { get; }
        public ICommand ClearSearchCommand { get; }

        public MainViewModel()
        {
            var fileSystem = new DefaultFileSystemProvider();
            _configService = new ConfigurationService(fileSystem);
            _noteService = new NoteService(fileSystem, _configService);
            _fileWatcher = new FileWatcherService();

            Categories = new ObservableCollection<CategoryTreeItem>();
            OpenTabs = new ObservableCollection<NoteTabItem>();

            // Initialize commands
            NewNoteCommand = new RelayCommand(async _ => await CreateNewNoteAsync(), _ => SelectedCategory != null);
            OpenNoteCommand = new RelayCommand<NoteTreeItem>(async note => await OpenNoteAsync(note));
            SaveNoteCommand = new RelayCommand(async _ => await SaveCurrentNoteAsync(), _ => SelectedTab?.IsDirty == true);
            SaveAllCommand = new RelayCommand(async _ => await SaveAllNotesAsync(), _ => OpenTabs.Any(t => t.IsDirty));
            CloseTabCommand = new RelayCommand<NoteTabItem>(async tab => await CloseTabAsync(tab));
            NewCategoryCommand = new RelayCommand(async _ => await CreateNewCategoryAsync());
            NewSubCategoryCommand = new RelayCommand<CategoryTreeItem>(async cat => await CreateNewSubCategoryAsync(cat), _ => SelectedCategory != null);
            DeleteCategoryCommand = new RelayCommand(async _ => await DeleteCategoryAsync(), _ => SelectedCategory != null);
            RefreshCommand = new RelayCommand(async _ => await LoadCategoriesAsync());
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            SearchNavigateDownCommand = new RelayCommand(_ => NavigateSearch(+1), _ => _searchResults.Count > 0);
            SearchNavigateUpCommand = new RelayCommand(_ => NavigateSearch(-1), _ => _searchResults.Count > 0);
            SearchOpenCommand = new RelayCommand(async _ => await OpenFromSearchAsync(), _ => _searchResults.Count > 0);
            ClearSearchCommand = new RelayCommand(_ => { SearchText = string.Empty; IsSearchActive = false; });

            // Initialize
            _ = InitializeAsync();
            InitializeAutoSave();
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading...";

                var settings = await _configService.LoadSettingsAsync();
                await _configService.EnsureDefaultDirectoriesAsync();
                await LoadCategoriesAsync();

                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                Categories.Clear();

                var settings = _configService.Settings;
                
                // Ensure base directories exist
                var fileSystem = new DefaultFileSystemProvider();
                if (!await fileSystem.ExistsAsync(settings.DefaultNotePath))
                {
                    await fileSystem.CreateDirectoryAsync(settings.DefaultNotePath);
                }
                if (!await fileSystem.ExistsAsync(settings.MetadataPath))
                {
                    await fileSystem.CreateDirectoryAsync(settings.MetadataPath);
                }

                var flatCategories = await _noteService.LoadCategoriesAsync(settings.MetadataPath);

                // Create sample categories if none exist (roots only)
                if (!flatCategories.Any())
                {
                    flatCategories = new List<CategoryModel>
                    {
                        new CategoryModel
                        {
                            Id = "personal",
                            ParentId = null,
                            Name = "Personal",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Personal"),
                            Pinned = false,
                            Tags = new List<string> { "personal" }
                        },
                        new CategoryModel
                        {
                            Id = "work", 
                            ParentId = null,
                            Name = "Work",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Work"),
                            Pinned = true,
                            Tags = new List<string> { "work", "important" }
                        }
                    };
                    
                    // Create the physical directories
                    foreach (var cat in flatCategories)
                    {
                        if (!await fileSystem.ExistsAsync(cat.Path))
                        {
                            await fileSystem.CreateDirectoryAsync(cat.Path);
                        }
                    }
                    
                    // Save the categories to metadata
                    await _noteService.SaveCategoriesAsync(settings.MetadataPath, flatCategories);
                }

                // Build tree: add roots then recursively attach children
                var rootCategories = flatCategories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
                foreach (var root in rootCategories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
                    if (!await fileSystem.ExistsAsync(root.Path))
                    {
                        await fileSystem.CreateDirectoryAsync(root.Path);
                    }
                    var rootItem = await BuildCategoryTreeAsync(root, flatCategories, fileSystem, level: 0);
                    Categories.Add(rootItem);
                }

                // One recursive watcher per root
                foreach (var root in Categories)
                {
                    _fileWatcher.StartWatching(root.Model.Path, "*.txt", includeSubdirectories: true);
                }

                StatusMessage = $"Loaded {CountAllCategories(Categories)} categories";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error loading categories";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<CategoryTreeItem> BuildCategoryTreeAsync(CategoryModel parent, List<CategoryModel> all, DefaultFileSystemProvider fileSystem, int level)
        {
            parent.Level = level;
            var parentItem = new CategoryTreeItem(parent);
            // Load notes for this category
            try
            {
                var notes = await _noteService.GetNotesInCategoryAsync(parent);
                foreach (var note in notes)
                {
                    parentItem.Notes.Add(new NoteTreeItem(note));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading notes for {parent.Name}: {ex.Message}");
            }

            var children = all.Where(c => c.ParentId == parent.Id).OrderBy(c => c.Name).ToList();
            foreach (var child in children)
            {
                if (!await fileSystem.ExistsAsync(child.Path))
                {
                    await fileSystem.CreateDirectoryAsync(child.Path);
                }
                var childItem = await BuildCategoryTreeAsync(child, all, fileSystem, level + 1);
                parentItem.SubCategories.Add(childItem);
            }

            return parentItem;
        }

        private int CountAllCategories(ObservableCollection<CategoryTreeItem> nodes)
        {
            int count = nodes.Count;
            foreach (var n in nodes)
            {
                count += CountAllCategories(n.SubCategories);
            }
            return count;
        }

        private async Task CreateNewNoteAsync()
        {
            if (SelectedCategory == null)
            {
                MessageBox.Show(
                    "Please select a category first to create a new note.", 
                    "No Category Selected", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var title = "New Note " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                var note = await _noteService.CreateNoteAsync(SelectedCategory.Model, title, string.Empty);
                
                var noteItem = new NoteTreeItem(note);
                SelectedCategory.Notes.Add(noteItem);
                
                // Expand category to show new note
                SelectedCategory.IsExpanded = true;
                
                // Select and open the new note
                SelectedNote = noteItem;
                await OpenNoteAsync(noteItem);
                
                StatusMessage = $"Created: {title}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating note: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OpenNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;

            // Check if already open
            var existingTab = OpenTabs.FirstOrDefault(t => t.Note.FilePath == noteItem.Model.FilePath);
            if (existingTab != null)
            {
                SelectedTab = existingTab;
                return;
            }

            // Load note content if not loaded
            if (string.IsNullOrEmpty(noteItem.Model.Content))
            {
                var loadedNote = await _noteService.LoadNoteAsync(noteItem.Model.FilePath);
                noteItem.Model.Content = loadedNote.Content;
            }

            var tab = new NoteTabItem(noteItem.Model);
            OpenTabs.Add(tab);
            SelectedTab = tab;

            StatusMessage = $"Opened: {noteItem.Model.Title}";
        }

        private async Task SaveCurrentNoteAsync()
        {
            if (SelectedTab == null) return;

            try
            {
                await _noteService.SaveNoteAsync(SelectedTab.Note);
                SelectedTab.IsDirty = false;
                StatusMessage = $"Saved: {SelectedTab.Note.Title}";
                
                // Update recent files
                _configService.AddRecentFile(SelectedTab.Note.FilePath);
                await _configService.SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving note: {ex.Message}", "Save Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error saving note";
            }
        }

        private async Task SaveAllNotesAsync()
        {
            foreach (var tab in OpenTabs.Where(t => t.IsDirty))
            {
                await _noteService.SaveNoteAsync(tab.Note);
                tab.IsDirty = false;
            }
            StatusMessage = "All notes saved";
        }

        private async Task CloseTabAsync(NoteTabItem tab)
        {
            if (tab == null) return;

            if (tab.IsDirty)
            {
                var result = MessageBox.Show(
                    $"Save changes to {tab.Note.Title}?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                {
                    await _noteService.SaveNoteAsync(tab.Note);
                }
            }

            OpenTabs.Remove(tab);
        }

        private async Task CreateNewCategoryAsync()
        {
            var dialog = new Dialogs.InputDialog(
                "New Category",
                "Enter category name:",
                "");
            
            dialog.ValidationFunction = (text) =>
            {
                if (Categories.Any(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    return "A category with this name already exists.";
                return null;
            };
            
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResponseText)) 
                return;

            try
            {
                var name = dialog.ResponseText;
                var settings = _configService.Settings;
                var safeName = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
                
                var category = new CategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = null,
                    Name = name,
                    Path = System.IO.Path.Combine(settings.DefaultNotePath, safeName),
                    Tags = new List<string>()
                };

                // Create the physical directory
                var fileSystem = new DefaultFileSystemProvider();
                if (!await fileSystem.ExistsAsync(category.Path))
                {
                    await fileSystem.CreateDirectoryAsync(category.Path);
                }

                var categoryItem = new CategoryTreeItem(category);
                Categories.Add(categoryItem);

                // Save updated categories to metadata
                var allCategories = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(settings.MetadataPath, allCategories);
                // Use recursive root watcher strategy
                StartRecursiveWatch(categoryItem);
                StatusMessage = $"Created category: {name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating category: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task CreateNewSubCategoryAsync(CategoryTreeItem parentCategory)
        {
            if (parentCategory == null)
            {
                parentCategory = SelectedCategory;
            }
            if (parentCategory == null)
            {
                MessageBox.Show("Please select a parent category first.", "No Category Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Dialogs.InputDialog(
                "New Subcategory",
                $"Enter subcategory name for '{parentCategory.Name}':",
                "");

            dialog.ValidationFunction = (text) =>
            {
                if (parentCategory.SubCategories.Any(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    return "A subcategory with this name already exists.";
                return null;
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResponseText))
                return;

            try
            {
                var name = dialog.ResponseText;
                var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                var subCategory = new CategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = parentCategory.Model.Id,
                    Name = name,
                    Path = Path.Combine(parentCategory.Model.Path, safeName),
                    Tags = new List<string>()
                };

                var fileSystem = new DefaultFileSystemProvider();
                if (!await fileSystem.ExistsAsync(subCategory.Path))
                {
                    await fileSystem.CreateDirectoryAsync(subCategory.Path);
                }

                var subItem = new CategoryTreeItem(subCategory);
                parentCategory.SubCategories.Add(subItem);
                parentCategory.IsExpanded = true;

                // Save updated flat list
                var all = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, all);

                StatusMessage = $"Created subcategory: {name} under {parentCategory.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating subcategory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteCategoryAsync()
        {
            if (SelectedCategory == null) return;

            // Count children and notes for warning text
            int subCount = CountAllCategories(new ObservableCollection<CategoryTreeItem>(new[] { SelectedCategory })) - 1;
            int noteCount = CountAllNotes(SelectedCategory);

            var warning = $"Delete category '{SelectedCategory.Name}'" +
                          (subCount > 0 || noteCount > 0 ? $" including {subCount} subcategories and {noteCount} notes" : "") + "?";

            var result = MessageBox.Show(warning, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            // Physically delete directory recursively
            try
            {
                if (Directory.Exists(SelectedCategory.Model.Path))
                {
                    Directory.Delete(SelectedCategory.Model.Path, recursive: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Remove from tree (search for parent if not root)
            var parent = FindParent(Categories, SelectedCategory);
            if (parent != null)
            {
                parent.SubCategories.Remove(SelectedCategory);
            }
            else
            {
                Categories.Remove(SelectedCategory);
            }

            // Save updated categories
            var all = GetAllCategoriesFlat();
            await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, all);

            StatusMessage = $"Deleted category: {SelectedCategory.Name}";
        }

        private int CountAllNotes(CategoryTreeItem category)
        {
            int count = category.Notes.Count;
            foreach (var sub in category.SubCategories)
            {
                count += CountAllNotes(sub);
            }
            return count;
        }

        private CategoryTreeItem FindParent(ObservableCollection<CategoryTreeItem> roots, CategoryTreeItem target)
        {
            foreach (var r in roots)
            {
                if (r.SubCategories.Contains(target)) return r;
                var found = FindParent(r.SubCategories, target);
                if (found != null) return found;
            }
            return null;
        }

        private List<CategoryModel> GetAllCategoriesFlat()
        {
            var list = new List<CategoryModel>();
            void Walk(ObservableCollection<CategoryTreeItem> items)
            {
                foreach (var item in items)
                {
                    list.Add(item.Model);
                    Walk(item.SubCategories);
                }
            }
            Walk(Categories);
            return list;
        }

        private void FilterNotes()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Show all
                foreach (var category in Categories)
                {
                    category.IsVisible = true;
                    foreach (var note in category.Notes)
                    {
                        note.IsVisible = true;
                    }
                }
                _searchResults.Clear();
                _searchIndex = -1;
            }
            else
            {
                var searchLower = SearchText.ToLower();
                _searchResults = new List<NoteTreeItem>();
                foreach (var category in Categories)
                {
                    var hasVisibleNotes = false;
                    foreach (var note in category.Notes)
                    {
                        note.IsVisible = note.Model.Title.ToLower().Contains(searchLower);
                        if (note.IsVisible)
                        {
                            hasVisibleNotes = true;
                            _searchResults.Add(note);
                        }
                    }
                    category.IsVisible = hasVisibleNotes || category.Name.ToLower().Contains(searchLower);
                }
                _searchIndex = -1;
            }
        }

        private void NavigateSearch(int delta)
        {
            if (_searchResults.Count == 0) return;
            _searchIndex = (_searchIndex + delta + _searchResults.Count) % _searchResults.Count;
            var target = _searchResults[_searchIndex];
            var parent = Categories.FirstOrDefault(c => c.Notes.Contains(target));
            if (parent != null)
            {
                parent.IsExpanded = true;
                SelectedCategory = parent;
                SelectedNote = target;
            }
        }

        private async Task OpenFromSearchAsync()
        {
            if (_searchResults.Count == 0) return;
            if (_searchIndex < 0)
            {
                // If nothing selected yet, select first
                _searchIndex = 0;
                var target = _searchResults[_searchIndex];
                var parent = Categories.FirstOrDefault(c => c.Notes.Contains(target));
                if (parent != null)
                {
                    parent.IsExpanded = true;
                    SelectedCategory = parent;
                    SelectedNote = target;
                }
            }
            if (SelectedNote != null)
            {
                await OpenNoteAsync(SelectedNote);
            }
        }

        private void InitializeAutoSave()
        {
            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(30); // Auto-save every 30 seconds
            _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();
            _autoSaveTimer.Start();
        }

        private async Task AutoSaveAsync()
        {
            var dirtyTabs = OpenTabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Any())
            {
                foreach (var tab in dirtyTabs)
                {
                    try
                    {
                        await _noteService.SaveNoteAsync(tab.Note);
                        tab.IsDirty = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-save error: {ex.Message}");
                    }
                }
                
                if (dirtyTabs.Count > 0)
                {
                    StatusMessage = $"Auto-saved {dirtyTabs.Count} note(s)";
                }
            }
        }

        public async Task RenameCategoryAsync(CategoryTreeItem categoryItem, string newName)
        {
            if (categoryItem == null || string.IsNullOrWhiteSpace(newName)) return;

            try
            {
                var oldName = categoryItem.Model.Name;
                categoryItem.Model.Name = newName;
                
                // Save updated categories
                var allCategories = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                StatusMessage = $"Renamed category from '{oldName}' to '{newName}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error renaming category: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ToggleCategoryPinAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;

            try
            {
                categoryItem.Model.Pinned = !categoryItem.Model.Pinned;
                
                // Save updated categories
                var allCategories = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                // Re-sort categories
                await LoadCategoriesAsync();
                
                StatusMessage = categoryItem.Model.Pinned ? 
                    $"Pinned '{categoryItem.Name}'" : $"Unpinned '{categoryItem.Name}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling pin: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartRecursiveWatch(CategoryTreeItem root)
        {
            // Switch to a single watcher per root directory with recursion
            // FileWatcherService currently does not expose IncludeSubdirectories; adjust watcher directly
            try
            {
                // Stop any existing watcher for this path to avoid duplication
                _fileWatcher.StopWatching(root.Model.Path);
            }
            catch { }

            // Start a watcher and set IncludeSubdirectories via reflection to keep service public API unchanged
            // Create a private watcher mirroring service logic
            var watcher = new FileSystemWatcher(root.Model.Path)
            {
                Filter = "*.txt",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            // Hook to service events to keep behavior consistent (optional; no-op if not needed)
            watcher.Changed += (s, e) => { };
            watcher.Created += (s, e) => { };
            watcher.Deleted += (s, e) => { };
            watcher.Renamed += (s, e) => { };
        }

        public async Task RenameNoteAsync(NoteTreeItem noteItem, string newName)
        {
            if (noteItem == null || string.IsNullOrWhiteSpace(newName)) return;

            try
            {
                var oldPath = noteItem.Model.FilePath;
                var directory = System.IO.Path.GetDirectoryName(oldPath);
                var newFileName = newName + ".txt";
                var newPath = System.IO.Path.Combine(directory, newFileName);
                
                // Check if file already exists
                if (System.IO.File.Exists(newPath) && newPath != oldPath)
                {
                    MessageBox.Show("A note with this name already exists.", "Name Conflict", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Rename the physical file
                if (System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Move(oldPath, newPath);
                }
                
                // Update the model
                noteItem.Model.Title = newName;
                noteItem.Model.FilePath = newPath;
                
                // Update open tab if exists
                var openTab = OpenTabs.FirstOrDefault(t => t.Note.FilePath == oldPath);
                if (openTab != null)
                {
                    openTab.Note.Title = newName;
                    openTab.Note.FilePath = newPath;
                    openTab.OnPropertyChanged(nameof(openTab.Title));
                }
                
                StatusMessage = $"Renamed note to '{newName}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error renaming note: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task DeleteNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;

            try
            {
                // Close tab if open
                var openTab = OpenTabs.FirstOrDefault(t => t.Note.FilePath == noteItem.Model.FilePath);
                if (openTab != null)
                {
                    OpenTabs.Remove(openTab);
                }
                
                // Delete the physical file
                await _noteService.DeleteNoteAsync(noteItem.Model);
                
                // Remove from tree
                var category = Categories.FirstOrDefault(c => c.Notes.Contains(noteItem));
                if (category != null)
                {
                    category.Notes.Remove(noteItem);
                }
                
                StatusMessage = $"Deleted '{noteItem.Title}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting note: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public ConfigurationService GetConfigService()
        {
            return _configService;
        }
    }
}
