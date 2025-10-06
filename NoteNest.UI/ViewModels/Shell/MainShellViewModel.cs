using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MediatR;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.Services;
using System.Linq;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Controls.Editor.RTF;

namespace NoteNest.UI.ViewModels.Shell
{
    public class MainShellViewModel : ViewModelBase, IDisposable
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IAppLogger _logger;
        private bool _isLoading;
        private string _statusMessage;
        
        // Status bar enhancements
        private bool _showSaveIndicator;
        private int _wordCount;
        private int _characterCount;
        private DispatcherTimer _saveIndicatorTimer;
        private DispatcherTimer _statsUpdateTimer;
        private TabViewModel _currentTab;

        public MainShellViewModel(
            IMediator mediator,
            IDialogService dialogService,
            IAppLogger logger,
            CategoryTreeViewModel categoryTree,
            NoteOperationsViewModel noteOperations,
            CategoryOperationsViewModel categoryOperations,
            WorkspaceViewModel workspace,
            SearchViewModel search)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            _logger = logger;
            
            // Composed ViewModels - each with single responsibility
            CategoryTree = categoryTree;
            NoteOperations = noteOperations;
            CategoryOperations = categoryOperations;
            Workspace = workspace;
            Search = search;
            
            InitializeCommands();
            InitializeTimers();
            SubscribeToEvents();
        }

        // Focused ViewModels - Clean Architecture approach
        public CategoryTreeViewModel CategoryTree { get; }
        public NoteOperationsViewModel NoteOperations { get; }
        public CategoryOperationsViewModel CategoryOperations { get; }
        public WorkspaceViewModel Workspace { get; }
        public SearchViewModel Search { get; }

        // Expose selected category from CategoryTree
        public CategoryViewModel SelectedCategory => CategoryTree.SelectedCategory;

        // Additional aggregated properties for convenience
        public bool HasOpenTabs => Workspace.OpenTabs.Any();

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
        
        // Status bar enhancements - save indicator and document stats
        public bool ShowSaveIndicator
        {
            get => _showSaveIndicator;
            set => SetProperty(ref _showSaveIndicator, value);
        }
        
        public int WordCount
        {
            get => _wordCount;
            set => SetProperty(ref _wordCount, value);
        }
        
        public int CharacterCount
        {
            get => _characterCount;
            set => SetProperty(ref _characterCount, value);
        }

        // Commands are now delegated to focused ViewModels
        public ICommand CreateNoteCommand => NoteOperations.CreateNoteCommand;
        public ICommand SaveNoteCommand => Workspace.SaveTabCommand;
        public ICommand SaveAllCommand => Workspace.SaveAllTabsCommand;
        public ICommand RefreshCommand { get; private set; }
        
        // Selection-based commands
        public ICommand DeleteSelectedCommand { get; private set; }
        public ICommand RenameSelectedCommand { get; private set; }

        private void InitializeCommands()
        {
            RefreshCommand = new AsyncRelayCommand(ExecuteRefresh);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            RenameSelectedCommand = new AsyncRelayCommand(RenameSelectedAsync);
        }
        
        private void InitializeTimers()
        {
            // Save indicator timer - shows for 3 seconds after save
            _saveIndicatorTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromSeconds(3),
                IsEnabled = false
            };
            _saveIndicatorTimer.Tick += (s, e) =>
            {
                _saveIndicatorTimer.Stop();
                ShowSaveIndicator = false;
            };
            
            // Stats update timer - debounces document stats calculation (500ms)
            _statsUpdateTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(500),
                IsEnabled = false
            };
            _statsUpdateTimer.Tick += (s, e) =>
            {
                _statsUpdateTimer.Stop();
                UpdateDocumentStats();
            };
        }

        private async Task ExecuteRefresh()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Refreshing...";
                
                await CategoryTree.RefreshAsync();
                
                StatusMessage = "Refreshed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task DeleteSelectedAsync()
        {
            if (CategoryTree.SelectedNote != null)
            {
                // Delete the selected note using the command
                if (NoteOperations.DeleteNoteCommand.CanExecute(CategoryTree.SelectedNote))
                {
                    NoteOperations.DeleteNoteCommand.Execute(CategoryTree.SelectedNote);
                }
            }
            else if (CategoryTree.SelectedCategory != null && !CategoryTree.SelectedCategory.IsRoot)
            {
                // Delete the selected category using the command
                if (CategoryOperations.DeleteCategoryCommand.CanExecute(CategoryTree.SelectedCategory))
                {
                    CategoryOperations.DeleteCategoryCommand.Execute(CategoryTree.SelectedCategory);
                }
            }
            await Task.CompletedTask; // Keep method async for consistency
        }
        
        private async Task RenameSelectedAsync()
        {
            if (CategoryTree.SelectedNote != null)
            {
                // Rename the selected note using the command
                if (NoteOperations.RenameNoteCommand.CanExecute(CategoryTree.SelectedNote))
                {
                    NoteOperations.RenameNoteCommand.Execute(CategoryTree.SelectedNote);
                }
            }
            else if (CategoryTree.SelectedCategory != null && !CategoryTree.SelectedCategory.IsRoot)
            {
                // Rename the selected category using the command
                if (CategoryOperations.RenameCategoryCommand.CanExecute(CategoryTree.SelectedCategory))
                {
                    CategoryOperations.RenameCategoryCommand.Execute(CategoryTree.SelectedCategory);
                }
            }
            await Task.CompletedTask; // Keep method async for consistency
        }
        
        private void UpdateDocumentStats()
        {
            var tab = Workspace?.SelectedTab;
            if (tab == null)
            {
                WordCount = 0;
                CharacterCount = 0;
                return;
            }
            
            try
            {
                // Get RTF content from the tab
                var rtfContent = tab.GetContentToLoad();
                if (string.IsNullOrEmpty(rtfContent))
                {
                    WordCount = 0;
                    CharacterCount = 0;
                    return;
                }
                
                // Extract plain text for stats calculation
                var plainText = RTFOperations.ExtractPlainText(rtfContent);
                
                // Calculate word count
                var words = plainText.Split(new[] { ' ', '\n', '\r', '\t' }, 
                                           StringSplitOptions.RemoveEmptyEntries);
                WordCount = words.Length;
                
                // Calculate character count
                CharacterCount = plainText.Length;
                
                _logger.Debug($"Document stats updated: {WordCount} words, {CharacterCount} chars");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update document stats");
                WordCount = 0;
                CharacterCount = 0;
            }
        }

        private void SubscribeToEvents()
        {
            // Wire up cross-ViewModel communication
            CategoryTree.CategorySelected += OnCategorySelected;
            CategoryTree.NoteSelected += OnNoteSelected;
            CategoryTree.NoteOpenRequested += OnNoteOpenRequested;
            
            NoteOperations.NoteCreated += OnNoteCreated;
            NoteOperations.NoteDeleted += OnNoteDeleted;
            NoteOperations.NoteRenamed += OnNoteRenamed;
            
            CategoryOperations.CategoryCreated += OnCategoryCreated;
            CategoryOperations.CategoryDeleted += OnCategoryDeleted;
            CategoryOperations.CategoryRenamed += OnCategoryRenamed;
            
            Workspace.TabSelected += OnTabSelected;
            Workspace.NoteOpened += OnNoteOpened;
            Workspace.NoteSaved += OnNoteSaved;
            
            // Wire up search events
            Search.ResultSelected += OnSearchResultSelected; // RESTORED FOR DIAGNOSTIC TESTING
            Search.NoteOpenRequested += OnSearchNoteOpenRequested;
        }
        
        private void OnNoteSaved(string noteId, bool wasAutoSave)
        {
            // Show save indicator for 3 seconds
            ShowSaveIndicator = true;
            _saveIndicatorTimer?.Stop();
            _saveIndicatorTimer?.Start();
            
            _logger.Debug($"Save indicator triggered for note: {noteId}, AutoSave={wasAutoSave}");
        }

        private void OnCategorySelected(CategoryViewModel category)
        {
            // Update NoteOperations with selected category
            NoteOperations.SelectedCategoryId = category?.Id;
            
            // Update status
            StatusMessage = category != null ? $"Selected: {category.Name}" : "No category selected";
        }

        private async void OnNoteCreated(string noteId)
        {
            // TODO: Implement proper note lookup to get full Note object for opening
            // For now, just refresh the tree to show the new note
            await CategoryTree.RefreshAsync();
            StatusMessage = "Note created - refresh tree to see it";
            
            // Note: Opening newly created notes requires note lookup to get full Note object
            // This will be implemented when note creation flow is enhanced
        }

        private async void OnNoteDeleted(string noteId)
        {
            // Close tab if note was open
            var openTab = Workspace.OpenTabs.FirstOrDefault(t => t.NoteId == noteId);
            if (openTab != null)
            {
                openTab.Dispose(); // Properly dispose RTF editor
                Workspace.OpenTabs.Remove(openTab);
            }
            
            // Refresh tree to reflect deletion
            await CategoryTree.RefreshAsync();
            StatusMessage = "Note deleted";
        }
        
        private async void OnNoteRenamed(string noteId, string newTitle)
        {
            // Update tab title if note is open
            var openTab = Workspace.OpenTabs.FirstOrDefault(t => t.NoteId == noteId);
            if (openTab != null)
            {
                // Update the underlying NoteModel title (triggers INotifyPropertyChanged automatically)
                openTab.Note.Title = newTitle;
                
                _logger.Info($"Updated open tab title to: {newTitle}");
            }
            
            // Refresh tree to reflect renamed note
            await CategoryTree.RefreshAsync();
            StatusMessage = $"Note renamed to: {newTitle}";
        }
        
        private async void OnCategoryCreated(string categoryPath)
        {
            // Refresh tree to show new category
            await CategoryTree.RefreshAsync();
            StatusMessage = "Category created";
        }
        
        private async void OnCategoryDeleted(string categoryId)
        {
            // Refresh tree to reflect deletion
            await CategoryTree.RefreshAsync();
            StatusMessage = "Category deleted";
        }
        
        private async void OnCategoryRenamed(string categoryId, string newName)
        {
            // Refresh tree to reflect renamed category
            await CategoryTree.RefreshAsync();
            StatusMessage = $"Category renamed to: {newName}";
        }

        private void OnTabSelected(TabViewModel tab)
        {
            // Unsubscribe from old tab's PropertyChanged
            if (_currentTab != null)
            {
                _currentTab.PropertyChanged -= OnCurrentTabPropertyChanged;
            }
            
            // Update reference and subscribe to new tab
            _currentTab = tab;
            if (_currentTab != null)
            {
                _currentTab.PropertyChanged += OnCurrentTabPropertyChanged;
            }
            
            StatusMessage = tab != null ? $"Editing: {tab.Title}" : "No note selected";
            
            // Update document stats immediately when tab changes
            UpdateDocumentStats();
        }
        
        private void OnCurrentTabPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // When IsDirty changes, debounce stats update (user is typing)
            if (e.PropertyName == nameof(TabViewModel.IsDirty))
            {
                _statsUpdateTimer?.Stop();
                _statsUpdateTimer?.Start();
            }
        }

        private void OnNoteOpened(string noteId)
        {
            StatusMessage = "Note opened in editor";
        }
        
        private async void OnSearchNoteOpenRequested(object sender, string filePath)
        {
            try
            {
                IsLoading = true;
                StatusMessage = $"Opening note from search...";
                
                // Load note from file path
                // For now, create a minimal domain Note object with file path
                var note = new NoteNest.Domain.Notes.Note(
                    NoteNest.Domain.Categories.CategoryId.Create(), // Temporary category
                    System.IO.Path.GetFileNameWithoutExtension(filePath)
                );
                // Set file path using reflection or constructor
                note.GetType().GetProperty("FilePath")?.SetValue(note, filePath);
                
                // Open in workspace
                await Workspace.OpenNoteAsync(note);
                
                StatusMessage = "Note opened from search";
                _logger.Info($"Opened note from search: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to open note from search: {filePath}");
                StatusMessage = "Failed to open note";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // RESTORED FOR DIAGNOSTIC TESTING
        private async void OnSearchResultSelected(object sender, SearchResultSelectedEventArgs e)
        {
            var searchResult = e?.Result;
            if (searchResult == null) return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Opening search result: {searchResult.Title}...";

                // Use existing SearchResultViewModel.FilePath (your working search system)
                if (!System.IO.File.Exists(searchResult.FilePath))
                {
                    StatusMessage = $"File not found: {searchResult.FilePath}";
                    return;
                }

                // Create Note domain object from search result
                var categoryPath = System.IO.Path.GetDirectoryName(searchResult.FilePath);
                var categoryId = NoteNest.Domain.Categories.CategoryId.From(categoryPath ?? searchResult.CategoryId);
                var note = new NoteNest.Domain.Notes.Note(categoryId, searchResult.Title);
                note.SetFilePath(searchResult.FilePath);

                // Load content from RTF file
                var content = await System.IO.File.ReadAllTextAsync(searchResult.FilePath);
                note.UpdateContent(content);

                // Open in workspace with RTF editor
                await Workspace.OpenNoteAsync(note);
                
                StatusMessage = $"Opened search result: {searchResult.Title}";
                _logger.Info($"🔍 Search result opened: {searchResult.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open search result: {ex.Message}";
                _dialogService.ShowError($"Failed to open search result: {ex.Message}", "Search Error");
                _logger.Error(ex, $"Failed to open search result: {searchResult?.Title}");
            }
            finally
            {
                IsLoading = false;
            }
        }
        // END RESTORED METHOD

        // =============================================================================
        // NEW NOTE INTERACTION HANDLERS - Clean Architecture Event Orchestration
        // =============================================================================

        private void OnNoteSelected(NoteItemViewModel note)
        {
            if (note != null)
            {
                StatusMessage = $"Selected note: {note.Title}";
                // Could add preview pane logic here in the future
            }
        }

        private async void OnNoteOpenRequested(NoteItemViewModel note)
        {
            if (note == null) return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Opening {note.Title}...";

                // 🎯 CRITICAL FIX: Pass the entire Note object with FilePath information
                // This allows the workspace to access the file path for RTF content loading
                await Workspace.OpenNoteAsync(note.Note);

                StatusMessage = $"Opened {note.Title}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open {note.Title}: {ex.Message}";
                _dialogService.ShowError($"Failed to open note: {ex.Message}", "Open Error");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // =============================================================================
        // RESOURCE CLEANUP - Prevent Memory Leaks
        // =============================================================================
        
        public void Dispose()
        {
            try
            {
                // Clean up timers
                if (_saveIndicatorTimer != null)
                {
                    _saveIndicatorTimer.Stop();
                    _saveIndicatorTimer = null;
                }
                
                if (_statsUpdateTimer != null)
                {
                    _statsUpdateTimer.Stop();
                    _statsUpdateTimer = null;
                }
                
                // Unsubscribe from current tab
                if (_currentTab != null)
                {
                    _currentTab.PropertyChanged -= OnCurrentTabPropertyChanged;
                    _currentTab = null;
                }
                
                // Dispose ViewModels that implement IDisposable
                (Search as IDisposable)?.Dispose();
                (Workspace as IDisposable)?.Dispose();
                
                _logger?.Debug("MainShellViewModel disposed successfully");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error disposing MainShellViewModel");
            }
        }
    }
}
