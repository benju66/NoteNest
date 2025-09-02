using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Commands;
using System.IO;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Interfaces;

namespace NoteNest.UI.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        // DI Services (fast, essential)
        private readonly IAppLogger _logger;
        private readonly ConfigurationService _configService;
        private readonly NoteService _noteService;
        private readonly IStateManager _stateManager;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IDialogService _dialogService;
        private readonly IFileSystemProvider _fileSystem;
        private readonly ContentCache _contentCache;
        private readonly IWorkspaceStateService _workspaceStateService;
        private readonly ITabPersistenceService _tabPersistence;

        // Lazy Services (created only when needed)
        private SearchIndexService _searchIndex;
        private FileWatcherService _fileWatcher;
        private ICategoryManagementService _categoryService;
        private INoteOperationsService _noteOperationsService;
        private readonly IWorkspaceService _workspaceService;
        private WorkspaceViewModel _workspaceViewModel;
        private DispatcherTimer _autoSaveTimer;
        private readonly NoteNest.Core.Services.NotePinService _notePinService;
        private bool _disposed;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _initializationTask;
        
        private ObservableCollection<CategoryTreeItem> _categories;
        private ObservableCollection<CategoryTreeItem> _pinnedCategories;
        private ObservableCollection<PinnedNoteItem> _pinnedNotes;
        private CategoryTreeItem _selectedCategory;
        private NoteTreeItem _selectedNote;
        private string _searchText;
        private bool _isSearchActive;
        private List<NoteTreeItem> _searchResults = new List<NoteTreeItem>();
        private int _searchSelectionIndex = -1;
        private bool _isLoading;
        private string _statusMessage;
        private List<string> _recentSearches = new List<string>();

        #region Properties

        public ObservableCollection<CategoryTreeItem> Categories
        {
            get => _categories ??= new ObservableCollection<CategoryTreeItem>();
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<CategoryTreeItem> PinnedCategories
        {
            get => _pinnedCategories ??= new ObservableCollection<CategoryTreeItem>();
            set => SetProperty(ref _pinnedCategories, value);
        }

        public ObservableCollection<PinnedNoteItem> PinnedNotes
        {
            get => _pinnedNotes ??= new ObservableCollection<PinnedNoteItem>();
            set => SetProperty(ref _pinnedNotes, value);
        }

        // Raised to request opening a note in the active split pane (handled by NoteNestPanel)
        public event Action<NoteTreeItem> NoteOpenRequested;

        private void OnServiceTabSelectionChanged(object sender, TabChangedEventArgs e)
        {
            // Update ViewModel selection from service event
            if (e?.NewTab is NoteTabItem noteTab)
            {
                SelectedTab = noteTab;
                CommandManager.InvalidateRequerySuggested();
            }
            else if (e?.NewTab != null && e.NewTab.Note != null)
            {
                var match = OpenTabs.FirstOrDefault(t =>
                    t.Note?.FilePath?.Equals(e.NewTab.Note.FilePath, StringComparison.OrdinalIgnoreCase) == true);
                if (match != null)
                {
                    SelectedTab = match;
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<NoteTabItem> OpenTabs => GetWorkspaceViewModel().OpenTabs;

        public NoteTabItem SelectedTab
        {
            get => GetWorkspaceViewModel().SelectedTab;
            set => GetWorkspaceViewModel().SelectedTab = value;
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
            get => _stateManager.IsLoading;
            set => _stateManager.IsLoading = value;
        }

        public string StatusMessage
        {
            get => _stateManager.StatusMessage;
            set => _stateManager.StatusMessage = value;
        }

        #endregion

        // Lazy service creation methods (called only when needed)
        private SearchIndexService GetSearchIndex()
        {
            return _searchIndex ??= new SearchIndexService(_configService?.Settings?.SearchIndexContentWordLimit ?? 500);
        }

        private FileWatcherService GetFileWatcher()
        {
            return _fileWatcher ??= new FileWatcherService(_logger, _configService);
        }

        private ICategoryManagementService GetCategoryService()
        {
            return _categoryService ??= new CategoryManagementService(
                _noteService,
                _configService,
                _errorHandler,
                _logger,
                _fileSystem);
        }

        private INoteOperationsService GetNoteOperationsService()
        {
            return _noteOperationsService ??= new NoteOperationsService(
                _noteService,
                _errorHandler,
                _logger,
                _fileSystem,
                _configService,
                _contentCache);
        }

        private IWorkspaceService GetWorkspaceService() => _workspaceService;

        private WorkspaceViewModel GetWorkspaceViewModel()
        {
            return _workspaceViewModel ??= new WorkspaceViewModel(GetWorkspaceService());
        }

        #region Commands

        public ICommand NewNoteCommand { get; private set; }
        // Removed: OpenNoteCommand in favor of split-pane exclusive flow
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

        // PERFORMANCE-OPTIMIZED DI Constructor
        public MainViewModel(
            IAppLogger logger,
            ConfigurationService configService,
            NoteService noteService,
            IStateManager stateManager,
            IServiceErrorHandler errorHandler,
            IDialogService dialogService,
            IFileSystemProvider fileSystem,
            IWorkspaceService workspaceService,
            ContentCache contentCache,
            IWorkspaceStateService workspaceStateService,
            ITabPersistenceService tabPersistence,
            NoteNest.Core.Services.NotePinService notePinService)
        {
            // Assign essential services only (fast)
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _workspaceStateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
            _tabPersistence = tabPersistence ?? throw new ArgumentNullException(nameof(tabPersistence));
            _notePinService = notePinService ?? throw new ArgumentNullException(nameof(notePinService));

            _logger.Info("MainViewModel fast startup initiated");
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Wire up state management (fast)
                _stateManager.PropertyChanged += OnStateManagerPropertyChanged;

                // Initialize collections (fast)
                Categories = new ObservableCollection<CategoryTreeItem>();
                PinnedCategories = new ObservableCollection<CategoryTreeItem>();
                PinnedNotes = new ObservableCollection<PinnedNoteItem>();
                
                // Initialize commands (fast)
                InitializeCommands();

                // Service-driven selection sync
                _workspaceService.TabSelectionChanged += OnServiceTabSelectionChanged;

                // Start async initialization (doesn't block startup)
                _initializationTask = InitializeAsync(_cancellationTokenSource.Token);

                // Hook workspace events to persist session state
                _workspaceService.TabOpened += OnWorkspaceTabOpened;
                _workspaceService.TabClosed += OnWorkspaceTabClosed;
                _workspaceService.TabSelectionChanged += OnWorkspaceTabSelectionChangedForPersistence;
                
                _logger.Info("MainViewModel ready - total time < 50ms");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Failed fast MainViewModel initialization");
                _dialogService?.ShowError("Startup failed", "Error");
                throw;
            }
        }

        private void OnWorkspaceTabOpened(object sender, TabEventArgs e)
        {
            try { _tabPersistence.MarkChanged(); } catch { }
        }

        private void OnWorkspaceTabClosed(object sender, TabEventArgs e)
        {
            try { _tabPersistence.MarkChanged(); } catch { }
        }

        private void OnWorkspaceTabSelectionChangedForPersistence(object sender, TabChangedEventArgs e)
        {
            try { _tabPersistence.MarkChanged(); } catch { }
        }

        public List<string> GetSearchSuggestions(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<string>();

            var titlesFromTree = Categories?
                .SelectMany(c => GetAllNotesRecursive(c))
                .Select(n => n.Title) ?? Enumerable.Empty<string>();

            var titlesFromOpen = OpenTabs?.Select(t => t.Title) ?? Enumerable.Empty<string>();

            var all = titlesFromTree
                .Concat(titlesFromOpen)
                .Concat(_recentSearches)
                .Where(t => t?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            return all;
        }

        private void InitializeCommands()
        {
            NewNoteCommand = new RelayCommand(async _ => await CreateNewNoteAsync(), _ => SelectedCategory != null);
            SaveNoteCommand = new RelayCommand(async _ => await SaveCurrentNoteAsync(), _ => SelectedTab != null);
            SaveAllCommand = new RelayCommand(async _ => await SaveAllNotesAsync(), _ => true);
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
                    // Directly shutdown; Window_Closing will force-save
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during exit command");
                    System.Environment.Exit(0);
                }
            });
            SearchNavigateDownCommand = new RelayCommand(_ => NavigateSearch(+1), _ => _searchResults.Count > 0);
            SearchNavigateUpCommand = new RelayCommand(_ => NavigateSearch(-1), _ => _searchResults.Count > 0);
            SearchOpenCommand = new RelayCommand(async _ => await OpenFromSearchAsync(), _ => _searchResults.Count > 0);
            ClearSearchCommand = new RelayCommand(_ => { SearchText = string.Empty; IsSearchActive = false; });
        }

        private async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading...";

                var settings = await _configService.LoadSettingsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                
                await _configService.EnsureDefaultDirectoriesAsync();
                cancellationToken.ThrowIfCancellationRequested();

                // Validate notes root and guide the user if misconfigured
                await ValidateNotesRootAsync(cancellationToken);

                // Initialize auto-save after settings are loaded
                InitializeAutoSave();

                try
                {
                    await LoadCategoriesAsync();
                    if (Categories.Count == 0)
                    {
                        _logger.Warning("No categories loaded. Check notes folder configuration.");
                        _dialogService.ShowInfo("No categories found in the configured notes folder. You can change the folder in Settings.", "No Categories");
                    }
                }
                catch (Exception catEx)
                {
                    _logger.Error(catEx, "Failed to load categories - continuing without them");
                    Categories.Clear();
                }
                
                cancellationToken.ThrowIfCancellationRequested();

                // Ensure workspace view model is created and subscribed before restoration
                _ = GetWorkspaceViewModel();

                // Restore previous tabs
                await RestoreTabsAsync();

                StatusMessage = "Ready";
                _logger.Info("Application initialized successfully");
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Initialization cancelled");
                StatusMessage = "Initialization cancelled";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during initialization");
                if (!cancellationToken.IsCancellationRequested)
                {
                    _errorHandler?.LogError(ex, "Initialization");
                    _dialogService?.ShowError($"Error initializing: {ex.Message}", "Error");
                }
                StatusMessage = "Initialization error";
            }
            finally
            {
                IsLoading = false;
                if (_stateManager != null)
                {
                    _stateManager.IsLoading = false;
                    _stateManager.EndOperation("Ready");
                }
            }
        }

        private async Task ValidateNotesRootAsync(CancellationToken cancellationToken)
        {
            try
            {
                var root = _configService?.Settings?.DefaultNotePath;
                var meta = _configService?.Settings?.MetadataPath;
                var categoriesPath = NoteNest.Core.Services.PathService.CategoriesPath;
                var validRoot = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
                var hasCategories = File.Exists(categoriesPath);

                if (!validRoot || !hasCategories)
                {
                    _logger.Warning($"Notes root validation failed. RootExists={validRoot} HasCategories={hasCategories}");
                    var pick = await _dialogService.ShowYesNoCancelAsync(
                        "Your notes folder isn't configured or looks empty. Do you want to select your existing notes folder now?",
                        "Configure Notes Folder");
                    if (pick == true)
                    {
                        var selected = await _dialogService.ShowInputDialogAsync(
                            "Select Notes Folder",
                            "Enter the full path to your notes root (contains Projects and .metadata):",
                            _configService?.Settings?.DefaultNotePath ?? "",
                            path =>
                            {
                                try { return Directory.Exists(path) ? null : "Folder does not exist."; } catch { return "Invalid path."; }
                            });

                        if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(selected))
                        {
                            _configService.Settings.DefaultNotePath = selected;
                            _configService.Settings.MetadataPath = System.IO.Path.Combine(selected, ".metadata");
                            NoteNest.Core.Services.PathService.RootPath = selected;
                            await _configService.SaveSettingsAsync();
                            await _configService.EnsureDefaultDirectoriesAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"ValidateNotesRoot failed: {ex.Message}");
            }
        }

        private async Task RestoreTabsAsync()
        {
            try
            {
                if (_configService?.Settings?.RestoreTabs != true) return;
                var state = await _tabPersistence.LoadAsync();
                if (state?.Tabs == null || state.Tabs.Count == 0) return;

                // Determine active tab
                var active = !string.IsNullOrEmpty(state.ActiveTabId)
                    ? state.Tabs.FirstOrDefault(t => t.Id == state.ActiveTabId)
                    : state.Tabs.FirstOrDefault();
                if (active == null) return;

                // Open active first with embedded content (no disk read) and select it
                var activeNote = new NoteModel { Id = active.Id, FilePath = active.Path, Title = active.Title, Content = state.ActiveTabContent ?? string.Empty };
                var opened = await _workspaceService.OpenNoteAsync(activeNote);
                try { _workspaceService.SelectedTab = opened; } catch { }

                // Queue other tabs lazily
                var others = state.Tabs.Where(t => t.Id != active.Id).ToList();
                if (others.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        foreach (var t in others)
                        {
                            try
                            {
                                var note = new NoteModel { Id = t.Id, FilePath = t.Path, Title = t.Title, Content = null };
                                await Application.Current.Dispatcher.InvokeAsync(async () =>
                                {
                                    try { await _workspaceService.OpenNoteAsync(note); } catch { }
                                });
                                await Task.Delay(30);
                            }
                            catch { }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"RestoreTabs failed: {ex.Message}");
            }
        }

        // Centralized handler to allow proper unsubscription on dispose
        private void OnStateManagerPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(IStateManager.IsLoading))
                OnPropertyChanged(nameof(IsLoading));
            if (e?.PropertyName == nameof(IStateManager.StatusMessage))
                OnPropertyChanged(nameof(StatusMessage));
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                _stateManager.BeginOperation("Loading categories...");
                Categories.Clear();
                PinnedCategories.Clear();
                PinnedNotes.Clear();

                var flatCategories = await GetCategoryService().LoadCategoriesAsync();
                if (!flatCategories.Any())
                {
                    _logger.Info("No categories loaded");
                }

                // Stop all existing watchers before setting up new ones
                var watcher = GetFileWatcher();
                watcher.StopAllWatchers();

                // Build tree structure off the UI thread, then apply to UI in one batch
                var rootCategories = flatCategories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
                var built = await Task.Run(async () =>
                {
                    var list = new List<CategoryTreeItem>();
                    foreach (var root in rootCategories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                    {
                        var rootItem = await BuildCategoryTreeAsync(root, flatCategories, 0);
                        list.Add(rootItem);
                    }
                    return list;
                });

                // Apply to UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in built)
                    {
                        Categories.Add(item);
                    }
                    // Build pinned categories quick-access (non-destructive)
                    foreach (var cat in Categories)
                    {
                        CollectPinnedCategories(cat, PinnedCategories);
                        CollectPinnedNotes(cat, PinnedNotes);
                    }
                });

                // Set up file watcher
                watcher.StartWatching(PathService.ProjectsPath, "*.*", includeSubdirectories: true);

                _stateManager.EndOperation($"Loaded {flatCategories.Count} categories");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories");
                _dialogService.ShowError($"Error loading categories: {ex.Message}", "Error");
                _stateManager.EndOperation("Error loading categories");
            }
        }

        private async Task<CategoryTreeItem> BuildCategoryTreeAsync(CategoryModel parent, List<CategoryModel> all, int level)
        {
            parent.Level = level;
            // Pass the NoteService to enable lazy loading handled within CategoryTreeItem
            var parentItem = new CategoryTreeItem(parent, _noteService);

            // Build subcategories (pinned first, then by name for consistency with roots)
            var children = all
                .Where(c => c.ParentId == parent.Id)
                .OrderByDescending(c => c.Pinned)
                .ThenBy(c => c.Name)
                .ToList();
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
            if (SelectedCategory == null)
            {
                _dialogService.ShowInfo(
                    "Please select a category first to create a new note.", 
                    "No Category Selected");
                return;
            }

            var title = "New Note " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            var note = await GetNoteOperationsService().CreateNoteAsync(SelectedCategory.Model, title, string.Empty);
            
            if (note != null)
            {
                var noteItem = new NoteTreeItem(note);
                SelectedCategory.Notes.Add(noteItem);
                SelectedCategory.IsExpanded = true;
                
                SelectedNote = noteItem;
                // Route open via split-pane exclusive flow
                NoteOpenRequested?.Invoke(noteItem);
                
                _stateManager.StatusMessage = $"Created: {title}";
            }
        }

		// Removed: OpenNoteAsync - opening is handled exclusively by split-pane layer

        private async Task SaveCurrentNoteAsync()
        {
            var current = GetWorkspaceService().SelectedTab;
            if (current == null) return;

            // Ensure state has freshest content and save
            try
            {
                var text = current.Content ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"[VM] SaveCurrent START noteId={current.Note?.Id} len={text.Length} at={DateTime.Now:HH:mm:ss.fff}");
                _workspaceStateService.UpdateNoteContent(current.Note.Id, text);
            }
            catch { }
            
            var result = await _workspaceStateService.SaveNoteAsync(current.Note.Id);
            System.Diagnostics.Debug.WriteLine($"[VM] SaveCurrent END noteId={current.Note?.Id} success={result?.Success} at={DateTime.Now:HH:mm:ss.fff}");
            if (result?.Success == true)
            {
                _stateManager.StatusMessage = $"Saved: {current.Note.Title}";
            }
        }

        private async Task SaveAllNotesAsync()
        {
            await SaveAllNotesAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
        }

        private async Task SaveAllNotesAsync(CancellationToken cancellationToken)
        {
            if (!GetWorkspaceService().HasUnsavedChanges) return;
            
            _stateManager.BeginOperation("Saving all notes...");
            
            try
            {
                var result = await _workspaceStateService.SaveAllDirtyNotesAsync();
                _stateManager.EndOperation(result.FailureCount > 0
                    ? $"Saved {result.SuccessCount} with {result.FailureCount} errors"
                    : "All notes saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save all notes");
                _stateManager.EndOperation("Error saving notes");
            }
        }

        private async Task CloseTabAsync(NoteTabItem tab)
        {
            if (tab == null) return;

            var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
            if (closeService == null) return;

            // Prefer closing via ITabItem if present; otherwise close via workspace mapping
            var toClose = GetWorkspaceService().OpenTabs
                .FirstOrDefault(t => ReferenceEquals(t?.Note, tab.Note))
                ?? (ITabItem)tab; // NoteTabItem now implements ITabItem
            var closed = await closeService.CloseTabWithPromptAsync(toClose);
            if (closed)
            {
                _workspaceViewModel.RemoveTab(tab);
                _logger.Debug($"Closed tab: {tab.Note.Title}");
            }
        }

        #endregion

        #region Category Operations

        private async Task CreateNewCategoryAsync()
        {
            var name = await _dialogService.ShowInputDialogAsync(
                    "New Category",
                    "Enter category name:",
                "",
                text => Categories.Any(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) 
                    ? "A category with this name already exists." 
                    : null);
            
            if (string.IsNullOrWhiteSpace(name)) return;
            
            var category = await GetCategoryService().CreateCategoryAsync(name);
            if (category != null)
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = $"Created category: {name}";
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
                _dialogService.ShowInfo(
                    "Please select a parent category first.", 
                    "No Category Selected");
                    return;
                }

            var name = await _dialogService.ShowInputDialogAsync(
                    "New Subcategory",
                    $"Enter subcategory name for '{parentCategory.Name}':",
                "",
                text => parentCategory.SubCategories.Any(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
                    ? "A subcategory with this name already exists."
                    : null);
            
            if (string.IsNullOrWhiteSpace(name)) return;
            
            var subCategory = await GetCategoryService().CreateSubCategoryAsync(parentCategory.Model, name);
            if (subCategory != null)
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = $"Created subcategory: {name} under {parentCategory.Name}";
            }
        }

        private async Task DeleteCategoryAsync()
        {
            if (SelectedCategory == null) return;

                int subCount = CountAllCategories(new ObservableCollection<CategoryTreeItem>(new[] { SelectedCategory })) - 1;
                int noteCount = CountAllNotes(SelectedCategory);

                var warning = $"Delete category '{SelectedCategory.Name}'" +
                          (subCount > 0 || noteCount > 0 ? 
                           $" including {subCount} subcategories and {noteCount} notes" : "") + "?";

            if (!await _dialogService.ShowConfirmationDialogAsync(warning, "Confirm Delete"))
                return;

                var categoryName = SelectedCategory.Name;
            if (await GetCategoryService().DeleteCategoryAsync(SelectedCategory.Model))
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = $"Deleted category: {categoryName}";
            }
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
                _searchSelectionIndex = -1;
            }
            else
            {
                var searchLower = SearchText.ToLower();
                _searchResults = new List<NoteTreeItem>();
                
                foreach (var category in Categories)
                {
                    FilterCategory(category, searchLower);
                }
                
                _searchSelectionIndex = -1;
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
            
            _searchSelectionIndex = (_searchSelectionIndex + delta + _searchResults.Count) % _searchResults.Count;
            var target = _searchResults[_searchSelectionIndex];
            
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

        private IEnumerable<NoteTreeItem> GetAllNotesRecursive(CategoryTreeItem category)
        {
            foreach (var n in category.Notes) yield return n;
            foreach (var sub in category.SubCategories)
            {
                foreach (var n in GetAllNotesRecursive(sub)) yield return n;
            }
        }

        private Task OpenFromSearchAsync()
        {
            if (_searchResults.Count == 0) return Task.CompletedTask;
            
            if (_searchSelectionIndex < 0 && _searchResults.Count > 0)
            {
                _searchSelectionIndex = 0;
                NavigateSearch(0);
            }
            
            if (SelectedNote != null)
            {
                // Route open via event to split-pane layer
                NoteOpenRequested?.Invoke(SelectedNote);
            }

            return Task.CompletedTask;
        }

        // Add new search method using the index
        public async Task<List<NoteTreeItem>> SearchNotesAsync(string query)
        {
            if (_searchIndex.NeedsReindex)
            {
                await Task.Run(() =>
                {
                    var allCategories = GetAllCategoriesFlat();
                    var allNotes = new List<NoteModel>();
                    
                    foreach (var category in Categories)
                    {
                        CollectAllNotes(category, allNotes);
                    }
                    
                    _searchIndex.BuildIndex(allCategories, allNotes);
                });
            }

            var results = _searchIndex.Search(query);
            
            var noteItems = new List<NoteTreeItem>();
            foreach (var result in results.Where(r => !string.IsNullOrEmpty(r.NoteId)))
            {
                var noteItem = FindNoteById(result.NoteId);
                if (noteItem != null)
                {
                    noteItems.Add(noteItem);
                }
            }
            
            return noteItems;
        }

        // Helper method to collect all notes
        private void CollectAllNotes(CategoryTreeItem category, List<NoteModel> allNotes)
        {
            foreach (var note in category.Notes)
            {
                allNotes.Add(note.Model);
            }
            
            foreach (var subCategory in category.SubCategories)
            {
                CollectAllNotes(subCategory, allNotes);
            }
        }

        // Helper to find note by ID
        private NoteTreeItem FindNoteById(string noteId)
        {
            foreach (var category in Categories)
            {
                var found = FindNoteInCategory(category, noteId);
                if (found != null) return found;
            }
            return null;
        }

        private NoteTreeItem FindNoteInCategory(CategoryTreeItem category, string noteId)
        {
            var note = category.Notes.FirstOrDefault(n => n.Model.Id == noteId);
            if (note != null) return note;
            
            foreach (var subCategory in category.SubCategories)
            {
                var found = FindNoteInCategory(subCategory, noteId);
                if (found != null) return found;
            }
            
            return null;
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
            // Fix: Use synchronous event handler to avoid async void
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            
            if (_configService.Settings.AutoSave)
            {
                _autoSaveTimer.Start();
            }
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // Execute auto-save on background thread to avoid blocking UI
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VM] AutoSaveTimer tick at={DateTime.Now:HH:mm:ss.fff}");
                        await AutoSaveAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Auto-save timer error");
                }
            }, _cancellationTokenSource.Token);
        }

        private async Task AutoSaveAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[VM] AutoSave START at={DateTime.Now:HH:mm:ss.fff}");
                var result = await _workspaceStateService.SaveAllDirtyNotesAsync();
                System.Diagnostics.Debug.WriteLine($"[VM] AutoSave END success={result?.SuccessCount} fail={result?.FailureCount} at={DateTime.Now:HH:mm:ss.fff}");
                if (result.SuccessCount > 0)
                {
                    StatusMessage = $"Auto-saved {result.SuccessCount} note(s)";
                    _logger.Debug($"Auto-saved {result.SuccessCount} notes");
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
                _errorHandler?.LogError(ex, operationName);
                _dialogService?.ShowError(
                    $"Operation failed: {operationName}\n\nError: {ex.Message}",
                    "Error");
                StatusMessage = $"Error: {operationName}";
            }
        }

        private async Task SafeExecuteAsync(Action operation, string operationName)
        {
            try
            {
                _logger.Debug($"Starting operation: {operationName}");
                await Task.Run(operation);
                _logger.Debug($"Completed operation: {operationName}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed operation: {operationName}");
                _errorHandler?.LogError(ex, operationName);
                _dialogService?.ShowError(
                    $"Operation failed: {operationName}\n\nError: {ex.Message}",
                    "Error");
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
            // This is a local tree traversal, doesn't need to go through service
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

        private void CollectPinnedCategories(CategoryTreeItem category, ObservableCollection<CategoryTreeItem> pinned)
        {
            if (category?.Model?.Pinned == true)
            {
                pinned.Add(category);
            }
            foreach (var sub in category.SubCategories)
            {
                CollectPinnedCategories(sub, pinned);
            }
        }

        public class PinnedNoteItem
        {
            public NoteTreeItem Note { get; }
            public string CategoryName { get; }
            public PinnedNoteItem(NoteTreeItem note, string categoryName)
            {
                Note = note;
                CategoryName = categoryName;
            }
        }

        private void CollectPinnedNotes(CategoryTreeItem category, ObservableCollection<PinnedNoteItem> pinned)
        {
            foreach (var note in category.Notes)
            {
                try
                {
                    if (_notePinService.IsPinned(note.Model.FilePath))
                    {
                        pinned.Add(new PinnedNoteItem(note, category.Name));
                    }
                }
                catch { }
            }
            foreach (var sub in category.SubCategories)
            {
                CollectPinnedNotes(sub, pinned);
            }
        }

        #endregion

        #region Public Methods

        public async Task RenameCategoryAsync(CategoryTreeItem categoryItem, string newName)
        {
            if (categoryItem == null || string.IsNullOrWhiteSpace(newName)) return;

            if (await _categoryService.RenameCategoryAsync(categoryItem.Model, newName))
            {
                // Update the local model to reflect the change immediately
                categoryItem.Model.Name = newName;
                categoryItem.OnPropertyChanged(nameof(CategoryTreeItem.Name));
                _stateManager.StatusMessage = $"Renamed category to '{newName}'";
            }
        }

        public async Task ToggleCategoryPinAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;

            if (await _categoryService.ToggleCategoryPinAsync(categoryItem.Model))
            {
                await LoadCategoriesAsync(); // Refresh to reorder by pin status
                _stateManager.StatusMessage = categoryItem.Model.Pinned ? 
                    $"Pinned category: {categoryItem.Name}" : 
                    $"Unpinned category: {categoryItem.Name}";
            }
        }

        public async Task RenameNoteAsync(NoteTreeItem noteItem, string newName)
        {
            if (noteItem == null || string.IsNullOrWhiteSpace(newName)) return;

            var success = await GetNoteOperationsService().RenameNoteAsync(noteItem.Model, newName);
            
            if (!success)
            {
                _dialogService.ShowError("A note with this name already exists.", "Name Conflict");
                return;
            }
            
            // Update UI elements
            noteItem.OnPropertyChanged(nameof(NoteTreeItem.Title));
            noteItem.OnPropertyChanged(nameof(NoteTreeItem.FilePath));
            
            // Update open tab if exists
            var openTab = OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Note, noteItem.Model));
            if (openTab != null)
            {
                openTab.OnPropertyChanged(nameof(NoteTabItem.Title));
            }
            
            _stateManager.StatusMessage = $"Renamed note to '{newName}'";
        }

        public async Task DeleteNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            // Close tab if open via workspace
            var openTab = OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Note, noteItem.Model));
            if (openTab != null)
            {
                _workspaceViewModel.RemoveTab(openTab);
            }
            
            // Delete through service
            await GetNoteOperationsService().DeleteNoteAsync(noteItem.Model);
            
            // Remove from tree
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
            
            _stateManager.StatusMessage = $"Deleted '{noteItem.Title}'";
        }

        // Removed: OnOpenTabsChanged tracking; tracking handled by workspace service

        public ConfigurationService GetConfigService()
        {
            return _configService;
        }

        public async Task<bool> MoveNoteToCategory(NoteTreeItem noteItem, CategoryTreeItem targetCategory)
        {
            if (noteItem == null || targetCategory == null) return false;
            
            var success = await GetNoteOperationsService().MoveNoteAsync(noteItem.Model, targetCategory.Model);
            
            if (success)
            {
                // Remove from old category tree
                CategoryTreeItem oldCategory = null;
                foreach (var cat in Categories)
                {
                    oldCategory = FindCategoryContainingNote(cat, noteItem);
                    if (oldCategory != null) break;
                }
                
                if (oldCategory != null)
                {
                    oldCategory.Notes.Remove(noteItem);
                }
                
                // Add to new category tree
                targetCategory.Notes.Add(noteItem);
                
                // Update open tab if exists
                var openTab = OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Note, noteItem.Model));
                if (openTab != null)
                {
                    openTab.OnPropertyChanged(nameof(NoteTabItem.Title));
                }
                
                _stateManager.StatusMessage = $"Moved '{noteItem.Title}' to '{targetCategory.Name}'";
            }
            
            return success;
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
            if (!_disposed && disposing)
            {
                _logger?.Info("Fast disposing MainViewModel");
                
                try
                {
                    // Cancel operations quickly
                    _cancellationTokenSource?.Cancel();

                    // Unsubscribe events
                    if (_workspaceService != null)
                    {
                        _workspaceService.TabSelectionChanged -= OnServiceTabSelectionChanged;
                        _workspaceService.TabOpened -= OnWorkspaceTabOpened;
                        _workspaceService.TabClosed -= OnWorkspaceTabClosed;
                        _workspaceService.TabSelectionChanged -= OnWorkspaceTabSelectionChangedForPersistence;
                    }
                    if (_stateManager != null)
                    {
                        _stateManager.PropertyChanged -= OnStateManagerPropertyChanged;
                    }

                    // Dispose only what was actually created
                    _searchIndex = null;
                    _fileWatcher?.Dispose();
                    (_workspaceViewModel as IDisposable)?.Dispose();
                    (_workspaceService as IDisposable)?.Dispose();

                    _cancellationTokenSource?.Dispose();
                    _autoSaveTimer?.Stop();
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Disposal error - continuing: {ex.Message}");
                }
                
                _disposed = true;
            }
        }

        #endregion
    }
}