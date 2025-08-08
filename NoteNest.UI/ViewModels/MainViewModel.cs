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
                }
            }
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
        public ICommand DeleteCategoryCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExitCommand { get; }

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
            DeleteCategoryCommand = new RelayCommand(async _ => await DeleteCategoryAsync(), _ => SelectedCategory != null);
            RefreshCommand = new RelayCommand(async _ => await LoadCategoriesAsync());
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());

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

                var categories = await _noteService.LoadCategoriesAsync(settings.MetadataPath);

                // Create sample categories if none exist
                if (!categories.Any())
                {
                    categories = new List<CategoryModel>
                    {
                        new CategoryModel
                        {
                            Id = "personal",
                            Name = "Personal",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Personal"),
                            Pinned = false,
                            Tags = new List<string> { "personal" }
                        },
                        new CategoryModel
                        {
                            Id = "work", 
                            Name = "Work",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Work"),
                            Pinned = true,
                            Tags = new List<string> { "work", "important" }
                        }
                    };
                    
                    // Create the physical directories
                    foreach (var cat in categories)
                    {
                        if (!await fileSystem.ExistsAsync(cat.Path))
                        {
                            await fileSystem.CreateDirectoryAsync(cat.Path);
                        }
                    }
                    
                    // Save the categories to metadata
                    await _noteService.SaveCategoriesAsync(settings.MetadataPath, categories);
                }

                // Load categories and their notes
                foreach (var category in categories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
                    // Ensure category directory exists
                    if (!await fileSystem.ExistsAsync(category.Path))
                    {
                        await fileSystem.CreateDirectoryAsync(category.Path);
                    }
                    
                    var categoryItem = new CategoryTreeItem(category);
                    
                    // Load existing notes for this category
                    try
                    {
                        var notes = await _noteService.GetNotesInCategoryAsync(category);
                        foreach (var note in notes)
                        {
                            categoryItem.Notes.Add(new NoteTreeItem(note));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading notes for {category.Name}: {ex.Message}");
                    }

                    Categories.Add(categoryItem);
                    
                    // Start watching this category folder
                    _fileWatcher.StartWatching(category.Path);
                }
                
                StatusMessage = $"Loaded {Categories.Count} categories";
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
                var allCategories = Categories.Select(c => c.Model).ToList();
                await _noteService.SaveCategoriesAsync(settings.MetadataPath, allCategories);

                _fileWatcher.StartWatching(category.Path);
                StatusMessage = $"Created category: {name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating category: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteCategoryAsync()
        {
            if (SelectedCategory == null) return;

            var result = MessageBox.Show(
                $"Delete category '{SelectedCategory.Name}' and all its notes?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            Categories.Remove(SelectedCategory);
            
            // Save updated categories
            var settings = _configService.Settings;
            var allCategories = Categories.Select(c => c.Model).ToList();
            await _noteService.SaveCategoriesAsync(settings.MetadataPath, allCategories);

            _fileWatcher.StopWatching(SelectedCategory.Model.Path);
            StatusMessage = $"Deleted category: {SelectedCategory.Name}";
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
            }
            else
            {
                var searchLower = SearchText.ToLower();
                foreach (var category in Categories)
                {
                    var hasVisibleNotes = false;
                    foreach (var note in category.Notes)
                    {
                        note.IsVisible = note.Model.Title.ToLower().Contains(searchLower);
                        if (note.IsVisible) hasVisibleNotes = true;
                    }
                    category.IsVisible = hasVisibleNotes || category.Name.ToLower().Contains(searchLower);
                }
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
                var allCategories = Categories.Select(c => c.Model).ToList();
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
                var allCategories = Categories.Select(c => c.Model).ToList();
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
