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
                var categories = await _noteService.LoadCategoriesAsync(settings.MetadataPath);

                // Create sample categories if none exist
                if (!categories.Any())
                {
                    categories = new List<CategoryModel>
                    {
                        new CategoryModel
                        {
                            Name = "Personal",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Personal"),
                            Pinned = false
                        },
                        new CategoryModel
                        {
                            Name = "Work",
                            Path = System.IO.Path.Combine(settings.DefaultNotePath, "Work"),
                            Pinned = true
                        }
                    };
                    
                    // Ensure category directories exist
                    var fileSystem = new DefaultFileSystemProvider();
                    foreach (var category in categories)
                    {
                        if (!await fileSystem.ExistsAsync(category.Path))
                        {
                            await fileSystem.CreateDirectoryAsync(category.Path);
                        }
                    }
                    
                    await _noteService.SaveCategoriesAsync(settings.MetadataPath, categories);
                }

                foreach (var category in categories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
                    // Ensure category directory exists before loading notes
                    var fileSystem = new DefaultFileSystemProvider();
                    if (!await fileSystem.ExistsAsync(category.Path))
                    {
                        await fileSystem.CreateDirectoryAsync(category.Path);
                    }
                    
                    var categoryItem = new CategoryTreeItem(category);
                    
                    // Load notes for this category
                    var notes = await _noteService.GetNotesInCategoryAsync(category);
                    foreach (var note in notes)
                    {
                        categoryItem.Notes.Add(new NoteTreeItem(note));
                    }

                    Categories.Add(categoryItem);
                    
                    // Start watching this category folder
                    _fileWatcher.StartWatching(category.Path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CreateNewNoteAsync()
        {
            if (SelectedCategory == null) return;

            var title = "New Note " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            var note = await _noteService.CreateNoteAsync(SelectedCategory.Model, title, string.Empty);
            
            var noteItem = new NoteTreeItem(note);
            SelectedCategory.Notes.Add(noteItem);
            
            await OpenNoteAsync(noteItem);
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
            if (SelectedTab == null || !SelectedTab.IsDirty) return;

            await _noteService.SaveNoteAsync(SelectedTab.Note);
            SelectedTab.IsDirty = false;
            StatusMessage = $"Saved: {SelectedTab.Note.Title}";
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
            // Simple input for now - replace with proper dialog later
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter category name:", 
                "New Category", 
                "");

            if (string.IsNullOrWhiteSpace(name)) return;

            var settings = _configService.Settings;
            var category = new CategoryModel
            {
                Name = name,
                Path = System.IO.Path.Combine(settings.DefaultNotePath, name.Replace(" ", "_"))
            };

            // Ensure category directory exists
            var fileSystem = new DefaultFileSystemProvider();
            if (!await fileSystem.ExistsAsync(category.Path))
            {
                await fileSystem.CreateDirectoryAsync(category.Path);
            }

            var categoryItem = new CategoryTreeItem(category);
            Categories.Add(categoryItem);

            // Save updated categories
            var allCategories = Categories.Select(c => c.Model).ToList();
            await _noteService.SaveCategoriesAsync(settings.MetadataPath, allCategories);

            _fileWatcher.StartWatching(category.Path);
            StatusMessage = $"Created category: {name}";
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
    }
}
