using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Commands;
using System.IO;

namespace NoteNest.UI.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly NoteService _noteService;
        private readonly ConfigurationService _configService;
        private readonly FileWatcherService _fileWatcher;
        private readonly IAppLogger _logger;
        private DispatcherTimer _autoSaveTimer;
        private bool _disposed;
        
        private ObservableCollection<CategoryTreeItem> _categories;
        private ObservableCollection<NoteTabItem> _openTabs;
        private NoteTabItem _selectedTab;
        private CategoryTreeItem _selectedCategory;
        private NoteTreeItem _selectedNote;
        private string _searchText;
        private bool _isSearchActive;
        private List<NoteTreeItem> _searchResults = new List<NoteTreeItem>();
        private int _searchIndex = -1;
        private bool _isLoading;
        private string _statusMessage;

        #region Properties

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

        #endregion

        #region Commands

        public ICommand NewNoteCommand { get; private set; }
        public ICommand OpenNoteCommand { get; private set; }
        public ICommand SaveNoteCommand { get; private set; }
        public ICommand SaveAllCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        public ICommand NewCategoryCommand { get; private set; }
        public ICommand NewSubCategoryCommand { get; private set; }
        public ICommand DeleteCategoryCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand ExitCommand { get; private set; }
        public ICommand SearchNavigateDownCommand { get; private set; }
        public ICommand SearchNavigateUpCommand { get; private set; }
        public ICommand SearchOpenCommand { get; private set; }
        public ICommand ClearSearchCommand { get; private set; }

        #endregion

        public MainViewModel()
        {
            _logger = AppLogger.Instance;
            _logger.Info("Initializing MainViewModel");

            try
            {
                // Initialize services
                var fileSystem = new DefaultFileSystemProvider();
                _configService = new ConfigurationService(fileSystem);
                _noteService = new NoteService(fileSystem, _configService, _logger);
                _fileWatcher = new FileWatcherService(_logger);

                // Initialize collections
                Categories = new ObservableCollection<CategoryTreeItem>();
                OpenTabs = new ObservableCollection<NoteTabItem>();

                // Initialize commands
                InitializeCommands();

                // Load initial data and initialize auto-save after settings are loaded
                _ = InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Failed to initialize MainViewModel");
                MessageBox.Show(
                    "Failed to initialize application. Please check the log file for details.",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeCommands()
        {
            NewNoteCommand = new RelayCommand(async _ => await CreateNewNoteAsync(), _ => SelectedCategory != null);
            OpenNoteCommand = new RelayCommand<NoteTreeItem>(async note => await OpenNoteAsync(note));
            SaveNoteCommand = new RelayCommand(async _ => await SaveCurrentNoteAsync(), _ => SelectedTab?.IsDirty == true);
            SaveAllCommand = new RelayCommand(async _ => await SaveAllNotesAsync(), _ => OpenTabs.Any(t => t.IsDirty));
            CloseTabCommand = new RelayCommand<NoteTabItem>(async tab => await CloseTabAsync(tab));
            NewCategoryCommand = new RelayCommand(async _ => await CreateNewCategoryAsync());
            NewSubCategoryCommand = new RelayCommand<CategoryTreeItem>(async cat => await CreateNewSubCategoryAsync(cat), _ => SelectedCategory != null);
            DeleteCategoryCommand = new RelayCommand(async _ => await DeleteCategoryAsync(), _ => SelectedCategory != null);
            RefreshCommand = new RelayCommand(async _ => await LoadCategoriesAsync());
            ExitCommand = new RelayCommand(_ => 
            {
                try
                {
                    _logger.Info("ExitCommand triggered - initiating shutdown");
                    // Save any pending changes before exit
                    if (OpenTabs?.Any(t => t.IsDirty) == true)
                    {
                        SaveAllNotesAsync().GetAwaiter().GetResult();
                    }
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during exit command");
                    // Force shutdown even if there's an error
                    System.Environment.Exit(0);
                }
            });
            SearchNavigateDownCommand = new RelayCommand(_ => NavigateSearch(+1), _ => _searchResults.Count > 0);
            SearchNavigateUpCommand = new RelayCommand(_ => NavigateSearch(-1), _ => _searchResults.Count > 0);
            SearchOpenCommand = new RelayCommand(async _ => await OpenFromSearchAsync(), _ => _searchResults.Count > 0);
            ClearSearchCommand = new RelayCommand(_ => { SearchText = string.Empty; IsSearchActive = false; });
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading...";

                var settings = await _configService.LoadSettingsAsync();
                
                // Use PathService for proper paths
                settings.DefaultNotePath = PathService.ProjectsPath;
                settings.MetadataPath = PathService.MetadataPath;
                
                await _configService.EnsureDefaultDirectoriesAsync();

                // Initialize auto-save after settings are loaded
                InitializeAutoSave();

                await LoadCategoriesAsync();

                StatusMessage = "Ready";
                _logger.Info("Application initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during initialization");
                MessageBox.Show($"Error initializing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Initialization error";
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
                
                // Use PathService for consistent paths
                settings.DefaultNotePath = PathService.ProjectsPath;
                settings.MetadataPath = PathService.MetadataPath;
                
                // Ensure directories exist
                PathService.EnsureDirectoriesExist();

                var flatCategories = await _noteService.LoadCategoriesAsync(settings.MetadataPath);

                // LoadCategoriesAsync now handles default creation internally
                if (!flatCategories.Any())
                {
                    _logger.Info("No categories loaded - defaults should have been created");
                }

                // Stop all existing watchers before setting up new ones
                _fileWatcher.StopAllWatchers();

                // Build tree structure
                var rootCategories = flatCategories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
                foreach (var root in rootCategories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
                    var rootItem = await BuildCategoryTreeAsync(root, flatCategories, 0);
                    Categories.Add(rootItem);
                }

                // Use single recursive watcher for the entire projects directory
                _fileWatcher.StartWatching(PathService.ProjectsPath, "*.txt", includeSubdirectories: true);

                StatusMessage = $"Loaded {flatCategories.Count} categories";
                _logger.Info($"Successfully loaded {flatCategories.Count} categories");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories");
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error loading categories";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<CategoryTreeItem> BuildCategoryTreeAsync(CategoryModel parent, List<CategoryModel> all, int level)
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
                _logger.Warning($"Error loading notes for {parent.Name}: {ex.Message}");
            }

            // Build subcategories
            var children = all.Where(c => c.ParentId == parent.Id).OrderBy(c => c.Name).ToList();
            foreach (var child in children)
            {
                var childItem = await BuildCategoryTreeAsync(child, all, level + 1);
                parentItem.SubCategories.Add(childItem);
            }

            return parentItem;
        }

        #region Note Operations

        private async Task CreateNewNoteAsync()
        {
            await SafeExecuteAsync(async () =>
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

                var title = "New Note " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                var note = await _noteService.CreateNoteAsync(SelectedCategory.Model, title, string.Empty);
                
                var noteItem = new NoteTreeItem(note);
                SelectedCategory.Notes.Add(noteItem);
                SelectedCategory.IsExpanded = true;
                
                SelectedNote = noteItem;
                await OpenNoteAsync(noteItem);
                
                StatusMessage = $"Created: {title}";
                _logger.Info($"Created new note: {title}");
            }, "Create New Note");
        }

        private async Task OpenNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;

            await SafeExecuteAsync(async () =>
            {
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
                _logger.Debug($"Opened note: {noteItem.Model.FilePath}");
            }, "Open Note");
        }

        private async Task SaveCurrentNoteAsync()
        {
            if (SelectedTab == null) return;

            await SafeExecuteAsync(async () =>
            {
                await _noteService.SaveNoteAsync(SelectedTab.Note);
                SelectedTab.IsDirty = false;
                StatusMessage = $"Saved: {SelectedTab.Note.Title}";
                
                _configService.AddRecentFile(SelectedTab.Note.FilePath);
                await _configService.SaveSettingsAsync();
                
                _logger.Info($"Saved note: {SelectedTab.Note.Title}");
            }, "Save Note");
        }

        private async Task SaveAllNotesAsync()
        {
            await SafeExecuteAsync(async () =>
            {
                var savedCount = 0;
                foreach (var tab in OpenTabs.Where(t => t.IsDirty))
                {
                    await _noteService.SaveNoteAsync(tab.Note);
                    tab.IsDirty = false;
                    savedCount++;
                }
                
                if (savedCount > 0)
                {
                    StatusMessage = $"Saved {savedCount} notes";
                    _logger.Info($"Saved {savedCount} notes");
                }
            }, "Save All Notes");
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
                    await SaveCurrentNoteAsync();
                }
            }

            OpenTabs.Remove(tab);
            _logger.Debug($"Closed tab: {tab.Note.Title}");
        }

        #endregion

        #region Category Operations

        private async Task CreateNewCategoryAsync()
        {
            await SafeExecuteAsync(async () =>
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

                var name = dialog.ResponseText;
                var safeName = PathService.SanitizeName(name);
                
                var category = new CategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = null,
                    Name = name,
                    Path = Path.Combine(PathService.ProjectsPath, safeName), // Absolute path for runtime
                    Tags = new List<string>()
                };

                var fileSystem = new DefaultFileSystemProvider();
                if (!await fileSystem.ExistsAsync(category.Path))
                {
                    await fileSystem.CreateDirectoryAsync(category.Path);
                }

                // Add to current flat list and save
                var allCategories = GetAllCategoriesFlat();
                allCategories.Add(category);
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                await LoadCategoriesAsync(); // Reload to include new category
                StatusMessage = $"Created category: {name}";
                _logger.Info($"Created category: {name}");
            }, "Create Category");
        }

        public async Task CreateNewSubCategoryAsync(CategoryTreeItem parentCategory)
        {
            await SafeExecuteAsync(async () =>
            {
                if (parentCategory == null)
                {
                    parentCategory = SelectedCategory;
                }
                if (parentCategory == null)
                {
                    MessageBox.Show("Please select a parent category first.", "No Category Selected", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
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

                var name = dialog.ResponseText;
                var safeName = PathService.SanitizeName(name);
                var subCategory = new CategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    ParentId = parentCategory.Model.Id,
                    Name = name,
                    Path = Path.Combine(parentCategory.Model.Path, safeName), // Absolute path for runtime
                    Tags = new List<string>()
                };

                var fileSystem = new DefaultFileSystemProvider();
                if (!await fileSystem.ExistsAsync(subCategory.Path))
                {
                    await fileSystem.CreateDirectoryAsync(subCategory.Path);
                }

                // Add to flat list and save
                var allCategories = GetAllCategoriesFlat();
                allCategories.Add(subCategory);
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                await LoadCategoriesAsync(); // Reload to include new subcategory
                StatusMessage = $"Created subcategory: {name} under {parentCategory.Name}";
                _logger.Info($"Created subcategory: {name} under {parentCategory.Name}");
            }, "Create Subcategory");
        }

        private async Task DeleteCategoryAsync()
        {
            if (SelectedCategory == null) return;

            await SafeExecuteAsync(async () =>
            {
                int subCount = CountAllCategories(new ObservableCollection<CategoryTreeItem>(new[] { SelectedCategory })) - 1;
                int noteCount = CountAllNotes(SelectedCategory);

                var warning = $"Delete category '{SelectedCategory.Name}'" +
                              (subCount > 0 || noteCount > 0 ? $" including {subCount} subcategories and {noteCount} notes" : "") + "?";

                var result = MessageBox.Show(warning, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;

                var categoryName = SelectedCategory.Name;
                var categoryId = SelectedCategory.Model.Id;
                
                // Delete physical directory
                if (Directory.Exists(SelectedCategory.Model.Path))
                {
                    Directory.Delete(SelectedCategory.Model.Path, recursive: true);
                }

                // Remove from categories list and save
                var allCategories = GetAllCategoriesFlat();
                RemoveCategoryAndChildren(allCategories, categoryId);
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);

                await LoadCategoriesAsync(); // Reload after deletion
                StatusMessage = $"Deleted category: {categoryName}";
                _logger.Info($"Deleted category: {categoryName}");
            }, "Delete Category");
        }

        private void RemoveCategoryAndChildren(List<CategoryModel> allCategories, string categoryId)
        {
            // Find all children recursively
            var toRemove = new List<string> { categoryId };
            var queue = new Queue<string>();
            queue.Enqueue(categoryId);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var children = allCategories.Where(c => c.ParentId == currentId).Select(c => c.Id).ToList();
                foreach (var childId in children)
                {
                    toRemove.Add(childId);
                    queue.Enqueue(childId);
                }
            }

            // Remove all found categories
            allCategories.RemoveAll(c => toRemove.Contains(c.Id));
        }

        #endregion

        #region Search Operations

        private void FilterNotes()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // Show all
                SetAllVisible(true);
                _searchResults.Clear();
                _searchIndex = -1;
            }
            else
            {
                var searchLower = SearchText.ToLower();
                _searchResults = new List<NoteTreeItem>();
                
                foreach (var category in Categories)
                {
                    FilterCategory(category, searchLower);
                }
                
                _searchIndex = -1;
            }
        }

        private void SetAllVisible(bool visible)
        {
            foreach (var category in Categories)
            {
                SetCategoryVisibility(category, visible);
            }
        }

        private void SetCategoryVisibility(CategoryTreeItem category, bool visible)
        {
            category.IsVisible = visible;
            foreach (var note in category.Notes)
            {
                note.IsVisible = visible;
            }
            foreach (var subCategory in category.SubCategories)
            {
                SetCategoryVisibility(subCategory, visible);
            }
        }

        private bool FilterCategory(CategoryTreeItem category, string searchLower)
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
            
            foreach (var subCategory in category.SubCategories)
            {
                if (FilterCategory(subCategory, searchLower))
                {
                    hasVisibleNotes = true;
                }
            }
            
            category.IsVisible = hasVisibleNotes || category.Name.ToLower().Contains(searchLower);
            return category.IsVisible;
        }

        private void NavigateSearch(int delta)
        {
            if (_searchResults.Count == 0) return;
            
            _searchIndex = (_searchIndex + delta + _searchResults.Count) % _searchResults.Count;
            var target = _searchResults[_searchIndex];
            
            // Find parent category
            CategoryTreeItem parent = null;
            foreach (var cat in Categories)
            {
                parent = FindCategoryContainingNote(cat, target);
                if (parent != null) break;
            }
            
            if (parent != null)
            {
                parent.IsExpanded = true;
                SelectedCategory = parent;
                SelectedNote = target;
            }
        }

        private CategoryTreeItem FindCategoryContainingNote(CategoryTreeItem category, NoteTreeItem note)
        {
            if (category.Notes.Contains(note))
                return category;
            
            foreach (var subCategory in category.SubCategories)
            {
                var found = FindCategoryContainingNote(subCategory, note);
                if (found != null) return found;
            }
            
            return null;
        }

        private async Task OpenFromSearchAsync()
        {
            if (_searchResults.Count == 0) return;
            
            if (_searchIndex < 0 && _searchResults.Count > 0)
            {
                _searchIndex = 0;
                NavigateSearch(0);
            }
            
            if (SelectedNote != null)
            {
                await OpenNoteAsync(SelectedNote);
            }
        }

        #endregion

        #region Helper Methods

        private void InitializeAutoSave()
        {
            // Ensure settings are loaded before initializing auto-save
            if (_configService?.Settings == null)
            {
                _logger.Warning("Settings not available for auto-save initialization");
                return;
            }

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(_configService.Settings.AutoSaveInterval);
            _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();
            
            if (_configService.Settings.AutoSave)
            {
                _autoSaveTimer.Start();
            }
        }

        private async Task AutoSaveAsync()
        {
            try
            {
                var dirtyTabs = OpenTabs.Where(t => t.IsDirty).ToList();
                if (dirtyTabs.Any())
                {
                    foreach (var tab in dirtyTabs)
                    {
                        await _noteService.SaveNoteAsync(tab.Note);
                        tab.IsDirty = false;
                    }
                    
                    StatusMessage = $"Auto-saved {dirtyTabs.Count} note(s)";
                    _logger.Debug($"Auto-saved {dirtyTabs.Count} notes");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Auto-save failed");
            }
        }

        private async Task SafeExecuteAsync(Func<Task> operation, string operationName)
        {
            try
            {
                _logger.Debug($"Starting operation: {operationName}");
                await operation();
                _logger.Debug($"Completed operation: {operationName}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed operation: {operationName}");
                MessageBox.Show(
                    $"Operation failed: {operationName}\n\nError: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusMessage = $"Error: {operationName}";
            }
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

        #endregion

        #region Public Methods

        public async Task RenameCategoryAsync(CategoryTreeItem categoryItem, string newName)
        {
            if (categoryItem == null || string.IsNullOrWhiteSpace(newName)) return;

            await SafeExecuteAsync(async () =>
            {
                var oldName = categoryItem.Model.Name;
                categoryItem.Model.Name = newName;
                
                var allCategories = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                StatusMessage = $"Renamed category from '{oldName}' to '{newName}'";
                _logger.Info($"Renamed category from '{oldName}' to '{newName}'");
            }, "Rename Category");
        }

        public async Task ToggleCategoryPinAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;

            await SafeExecuteAsync(async () =>
            {
                categoryItem.Model.Pinned = !categoryItem.Model.Pinned;
                
                var allCategories = GetAllCategoriesFlat();
                await _noteService.SaveCategoriesAsync(_configService.Settings.MetadataPath, allCategories);
                
                await LoadCategoriesAsync();
                
                StatusMessage = categoryItem.Model.Pinned ? 
                    $"Pinned '{categoryItem.Name}'" : $"Unpinned '{categoryItem.Name}'";
                _logger.Info(StatusMessage);
            }, "Toggle Pin");
        }

        public async Task RenameNoteAsync(NoteTreeItem noteItem, string newName)
        {
            if (noteItem == null || string.IsNullOrWhiteSpace(newName)) return;

            await SafeExecuteAsync(async () =>
            {
                var oldPath = noteItem.Model.FilePath;
                var directory = Path.GetDirectoryName(oldPath);
                var newFileName = PathService.SanitizeName(newName) + ".txt";
                var newPath = Path.Combine(directory, newFileName);
                
                if (File.Exists(newPath) && newPath != oldPath)
                {
                    MessageBox.Show("A note with this name already exists.", "Name Conflict", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                }
                
                noteItem.Model.Title = newName;
                noteItem.Model.FilePath = newPath;
                
                var openTab = OpenTabs.FirstOrDefault(t => t.Note.FilePath == oldPath);
                if (openTab != null)
                {
                    openTab.Note.Title = newName;
                    openTab.Note.FilePath = newPath;
                    openTab.OnPropertyChanged(nameof(openTab.Title));
                }
                
                StatusMessage = $"Renamed note to '{newName}'";
                _logger.Info($"Renamed note from {oldPath} to {newPath}");
            }, "Rename Note");
        }

        public async Task DeleteNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;

            await SafeExecuteAsync(async () =>
            {
                var openTab = OpenTabs.FirstOrDefault(t => t.Note.FilePath == noteItem.Model.FilePath);
                if (openTab != null)
                {
                    OpenTabs.Remove(openTab);
                }
                
                await _noteService.DeleteNoteAsync(noteItem.Model);
                
                // Find the category containing this note
                CategoryTreeItem containingCategory = null;
                foreach (var cat in Categories)
                {
                    containingCategory = FindCategoryContainingNote(cat, noteItem);
                    if (containingCategory != null) break;
                }
                
                if (containingCategory != null)
                {
                    containingCategory.Notes.Remove(noteItem);
                }
                
                StatusMessage = $"Deleted '{noteItem.Title}'";
                _logger.Info($"Deleted note: {noteItem.Model.FilePath}");
            }, "Delete Note");
        }

        public ConfigurationService GetConfigService()
        {
            return _configService;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _logger.Info("Disposing MainViewModel");
                    
                    try
                    {
                        // Stop and dispose auto-save timer
                        if (_autoSaveTimer != null)
                        {
                            _autoSaveTimer.Stop();
                            _autoSaveTimer.Tick -= async (s, e) => await AutoSaveAsync();
                            _autoSaveTimer = null;
                        }
                        
                        // Stop file watcher first to prevent any new events during shutdown
                        if (_fileWatcher != null)
                        {
                            _fileWatcher.StopAllWatchers();
                            _fileWatcher.Dispose();
                        }
                        
                        // Save any unsaved changes synchronously to avoid deadlocks
                        try
                        {
                            if (OpenTabs?.Any(t => t.IsDirty) == true)
                            {
                                SaveAllNotesAsync().GetAwaiter().GetResult();
                            }
                            _configService?.SaveSettingsAsync().GetAwaiter().GetResult();
                        }
                        catch (Exception saveEx)
                        {
                            _logger.Error(saveEx, "Error saving during disposal - continuing shutdown");
                        }
                        
                        // Clear collections to help with cleanup
                        Categories?.Clear();
                        OpenTabs?.Clear();
                        
                        _logger.Info("MainViewModel disposed successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during MainViewModel disposal - forcing shutdown");
                    }
                }
                _disposed = true;
            }
        }

        #endregion
    }
}