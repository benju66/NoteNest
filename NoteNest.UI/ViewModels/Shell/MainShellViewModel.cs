using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using System.Linq;

namespace NoteNest.UI.ViewModels.Shell
{
    public class MainShellViewModel : ViewModelBase, IDisposable
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IAppLogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private bool _isLoading;
        private string _statusMessage;
        
        // Status bar enhancements
        private bool _showSaveIndicator;
        private DispatcherTimer _saveIndicatorTimer;
        
        // Right panel and activity bar
        private bool _isRightPanelVisible;
        private double _rightPanelWidth = 400; // Increased default width for better todo text visibility
        private string _activePluginTitle = string.Empty;
        private object? _activePluginContent;
        private ObservableCollection<ActivityBarItemViewModel> _activityBarItems;

        public MainShellViewModel(
            IMediator mediator,
            IDialogService dialogService,
            IAppLogger logger,
            IServiceProvider serviceProvider,
            CategoryTreeViewModel categoryTree,
            NoteOperationsViewModel noteOperations,
            CategoryOperationsViewModel categoryOperations,
            WorkspaceViewModel workspace,
            SearchViewModel search)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            _logger = logger;
            _serviceProvider = serviceProvider;
            
            // Composed ViewModels - each with single responsibility
            CategoryTree = categoryTree;
            NoteOperations = noteOperations;
            CategoryOperations = categoryOperations;
            Workspace = workspace;
            Search = search;
            
            // Initialize activity bar items
            _activityBarItems = new ObservableCollection<ActivityBarItemViewModel>();
            
            InitializeCommands();
            InitializeTimers();
            InitializePlugins();
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
        
        // Status bar enhancements - save indicator
        public bool ShowSaveIndicator
        {
            get => _showSaveIndicator;
            set => SetProperty(ref _showSaveIndicator, value);
        }
        
        // Right panel properties
        public bool IsRightPanelVisible
        {
            get => _isRightPanelVisible;
            set => SetProperty(ref _isRightPanelVisible, value);
        }
        
        public double RightPanelWidth
        {
            get => _rightPanelWidth;
            set => SetProperty(ref _rightPanelWidth, value);
        }
        
        public string ActivePluginTitle
        {
            get => _activePluginTitle;
            set => SetProperty(ref _activePluginTitle, value);
        }
        
        public object? ActivePluginContent
        {
            get => _activePluginContent;
            set => SetProperty(ref _activePluginContent, value);
        }
        
        public ObservableCollection<ActivityBarItemViewModel> ActivityBarItems
        {
            get => _activityBarItems;
            set => SetProperty(ref _activityBarItems, value);
        }

        // Commands are now delegated to focused ViewModels
        public ICommand CreateNoteCommand => NoteOperations.CreateNoteCommand;
        public ICommand SaveNoteCommand => Workspace.SaveTabCommand;
        public ICommand SaveAllCommand => Workspace.SaveAllTabsCommand;
        public ICommand RefreshCommand { get; private set; }
        
        // Selection-based commands
        public ICommand DeleteSelectedCommand { get; private set; }
        public ICommand RenameSelectedCommand { get; private set; }
        
        // Right panel commands
        public ICommand ToggleRightPanelCommand { get; private set; }

        private void InitializeCommands()
        {
            RefreshCommand = new AsyncRelayCommand(ExecuteRefresh);
            DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            RenameSelectedCommand = new AsyncRelayCommand(RenameSelectedAsync);
            ToggleRightPanelCommand = new RelayCommand(ExecuteToggleRightPanel);
        }
        
        private void ExecuteToggleRightPanel()
        {
            _logger.Info($"⌨️ Ctrl+B pressed - IsRightPanelVisible: {IsRightPanelVisible}");
            
            if (!IsRightPanelVisible)
            {
                // Opening panel - activate Todo plugin if nothing is active
                if (ActivePluginContent == null)
                {
                    _logger.Info("⌨️ No plugin active, activating Todo plugin...");
                    ActivateTodoPlugin();
                }
                else
                {
                    // Just show the panel with whatever was active
                    IsRightPanelVisible = true;
                    _logger.Info("⌨️ Showing existing plugin content");
                }
            }
            else
            {
                // Closing panel
                IsRightPanelVisible = false;
                _logger.Info("⌨️ Hiding right panel");
            }
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
        }
        
        private void InitializePlugins()
        {
            try
            {
                _logger.Info("🔌 InitializePlugins() called");
                _logger.Info($"🔌 ServiceProvider is null: {_serviceProvider == null}");
                
                // Initialize Todo plugin database
                _ = InitializeTodoPluginAsync();
                
                // Get the Todo plugin
                var todoPlugin = _serviceProvider?.GetService<TodoPlugin>();
                _logger.Info($"🔌 TodoPlugin retrieved: {todoPlugin != null}");
                
                if (todoPlugin != null)
                {
                    // Create activity bar item for Todo plugin
                    var todoCommand = new RelayCommand(() => ActivateTodoPlugin());
                    var todoItem = new ActivityBarItemViewModel(
                        todoPlugin.Id,
                        todoPlugin.Name,
                        System.Windows.Application.Current.FindResource("LucideCheck"),
                        todoCommand);
                    
                    ActivityBarItems.Add(todoItem);
                    _logger.Info("✅ Todo plugin registered in activity bar");
                    _logger.Info($"✅ ActivityBarItems count: {ActivityBarItems.Count}");
                }
                else
                {
                    _logger.Warning("⚠️ TodoPlugin is null - not registered in DI container?");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to initialize plugins");
            }
        }
        
        private async Task InitializeTodoPluginAsync()
        {
            try
            {
                _logger.Info("[TodoPlugin] Initializing database...");
                
                // CRITICAL: Register Dapper type handlers for Guid conversion (fixes persistence bug)
                Dapper.SqlMapper.AddTypeHandler(new NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.GuidTypeHandler());
                Dapper.SqlMapper.AddTypeHandler(new NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.NullableGuidTypeHandler());
                _logger.Info("[TodoPlugin] Registered Dapper type handlers for TEXT -> Guid conversion");
                
                // Initialize database schema
                var dbInitializer = _serviceProvider?.GetService<NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.ITodoDatabaseInitializer>();
                if (dbInitializer != null)
                {
                    var dbInitialized = await dbInitializer.InitializeAsync();
                    if (!dbInitialized)
                    {
                        _logger.Warning("[TodoPlugin] Database initialization had errors, but continuing with CategoryStore/TodoStore initialization...");
                        // IMPORTANT: Don't return early - CategoryStore and TodoStore should still initialize
                        // This ensures user data (categories/todos) can still load even if migrations have issues
                    }
                    else
                    {
                        _logger.Info("[TodoPlugin] Database initialized successfully");
                    }
                }
                
                // NEW: Initialize CategoryStore (load categories from tree)
                var categoryStore = _serviceProvider?.GetService<NoteNest.UI.Plugins.TodoPlugin.Services.ICategoryStore>();
                if (categoryStore != null)
                {
                    await categoryStore.InitializeAsync();
                    _logger.Info("[TodoPlugin] CategoryStore initialized from tree");
                    
                    // Run cleanup to handle any orphaned categories from previous sessions
                    var cleanupService = _serviceProvider?.GetService<NoteNest.UI.Plugins.TodoPlugin.Services.ICategoryCleanupService>();
                    if (cleanupService != null)
                    {
                        var cleanedCount = await cleanupService.CleanupOrphanedCategoriesAsync();
                        if (cleanedCount > 0)
                        {
                            _logger.Info($"[TodoPlugin] Cleaned up {cleanedCount} todos from orphaned categories");
                        }
                    }
                }
                
                // Initialize TodoStore (load todos from database)
                var todoStore = _serviceProvider?.GetService<NoteNest.UI.Plugins.TodoPlugin.Services.ITodoStore>();
                if (todoStore is NoteNest.UI.Plugins.TodoPlugin.Services.TodoStore store)
                {
                    await store.InitializeAsync();
                    _logger.Info("[TodoPlugin] TodoStore initialized successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoPlugin] Failed to initialize database/store");
            }
        }
        
        private void ActivateTodoPlugin()
        {
            try
            {
                _logger.Info("🎯 ActivateTodoPlugin() called - User clicked activity bar button");
                
                var todoPlugin = _serviceProvider?.GetService<TodoPlugin>();
                _logger.Info($"🎯 TodoPlugin retrieved in Activate: {todoPlugin != null}");
                
                if (todoPlugin != null)
                {
                    // Deactivate all other activity bar items
                    foreach (var item in ActivityBarItems)
                    {
                        item.IsActive = false;
                    }
                    
                    // Activate this item
                    var todoItem = ActivityBarItems.FirstOrDefault(i => i.Id == todoPlugin.Id);
                    if (todoItem != null)
                    {
                        todoItem.IsActive = true;
                        _logger.Info($"🎯 Activity bar item activated: {todoItem.Id}");
                    }
                    
                    // Show the Todo panel
                    _logger.Info($"🎯 Setting ActivePluginTitle to: {todoPlugin.Name}");
                    ActivePluginTitle = todoPlugin.Name;
                    
                    _logger.Info("🎯 Creating panel via CreatePanel()...");
                    var panel = todoPlugin.CreatePanel();
                    _logger.Info($"🎯 Panel created: {panel != null}, Type: {panel?.GetType().Name}");
                    
                    ActivePluginContent = panel;
                    _logger.Info($"🎯 ActivePluginContent set: {ActivePluginContent != null}");
                    
                    _logger.Info($"🎯 Setting IsRightPanelVisible = true (current: {IsRightPanelVisible})");
                    IsRightPanelVisible = true;
                    _logger.Info($"🎯 IsRightPanelVisible is now: {IsRightPanelVisible}");
                    
                    _logger.Info("✅ Todo plugin activated successfully");
                }
                else
                {
                    _logger.Warning("⚠️ TodoPlugin is null in ActivateTodoPlugin()");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to activate Todo plugin");
            }
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
            CategoryOperations.CategoryMoved += OnCategoryMoved;
            CategoryOperations.NoteMoved += OnNoteMoved;
            
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
            
            // NOTE: Do NOT refresh TodoPlugin categories - they're manually selected by user
            // Auto-refresh would replace user's selections with all categories from tree
        }
        
        private async void OnCategoryDeleted(string categoryId)
        {
            // Refresh tree to reflect deletion
            await CategoryTree.RefreshAsync();
            StatusMessage = "Category deleted";
            
            // NOTE: Do NOT refresh TodoPlugin categories - manual selection mode
        }
        
        private async void OnCategoryRenamed(string categoryId, string newName)
        {
            // Refresh tree to reflect renamed category
            await CategoryTree.RefreshAsync();
            StatusMessage = $"Category renamed to: {newName}";
            
            // TODO: Update category name in TodoPlugin if user has it selected
            // For now: User can re-add if they want the new name
        }
        
        private async void OnCategoryMoved(string categoryId, string oldParentId, string newParentId)
        {
            // Use incremental update - no full refresh, no flickering!
            await CategoryTree.MoveCategoryInTreeAsync(categoryId, oldParentId, newParentId);
            
            var targetName = string.IsNullOrEmpty(newParentId) ? "root" : "new parent";
            StatusMessage = $"Category moved to: {targetName}";
        }
        
        private async void OnNoteMoved(string noteId, string sourceCategoryId, string targetCategoryId)
        {
            // Use incremental update - no full refresh, no flickering!
            await CategoryTree.MoveNoteInTreeAsync(noteId, sourceCategoryId, targetCategoryId);
            StatusMessage = "Note moved successfully";
        }

        private void OnTabSelected(TabViewModel tab)
        {
            StatusMessage = tab != null ? $"Editing: {tab.Title}" : "No note selected";
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
        
        private bool _disposed = false;
        
        public void Dispose()
        {
            if (_disposed) return; // Prevent double-disposal
            
            try
            {
                // Unsubscribe from CategoryTree events
                if (CategoryTree != null)
                {
                    CategoryTree.CategorySelected -= OnCategorySelected;
                    CategoryTree.NoteSelected -= OnNoteSelected;
                    CategoryTree.NoteOpenRequested -= OnNoteOpenRequested;
                }
                
                // Unsubscribe from NoteOperations events
                if (NoteOperations != null)
                {
                    NoteOperations.NoteCreated -= OnNoteCreated;
                    NoteOperations.NoteDeleted -= OnNoteDeleted;
                    NoteOperations.NoteRenamed -= OnNoteRenamed;
                }
                
                // Unsubscribe from CategoryOperations events
                if (CategoryOperations != null)
                {
                    CategoryOperations.CategoryCreated -= OnCategoryCreated;
                    CategoryOperations.CategoryDeleted -= OnCategoryDeleted;
                    CategoryOperations.CategoryRenamed -= OnCategoryRenamed;
                    CategoryOperations.CategoryMoved -= OnCategoryMoved;
                    CategoryOperations.NoteMoved -= OnNoteMoved;
                }
                
                // Unsubscribe from Workspace events
                if (Workspace != null)
                {
                    Workspace.TabSelected -= OnTabSelected;
                    Workspace.NoteOpened -= OnNoteOpened;
                }
                
                // Unsubscribe from Search events
                if (Search != null)
                {
                    Search.ResultSelected -= OnSearchResultSelected;
                    Search.NoteOpenRequested -= OnSearchNoteOpenRequested;
                }
                
                // Clean up save indicator timer
                if (_saveIndicatorTimer != null)
                {
                    _saveIndicatorTimer.Stop();
                    _saveIndicatorTimer = null;
                }
                
                // Dispose child ViewModels that implement IDisposable
                (Search as IDisposable)?.Dispose();
                (Workspace as IDisposable)?.Dispose();
                
                _disposed = true;
                _logger?.Debug("MainShellViewModel disposed successfully - all events unsubscribed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error disposing MainShellViewModel");
            }
        }
    }
}
