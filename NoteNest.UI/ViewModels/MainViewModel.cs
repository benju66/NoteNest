using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Commands;
using System.IO;
using NoteNest.Core.Interfaces.Services;
using ISaveManagerFactory = NoteNest.Core.Interfaces.Services.ISaveManagerFactory;
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
        private readonly ITabPersistenceService _tabPersistence;
        private readonly ISaveManager _saveManager;
        private readonly IServiceProvider _serviceProvider;

        // Lazy Services (created only when needed)
        private FileWatcherService _fileWatcher;
        private NoteNest.Core.Services.NoteMetadataManager _metadataManager;
        private IPinService _pinService;
        private ICategoryManagementService _categoryService;
        private INoteOperationsService _noteOperationsService;
        private ITreeDataService _treeDataService;
        private ITreeOperationService _treeOperationService;
        private TreeViewModelAdapter _treeViewModelAdapter;
        private TreeOperationAdapter _treeOperationAdapter;
        private ITreeStateManager _treeStateManager;
        private TreeStateAdapter _treeStateAdapter;
        private ITreeController _treeController;
        private TreeControllerAdapter _treeControllerAdapter;
        private readonly IWorkspaceService _workspaceService;
        private WorkspaceViewModel _workspaceViewModel;
        private FileSystemEventCoordinator _fileSystemEventCoordinator;
        private TabPersistenceCoordinator _tabPersistenceCoordinator;
        // Auto-save timer removed - per-tab timers handle all auto-save operations
        // NotePinService removed - will be reimplemented with better architecture later
        private bool _disposed;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _initializationTask;
        private bool _recoveryInProgress;
        private HashSet<string> _pendingRecoveryNotes = new();
        
        private ObservableCollection<CategoryTreeItem> _categories;
        private ObservableCollection<CategoryTreeItem> _pinnedCategories;
        private ObservableCollection<PinnedNoteItem> _pinnedNotes;
        private CategoryTreeItem _selectedCategory;
        private NoteTreeItem _selectedNote;
        private bool _isLoading;
        private string _statusMessage;

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

        public bool HasPinnedItems => 
            (PinnedNotes?.Count > 0) || (PinnedCategories?.Count > 0);

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

        private FileWatcherService GetFileWatcher()
        {
            return _fileWatcher ??= new FileWatcherService(_logger, _configService);
        }


        private NoteNest.Core.Services.NoteMetadataManager GetMetadataManager()
        {
            return _metadataManager ??= _serviceProvider.GetRequiredService<NoteNest.Core.Services.NoteMetadataManager>();
        }

        private IPinService GetPinService()
        {
            return _pinService ??= _serviceProvider.GetRequiredService<IPinService>();
        }

        private ICategoryManagementService GetCategoryService()
        {
            return _categoryService ??= new CategoryManagementService(
                _noteService,
                _configService,
                _errorHandler,
                _logger,
                _fileSystem,
                (Application.Current as NoteNest.UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IEventBus)) as NoteNest.Core.Services.IEventBus);
        }

        private INoteOperationsService GetNoteOperationsService()
        {
            return _noteOperationsService ??= new NoteOperationsService(
                _noteService,
                _errorHandler,
                _logger,
                _fileSystem,
                _configService,
                _contentCache,
                _saveManager);
        }

        private IWorkspaceService GetWorkspaceService() => _workspaceService;

        private WorkspaceViewModel GetWorkspaceViewModel()
        {
            return _workspaceViewModel ??= new WorkspaceViewModel(GetWorkspaceService(), _saveManager);
        }

        private ITreeDataService GetTreeDataService()
        {
            return _treeDataService ??= _serviceProvider.GetRequiredService<ITreeDataService>();
        }

        private ITreeOperationService GetTreeOperationService()
        {
            return _treeOperationService ??= _serviceProvider.GetRequiredService<ITreeOperationService>();
        }

        private ITreeStateManager GetTreeStateManager()
        {
            return _treeStateManager ??= _serviceProvider.GetRequiredService<ITreeStateManager>();
        }

        private ITreeController GetTreeController()
        {
            return _treeController ??= _serviceProvider.GetRequiredService<ITreeController>();
        }

        private TreeViewModelAdapter GetTreeViewModelAdapter()
        {
            return _treeViewModelAdapter ??= new TreeViewModelAdapter(_noteService);
        }

        private TreeOperationAdapter GetTreeOperationAdapter()
        {
            return _treeOperationAdapter ??= new TreeOperationAdapter(GetTreeOperationService());
        }

        private TreeStateAdapter GetTreeStateAdapter()
        {
            return _treeStateAdapter ??= new TreeStateAdapter(GetTreeStateManager());
        }

        private FileSystemEventCoordinator GetFileSystemEventCoordinator()
        {
            return _fileSystemEventCoordinator ??= new FileSystemEventCoordinator(
                _logger, 
                _serviceProvider,
                FindNoteIdByFilePathAsync);
        }

        private TabPersistenceCoordinator GetTabPersistenceCoordinator()
        {
            return _tabPersistenceCoordinator ??= new TabPersistenceCoordinator(
                _tabPersistence,
                _saveManager,
                _workspaceService,
                _logger,
                () => _recoveryInProgress,
                () => _pendingRecoveryNotes,
                (noteId) => _pendingRecoveryNotes.Remove(noteId),
                () => _recoveryInProgress = false);
        }

        private async void OnFileRenamed(object sender, FileRenamedEventArgs e)
        {
            var coordinator = GetFileSystemEventCoordinator();
            await coordinator.HandleFileRenamedAsync(e.OldPath, e.NewPath);
        }

        private async void OnFileDeleted(object sender, FileChangedEventArgs e)
        {
            var coordinator = GetFileSystemEventCoordinator();
            await coordinator.HandleFileDeletedAsync(e.FilePath);
        }

        private async void OnFileCreated(object sender, FileChangedEventArgs e)
        {
            var coordinator = GetFileSystemEventCoordinator();
            await coordinator.HandleFileCreatedAsync(e.FilePath);
        }

        private async void OnFileModified(object sender, FileChangedEventArgs e)
        {
            var coordinator = GetFileSystemEventCoordinator();
            await coordinator.HandleFileModifiedAsync(e.FilePath);
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

        #endregion

        #region Context Menu Commands

        private ICommand _renameNoteCommand;
        public ICommand RenameNoteCommand => _renameNoteCommand ??= new AsyncRelayCommand<NoteTreeItem>(
            async (note) => await ExecuteRenameNoteAsync(note),
            (note) => note != null);

        private ICommand _toggleNotePinCommand;
        public ICommand ToggleNotePinCommand => _toggleNotePinCommand ??= new AsyncRelayCommand<NoteTreeItem>(
            async (note) => await ExecuteToggleNotePinAsync(note),
            (note) => note != null);

        private ICommand _deleteNoteCommand;
        public ICommand DeleteNoteCommand => _deleteNoteCommand ??= new AsyncRelayCommand<NoteTreeItem>(
            async (note) => await ExecuteDeleteNoteAsync(note),
            (note) => note != null);

        private ICommand _openNoteCommand;
        public ICommand OpenNoteCommand => _openNoteCommand ??= new RelayCommand<NoteTreeItem>(
            (note) => ExecuteOpenNote(note),
            (note) => note != null);

        private ICommand _renameCategoryCommand;
        public ICommand RenameCategoryCommand => _renameCategoryCommand ??= new AsyncRelayCommand<CategoryTreeItem>(
            async (cat) => await ExecuteRenameCategoryAsync(cat),
            (cat) => cat != null);

        private ICommand _deleteCategoryMenuCommand;
        public ICommand DeleteCategoryMenuCommand => _deleteCategoryMenuCommand ??= new AsyncRelayCommand<CategoryTreeItem>(
            async (cat) => await ExecuteDeleteCategoryAsync(cat),
            (cat) => cat != null && !cat.SubCategories.Any() && !cat.Notes.Any());

        private ICommand _newSubcategoryCommand;
        public ICommand NewSubcategoryCommand => _newSubcategoryCommand ??= new AsyncRelayCommand<CategoryTreeItem>(
            async (cat) => await CreateNewSubCategoryAsync(cat),
            (cat) => cat != null);

        private ICommand _toggleCategoryPinCommand;
        public ICommand ToggleCategoryPinCommand => _toggleCategoryPinCommand ??= new AsyncRelayCommand<CategoryTreeItem>(
            async (cat) => await ToggleCategoryPinAsync(cat),
            (cat) => cat != null);

        private async Task ExecuteRenameNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            try
            {
                var currentName = noteItem.Title;
                var siblingNames = SelectedCategory?.Notes?
                    .Where(n => n != noteItem)
                    .Select(n => n.Title)
                    .Where(title => !string.IsNullOrEmpty(title)) ?? Enumerable.Empty<string>();
                
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Note",
                    "Enter new name:",
                    currentName,
                    text => NoteNest.Core.Services.RenameValidation.ValidateNoteName(text, currentName, siblingNames));
                
                if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
                {
                    await RenameNoteAsync(noteItem, newName);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to execute rename note command");
                _dialogService?.ShowError($"Failed to rename note: {ex.Message}", "Rename Error");
            }
        }

        private async Task ExecuteToggleNotePinAsync(NoteTreeItem noteItem)
        {
            _logger?.Debug($"ExecuteToggleNotePinAsync called with note: {noteItem?.Title ?? "NULL"}");
            if (noteItem == null) 
            {
                _logger?.Warning("ExecuteToggleNotePinAsync called with null noteItem");
                return;
            }
            
            try
            {
                var pinService = GetPinService();
                if (pinService == null)
                {
                    _logger?.Error("Pin service not available");
                    _dialogService?.ShowError("Pin service is not available", "Pin Error");
                    return;
                }
                
                // CRITICAL FIX: Always use metadata ID for consistency
                // Get the correct metadata ID instead of using noteItem.Model.Id
                var metadataManager = _serviceProvider?.GetService<NoteNest.Core.Services.NoteMetadataManager>();
                string noteId = noteItem.Model.Id; // fallback to current ID
                
                if (metadataManager != null && !string.IsNullOrEmpty(noteItem.Model.FilePath))
                {
                    try
                    {
                        // Create a temporary note model to get metadata ID
                        var tempNote = new NoteNest.Core.Models.NoteModel
                        {
                            FilePath = noteItem.Model.FilePath,
                            Id = noteItem.Model.Id
                        };
                        
                        // Get the canonical metadata ID
                        noteId = await metadataManager.GetOrCreateNoteIdAsync(tempNote);
                        _logger?.Debug($"Using metadata ID for pin operation: {noteId} (was: {noteItem.Model.Id})");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to get metadata ID for {noteItem.Title}, using fallback: {noteItem.Model.Id}. Error: {ex.Message}");
                        noteId = noteItem.Model.Id;
                    }
                }
                
                // Toggle the pin state using the correct metadata ID
                var success = await pinService.TogglePinAsync(noteId, noteItem.Model.FilePath);
                
                if (success)
                {
                    // Refresh pinned items collection
                    _logger?.Debug("Refreshing pinned items collection...");
                    await RefreshPinnedItemsAsync();
                    
                    // Check current pin state for status message using the same metadata ID
                    var isPinned = await pinService.IsPinnedAsync(noteId);
                    var status = isPinned ? "Pinned" : "Unpinned";
                    StatusMessage = $"{status} '{noteItem.Title}'";
                    _logger?.Debug($"Pin state changed: {noteItem.Title} -> {status}");
                }
                else
                {
                    _logger?.Warning($"Failed to toggle pin state for: {noteItem.Title}");
                    _dialogService?.ShowError("Failed to update pin state", "Pin Error");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to toggle note pin for: {noteItem?.Title}");
                _dialogService?.ShowError($"Failed to pin/unpin note: {ex.Message}", "Pin Error");
            }
        }

        private async Task ExecuteDeleteNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete '{noteItem.Title}'?\n\nThis action cannot be undone.",
                    "Confirm Delete");
                
                if (result)
                {
                    await DeleteNoteAsync(noteItem);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to delete note");
                _dialogService?.ShowError($"Failed to delete note: {ex.Message}", "Delete Error");
            }
        }

        private void ExecuteOpenNote(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            try
            {
                NoteOpenRequested?.Invoke(noteItem);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to open note");
                _dialogService?.ShowError($"Failed to open note: {ex.Message}", "Open Error");
            }
        }

        private async Task ExecuteRenameCategoryAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;
            
            try
            {
                var currentName = categoryItem.Name;
                var siblingNames = GetSiblingCategoryNames(categoryItem);
                
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Category",
                    "Enter new name:",
                    currentName,
                    text => NoteNest.Core.Services.RenameValidation.ValidateCategoryName(text, currentName, siblingNames));
                
                if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
                {
                    await RenameCategoryAsync(categoryItem, newName);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to rename category");
                _dialogService?.ShowError($"Failed to rename category: {ex.Message}", "Rename Error");
            }
        }

        private async Task ExecuteDeleteCategoryAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;
            
            try
            {
                if (categoryItem.SubCategories.Any() || categoryItem.Notes.Any())
                {
                    _dialogService.ShowError(
                        "Cannot delete a category that contains notes or subcategories.\n\nPlease move or delete all contents first.",
                        "Category Not Empty");
                    return;
                }
                
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete the category '{categoryItem.Name}'?",
                    "Confirm Delete");
                
                if (result)
                {
                    // Use existing delete method
                    SelectedCategory = categoryItem;
                    await DeleteCategoryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to delete category");
                _dialogService?.ShowError($"Failed to delete category: {ex.Message}", "Delete Error");
            }
        }

        private IEnumerable<string> GetSiblingCategoryNames(CategoryTreeItem categoryItem)
        {
            if (categoryItem?.Model == null) return Enumerable.Empty<string>();
            
            if (categoryItem.Model.ParentId == null)
            {
                // Root level - check against other root categories
                return Categories?
                    .Where(c => c != categoryItem)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name)) ?? Enumerable.Empty<string>();
            }
            else
            {
                // Find parent and get siblings
                var parent = FindCategoryById(Categories, categoryItem.Model.ParentId);
                return parent?.SubCategories?
                    .Where(c => c != categoryItem)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name)) ?? Enumerable.Empty<string>();
            }
        }

        private CategoryTreeItem FindCategoryById(ObservableCollection<CategoryTreeItem> categories, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            
            foreach (var category in categories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                if (string.Equals(category.Model?.Id, id, StringComparison.OrdinalIgnoreCase))
                    return category;
                
                var found = FindCategoryById(category.SubCategories, id);
                if (found != null) return found;
            }
            return null;
        }

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
            ITabPersistenceService tabPersistence,
            // NotePinService removed - will be reimplemented later
            ISaveManager saveManager,
            IServiceProvider serviceProvider)
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
            _tabPersistence = tabPersistence ?? throw new ArgumentNullException(nameof(tabPersistence));
            // NotePinService initialization removed
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

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

                // TabPersistenceCoordinator will handle workspace events for persistence
                
                _logger.Info("MainViewModel ready - total time < 50ms");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Failed fast MainViewModel initialization");
                _dialogService?.ShowError("Startup failed", "Error");
                throw;
            }
        }




        // Event handler for emergency save notifications
        private void OnSaveCompleted(object? sender, SaveProgressEventArgs e)
        {
            if (e != null && e.FilePath.Contains("EMERGENCY"))
            {
                // Show notification to user
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var message = $"File could not be saved to original location.\n" +
                                 $"Emergency backup saved to:\n{e.FilePath}";
                    
                    _dialogService?.ShowError(message, "Emergency Save");
                    
                    // Update status bar
                    _stateManager.StatusMessage = "Emergency save completed - check Documents/NoteNest_Emergency";
                });
            }
        }

        // ADD method for external change handling:
        private async void OnExternalChangeDetected(object sender, ExternalChangeEventArgs e)
        {
            // Run on UI thread
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var result = MessageBox.Show(
                    $"The file '{Path.GetFileName(e.FilePath)}' has been modified externally.\n\n" +
                    "Do you want to reload it?\n\n" +
                    "Yes = Reload from disk (lose local changes)\n" +
                    "No = Keep local version (overwrite on next save)",
                    "External Change Detected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    var saveManager = _serviceProvider.GetService<ISaveManager>();
                    await saveManager.ResolveExternalChangeAsync(e.NoteId, ConflictResolution.KeepExternal);
                    
                    // Refresh UI
                    var workspace = GetWorkspaceService();
                    var tab = workspace.FindTabByPath(e.FilePath);
                    if (tab != null)
                    {
                        tab.Content = saveManager.GetContent(e.NoteId);
                    }
                }
                else
                {
                    var saveManager = _serviceProvider.GetService<ISaveManager>();
                    await saveManager.ResolveExternalChangeAsync(e.NoteId, ConflictResolution.KeepLocal);
                }
            });
        }
        
        private async Task LoadTemplatesAsync()
        {
            // TODO: Implement template loading if needed
            await Task.CompletedTask;
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

                // 1. FIRST: Run recovery service (MUST be before opening any notes)
                var recoveryService = _serviceProvider.GetService<StartupRecoveryService>();
                if (recoveryService != null)
                {
                    StatusMessage = "Checking for interrupted saves...";
                    var notesPath = _configService.Settings.DefaultNotePath;
                    
                    var summary = await recoveryService.RecoverInterruptedSavesAsync(notesPath);
                    
                    if (summary.RecoveredFiles.Count > 0)
                    {
                        StatusMessage = $"Recovered {summary.RecoveredFiles.Count} interrupted saves";
                        await Task.Delay(2000); // Show message briefly
                    }
                }

                // Check for recovery and notify user
                await CheckForRecovery();
                
                // Check for emergency saves from previous sessions
                await CheckForEmergencySaves();

                try
                {
                    await LoadCategoriesAsync();
                    if (Categories.Count == 0)
                    {
                        _logger.Warning("No categories loaded. Check notes folder configuration.");
                        _dialogService.ShowInfo("No categories found in the configured notes folder. You can change the folder in Settings.", "No Categories");
                    }
                    else
                    {
                        // Migrate note positions for existing notes (safe to call multiple times)
                        await MigrateNotePositionsIfNeededAsync();
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
                await GetTabPersistenceCoordinator().RestoreTabsAsync();
                
                // 4. Subscribe to save events
                _saveManager.ExternalChangeDetected += OnExternalChangeDetected;
                _saveManager.SaveCompleted += OnSaveCompleted;

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
                // SIMPLIFIED: FirstTimeSetupService should have already configured the storage path
                // This method now just performs basic validation and logging
                var root = _configService?.Settings?.DefaultNotePath;
                var meta = _configService?.Settings?.MetadataPath;
                var categoriesPath = NoteNest.Core.Services.PathService.CategoriesPath;
                var validRoot = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
                var hasCategories = File.Exists(categoriesPath);

                if (!validRoot)
                {
                    _logger.Error($"Notes root validation failed after FirstTimeSetupService - this should not happen. Root: {root}, Exists: {Directory.Exists(root ?? "")}");
                    throw new InvalidOperationException($"Notes root is not configured properly: {root}");
                }

                if (!hasCategories)
                {
                    _logger.Info("Categories file not found - this is normal for new installations");
                    // Create empty categories file if it doesn't exist
                    try
                    {
                        var categoriesDir = Path.GetDirectoryName(categoriesPath);
                        if (!Directory.Exists(categoriesDir))
                        {
                            Directory.CreateDirectory(categoriesDir);
                        }
                        
                        var initialCategories = new { Categories = new object[0] };
                        var json = System.Text.Json.JsonSerializer.Serialize(initialCategories, 
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(categoriesPath, json);
                        
                        _logger.Info($"Created initial categories file: {categoriesPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to create initial categories file: {ex.Message}");
                        // Continue - categories will be created when first category is added
                    }
                }

                _logger.Info($"Notes root validation completed. Root: {root}, HasCategories: {hasCategories}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during notes root validation");
                throw; // Re-throw since this indicates a serious configuration problem
            }
        }

        // REMOVED: ApplyBasicFirstRunSetup and ForceUpdateSaveEngineLocationAsync methods
        // These fallback mechanisms are no longer needed since FirstTimeSetupService
        // handles path configuration before DI container initialization

        private async Task CheckForRecovery()
        {
            try
            {
                var wal = GetService<IWriteAheadLog>();
                if (wal == null) return;
                
                var recovered = await wal.RecoverAllAsync();
                if (recovered.Count > 0)
                {
                    _logger.Info($"Found {recovered.Count} unsaved notes from previous session");
                    
                    // Optional: Show notification to user
                    var message = recovered.Count == 1 
                        ? "Recovered 1 unsaved note from previous session" 
                        : $"Recovered {recovered.Count} unsaved notes from previous session";
                        
                    _stateManager.StatusMessage = message;
                    
                    // Note: The recovered content will be loaded when notes are opened
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for recovery");
            }
        }

        private async Task RecoverUnpersistedChangesAsync(CancellationToken cancellationToken)
        {
            // Recovery is now handled by StartupRecoveryService during initialization
            // This method is kept for compatibility but does nothing
            await Task.CompletedTask;
        }

        private T GetService<T>()
        {
            return _serviceProvider.GetService<T>();
        }

        private async Task CheckForEmergencySaves()
        {
            try
            {
                var emergencyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest_Emergency");
                
                if (Directory.Exists(emergencyDir))
                {
                    var emergencyFiles = Directory.GetFiles(emergencyDir, "EMERGENCY_*.txt");
                    if (emergencyFiles.Length > 0)
                    {
                        var message = $"Found {emergencyFiles.Length} emergency backup(s) from previous sessions.\n" +
                                     $"Check {emergencyDir} to recover your content.";
                        
                        _dialogService?.ShowInfo(message, "Emergency Backups Found");
                        
                        _stateManager.StatusMessage = $"Found {emergencyFiles.Length} emergency backup(s) - check Documents folder";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for emergency saves");
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
                _logger.Debug("Loading categories...");
                _stateManager.BeginOperation("Loading categories...");
                
                // SAVE CURRENT EXPANSION STATE BEFORE CLEARING
                // This preserves what the user currently has expanded
                if (Categories != null && Categories.Count > 0)
                {
                    try
                    {
                        await GetTreeStateAdapter().SaveExpansionStateAsync(Categories);
                        _logger.Debug("Saved current expansion state before tree rebuild");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to save current expansion state: {ex.Message}");
                    }
                }
                
                Categories.Clear();
                PinnedCategories.Clear();
                PinnedNotes.Clear();
                _logger.Debug("Collections cleared, loading tree data...");
                
                var loadTask = GetTreeDataService().LoadTreeDataAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                var completedTask = await Task.WhenAny(loadTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _logger.Error("TreeDataService timed out after 15 seconds");
                    _stateManager.EndOperation("Error: TreeDataService timeout");
                    _dialogService.ShowError("TreeDataService timed out. Check logs for details.", "Loading Timeout");
                    return;
                }
                
                var treeData = await loadTask;
                _logger.Debug($"TreeDataService completed: Success={treeData.Success}");
                
                if (!treeData.Success)
                {
                    _logger.Error($"TreeDataService failed: {treeData.ErrorMessage}");
                    _dialogService.ShowError($"Error loading categories: {treeData.ErrorMessage}", "Error");
                    _stateManager.EndOperation("Error loading categories");
                    return;
                }

                // Convert tree data to UI ViewModels using adapter
                var uiCollections = GetTreeViewModelAdapter().ConvertToUICollections(treeData);

                // Apply tree data to UI collections
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    foreach (var item in uiCollections.RootCategories)
                    {
                        Categories.Add(item);
                    }
                    
                    // Set pinned collections using model properties
                    await RefreshPinnedItemsAsync();
                });

                // Restore expansion state using TreeStateAdapter
                // Restore expansion state
                try
                {
                    // First collapse everything so restore is authoritative
                    // First collapse everything so restore is authoritative
                    void CollapseAll(System.Collections.ObjectModel.ObservableCollection<CategoryTreeItem> items)
                    {
                        foreach (var i in items)
                        {
                            i.IsExpanded = false;
                            CollapseAll(i.SubCategories);
                        }
                    }
                    CollapseAll(Categories);

                    var success = await GetTreeStateAdapter().LoadAndApplyExpansionStateAsync(Categories);
                    
                    if (success)
                    {
                        _logger.Debug("TreeStateAdapter: Successfully restored expansion state");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"TreeStateAdapter: Expansion state restore failed: {ex.Message}");
                }

                // Set up file watcher event handlers and metadata manager
                // Set up file watcher event handlers and metadata manager
                var fileWatcher = GetFileWatcher();
                fileWatcher.FileRenamed += OnFileRenamed;
                fileWatcher.FileDeleted += OnFileDeleted;
                fileWatcher.FileCreated += OnFileCreated;  // FTS5 integration
                fileWatcher.FileChanged += OnFileModified; // FTS5 integration
                _metadataManager ??= new NoteNest.Core.Services.NoteMetadataManager(_fileSystem, _logger);
                fileWatcher.StartWatching(PathService.ProjectsPath, "*.*", includeSubdirectories: true);

                _stateManager.EndOperation($"Loaded {treeData.TotalCategoriesLoaded} categories");
                _logger.Debug($"LoadCategoriesAsync completed - {treeData.TotalCategoriesLoaded} categories");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in LoadCategoriesAsync");
                _stateManager.EndOperation("Error loading categories");
                _dialogService.ShowError($"Error loading categories: {ex.Message}", "Error");
            }
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
            
            try
            {
                var note = await GetNoteOperationsService().CreateNoteAsync(SelectedCategory.Model, title, string.Empty);
                
                if (note != null)
                {
                    await HandleNewNoteCreated(note);
                }
            }
            catch (ArgumentException ex) when (ex.Message.Contains("reserved") || 
                                               ex.Message.Contains("invalid"))
            {
                // Path validation failed
                _dialogService?.ShowError(
                    $"Cannot create note with name '{title}':\n{ex.Message}", 
                    "Invalid Name");
                return;
            }
            catch (Exception ex)
            {
                // Other errors
                _logger.Error(ex, $"Failed to create note: {title}");
                _dialogService?.ShowError($"Failed to create note: {ex.Message}");
                return;
            }
        }

        private async Task HandleNewNoteCreated(NoteModel note)
        {
            // If the category is expanded but not yet loaded, load to avoid duplicate when lazy load triggers
            if (SelectedCategory.IsExpanded && !SelectedCategory.IsLoaded)
            {
                await SelectedCategory.LoadChildrenAsync();
            }

            // Prevent duplicate additions (by Id or FilePath)
            var existingNote = SelectedCategory.Notes.FirstOrDefault(n =>
                string.Equals(n.Model.FilePath, note.FilePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Model.Id, note.Id, StringComparison.OrdinalIgnoreCase));

            NoteTreeItem noteItem = existingNote ?? new NoteTreeItem(note);
            if (existingNote == null)
            {
                SelectedCategory.Notes.Add(noteItem);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Prevented duplicate add in CreateNewNoteAsync: id={note.Id} path={note.FilePath}");
            }
            SelectedCategory.IsExpanded = true;
            
            SelectedNote = noteItem;
            
            // Route open via split-pane exclusive flow
            NoteOpenRequested?.Invoke(noteItem);
            
            // TRIGGER IMMEDIATE RENAME for better UX
            // Give user chance to provide proper name right away
            Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Task.Delay(100); // Let UI update
                    await TriggerRenameNoteAsync(noteItem, "New Note");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to trigger immediate rename: {ex.Message}");
                    _stateManager.StatusMessage = $"Created: {note.Title}";
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Triggers an immediate rename dialog for a note with enhanced UX
        /// </summary>
        private async Task TriggerRenameNoteAsync(NoteTreeItem noteItem, string placeholder)
        {
            if (noteItem == null) return;
            
            try
            {
                var currentName = noteItem.Title;
                var isNewNote = currentName.StartsWith("New Note", StringComparison.OrdinalIgnoreCase);
                
                // Use enhanced dialog approach (will create ModernInputDialog in Phase 2)
                var newName = await _dialogService.ShowInputDialogAsync(
                    "Rename Note",
                    isNewNote ? "Enter a name for your new note:" : "Enter new name:",
                    isNewNote ? string.Empty : currentName, // Empty for new notes, current name for existing
                    text =>
                    {
                        if (string.IsNullOrWhiteSpace(text)) return "Note name cannot be empty.";
                        if (text.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return null; // No change
                        
                        // Check for duplicates in the same category
                        if (SelectedCategory?.Notes?.Any(n => n != noteItem && 
                            string.Equals(n.Title, text, StringComparison.OrdinalIgnoreCase)) == true)
                        {
                            return "A note with this name already exists in this category.";
                        }
                        return null; // Valid
                    });
                
                if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
                {
                    await RenameNoteAsync(noteItem, newName);
                }
                else if (isNewNote && string.IsNullOrWhiteSpace(newName))
                {
                    // User cancelled rename of new note - keep original name but show message
                    _stateManager.StatusMessage = $"Created: {currentName} - press F2 to rename";
                }
                else
                {
                    _stateManager.StatusMessage = $"Created: {currentName}";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to trigger rename dialog");
                _stateManager.StatusMessage = $"Created: {noteItem.Title}";
            }
        }

		// Removed: OpenNoteAsync - opening is handled exclusively by split-pane layer

        private async Task SaveCurrentNoteAsync()
        {
            var current = GetWorkspaceService().SelectedTab;
            if (current != null)
            {
                try
                {
                    // Use RTF-integrated save system (now the only system)
                    var rtfSaveWrapper = (Application.Current as App)?.ServiceProvider?
                        .GetService(typeof(RTFSaveEngineWrapper)) as RTFSaveEngineWrapper;
                    
                    if (rtfSaveWrapper != null && current is NoteTabItem noteTabItem && noteTabItem.Editor != null)
                    {
                        var saveResult = await rtfSaveWrapper.SaveFromRichTextBoxAsync(
                            current.NoteId,
                            noteTabItem.Editor, // Direct access to RTF editor
                            current.Note?.Title ?? "Untitled",
                            NoteNest.Core.Services.SaveType.Manual
                        );
                        
                        if (saveResult.Success)
                        {
                            // Success message handled by WPFStatusNotifier
                            noteTabItem.IsDirty = false;
                            noteTabItem.Note.IsDirty = false;
                        }
                        // Error messages also handled by WPFStatusNotifier
                        return;
                    }
                    else
                    {
                        // Fallback to ISaveManager interface (still uses RTFIntegratedSaveEngine)
                        StatusMessage = "Saving...";
                        var result = await _saveManager.SaveNoteAsync(current.NoteId);
                        StatusMessage = result ? "Saved" : "Save failed";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Save error: {ex.Message}";
                    _logger?.Error(ex, "Error in SaveCurrentNoteAsync");
                }
            }
        }

        private async Task SaveAllNotesAsync()
        {
            await SaveAllNotesAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
        }

        private async Task SaveAllNotesAsync(CancellationToken cancellationToken)
        {
            StatusMessage = "Saving all notes...";
            var result = await _saveManager.SaveAllDirtyAsync();
            
            if (result.FailureCount > 0)
            {
                StatusMessage = $"Saved {result.SuccessCount} notes, {result.FailureCount} failed";
                
                // Show details of failures
                if (result.FailedNoteIds.Count > 0)
                {
                    var message = "Failed to save:\n";
                    foreach (var noteId in result.FailedNoteIds)
                    {
                        var path = _saveManager.GetFilePath(noteId);
                        message += $"- {Path.GetFileName(path)}\n";
                    }
                    MessageBox.Show(message, "Save Failures", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (result.SuccessCount > 0)
            {
                StatusMessage = $"Saved {result.SuccessCount} notes";
            }
            else
            {
                StatusMessage = "No changes to save";
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
            
            var result = await GetTreeOperationAdapter().CreateCategoryAsync(name);
            if (result.Success)
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = result.StatusMessage ?? $"Created category: {name}";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to create category", "Error");
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
            
            var result = await GetTreeOperationAdapter().CreateSubCategoryAsync(parentCategory, name);
            if (result.Success)
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = result.StatusMessage ?? $"Created subcategory: {name} under {parentCategory.Name}";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to create subcategory", "Error");
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
                var result = await GetTreeOperationAdapter().DeleteCategoryAsync(SelectedCategory);
            if (result.Success)
            {
                await LoadCategoriesAsync(); // Refresh tree
                _stateManager.StatusMessage = result.StatusMessage ?? $"Deleted category: {categoryName}";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to delete category", "Error");
            }
        }

        

        #endregion

        #region Helper Methods



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

        // Helper methods delegated to TreeControllerAdapter
        private CategoryTreeItem FindCategoryContainingNote(CategoryTreeItem category, NoteTreeItem note)
        {
            if (category.Notes.Contains(note)) return category;
            foreach (var subCategory in category.SubCategories)
            {
                var found = FindCategoryContainingNote(subCategory, note);
                if (found != null) return found;
            }
            return null;
        }

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
            // AUTO-SAVE ARCHITECTURE SIMPLIFIED: 
            // Removed global auto-save timer to eliminate conflicts with per-tab auto-save
            // Per-tab timers now handle all auto-save operations for better user experience
            _logger.Info("Auto-save handled by per-tab timers (5-second silent auto-save)");
        }

        // Global auto-save timer removed - per-tab timers handle all auto-save operations

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

        // Tree helper methods - simplified for UI use only
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




        #endregion

        #region Public Methods

        public async Task RenameCategoryAsync(CategoryTreeItem categoryItem, string newName)
        {
            if (categoryItem == null || string.IsNullOrWhiteSpace(newName)) return;

            var result = await GetTreeOperationAdapter().RenameCategoryAsync(categoryItem, newName);
            if (result.Success)
            {
                // Update the local model to reflect the change immediately
                categoryItem.Model.Name = newName;
                categoryItem.OnPropertyChanged(nameof(CategoryTreeItem.Name));
                _stateManager.StatusMessage = result.StatusMessage ?? $"Renamed category to '{newName}'";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to rename category", "Error");
            }
        }

        public async Task ToggleCategoryPinAsync(CategoryTreeItem categoryItem)
        {
            if (categoryItem == null) return;

            var result = await GetTreeOperationAdapter().ToggleCategoryPinAsync(categoryItem);
            if (result.Success)
            {
                await RefreshPinnedItemsAsync(); // Use unified refresh instead of full reload
                _stateManager.StatusMessage = result.StatusMessage ?? 
                    (categoryItem.Model.Pinned ? $"Pinned category: {categoryItem.Name}" : $"Unpinned category: {categoryItem.Name}");
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to toggle category pin", "Error");
            }
        }

        public async Task RefreshPinnedItemsAsync()
        {
            try
            {
                _logger?.Debug("RefreshPinnedItemsAsync started");
                var originalPinnedNotesCount = PinnedNotes.Count;
                var originalPinnedCategoriesCount = PinnedCategories.Count;
                
                PinnedNotes.Clear();
                PinnedCategories.Clear();
                
                var pinnedNotesFound = 0;
                var pinnedCategoriesFound = 0;
                
                // Get pin service
                var pinService = GetPinService();
                if (pinService == null)
                {
                    _logger?.Warning("Pin service not available for refresh");
                    return;
                }
                
                // Get all pinned note IDs from the service
                var pinnedNoteIds = await pinService.GetPinnedNoteIdsAsync();
                var pinnedSet = new HashSet<string>(pinnedNoteIds, StringComparer.OrdinalIgnoreCase);
                
                foreach (var category in Categories)
                {
                    // Categories still use existing Model.Pinned
                    if (category.Model.Pinned)
                    {
                        PinnedCategories.Add(category);
                        pinnedCategoriesFound++;
                        _logger?.Debug($"Found pinned category: {category.Name}");
                    }
                        
                    // Notes now use pin service
                    await CollectPinnedNotesFromCategoryAsync(category, pinnedSet);
                }
                
                pinnedNotesFound = PinnedNotes.Count;
                _logger?.Debug($"RefreshPinnedItemsAsync completed: Found {pinnedNotesFound} pinned notes, {pinnedCategoriesFound} pinned categories");
                _logger?.Debug($"Previous counts: {originalPinnedNotesCount} pinned notes, {originalPinnedCategoriesCount} pinned categories");
                
                OnPropertyChanged(nameof(HasPinnedItems));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to refresh pinned items");
            }
        }

        private async Task CollectPinnedNotesFromCategoryAsync(CategoryTreeItem category, HashSet<string> pinnedNoteIds)
        {
            var pinService = GetPinService();
            
            foreach (var note in category.Notes)
            {
                bool isPinned = false;
                
                // First try direct ID match
                if (!string.IsNullOrEmpty(note.Model.Id) && pinnedNoteIds.Contains(note.Model.Id))
                {
                    isPinned = true;
                    _logger?.Debug($"Found pinned note by ID: {note.Title} (ID: {note.Model.Id})");
                }
                // Fallback: check by file path if ID doesn't match
                else if (!string.IsNullOrEmpty(note.Model.FilePath) && pinService != null)
                {
                    try
                    {
                        // Get the correct metadata ID for this file path
                        var metadataManager = _serviceProvider?.GetService<NoteNest.Core.Services.NoteMetadataManager>();
                        if (metadataManager != null)
                        {
                            var tempNote = new NoteNest.Core.Models.NoteModel
                            {
                                FilePath = note.Model.FilePath,
                                Id = note.Model.Id
                            };
                            var metadataId = await metadataManager.GetOrCreateNoteIdAsync(tempNote);
                            
                            if (pinnedNoteIds.Contains(metadataId))
                            {
                                isPinned = true;
                                _logger?.Debug($"Found pinned note by metadata ID: {note.Title} (Tree ID: {note.Model.Id}, Metadata ID: {metadataId})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"Failed to check metadata ID for {note.Title}: {ex.Message}");
                    }
                }
                
                if (isPinned)
                {
                    PinnedNotes.Add(new NoteNest.UI.Services.PinnedNoteItem(note, category.Name));
                }
                else
                {
                    _logger?.Debug($"Note not pinned: {note.Title} (ID: {note.Model.Id})");
                }
            }
            
            foreach (var sub in category.SubCategories)
            {
                // DON'T add subcategories here - they're already handled in the main loop
                // Just recurse to get their notes
                await CollectPinnedNotesFromCategoryAsync(sub, pinnedNoteIds);
            }
        }

        private async Task<string> FindNoteIdByFilePathAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return null;
                
                // Search through all loaded notes to find the one with matching file path
                foreach (var category in Categories)
                {
                    var noteId = FindNoteIdInCategory(category, filePath);
                    if (!string.IsNullOrEmpty(noteId))
                        return noteId;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error finding note ID by file path {filePath}: {ex.Message}");
                return null;
            }
        }

        private string FindNoteIdInCategory(CategoryTreeItem category, string filePath)
        {
            foreach (var note in category.Notes)
            {
                if (string.Equals(note.Model.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return note.Model.Id;
                }
            }
            
            foreach (var sub in category.SubCategories)
            {
                var noteId = FindNoteIdInCategory(sub, filePath);
                if (!string.IsNullOrEmpty(noteId))
                    return noteId;
            }
            
            return null;
        }

        public async Task RenameNoteAsync(NoteTreeItem noteItem, string newName)
        {
            if (noteItem == null || string.IsNullOrWhiteSpace(newName)) return;

            var result = await GetTreeOperationAdapter().RenameNoteAsync(noteItem, newName);
            
            if (!result.Success)
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to rename note", "Name Conflict");
                return;
            }
            
            // Update UI elements
            noteItem.OnPropertyChanged(nameof(NoteTreeItem.Title));
            noteItem.OnPropertyChanged(nameof(NoteTreeItem.FilePath));
            
            // Update open tab if exists
            // Note: Tab should update itself automatically when the underlying NoteModel changes
            
            _stateManager.StatusMessage = result.StatusMessage ?? $"Renamed note to '{newName}'";
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
            
            var noteTitle = noteItem.Title;
            var result = await GetTreeOperationAdapter().DeleteNoteAsync(noteItem);
            
            if (result.Success)
            {
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
            
                _stateManager.StatusMessage = result.StatusMessage ?? $"Deleted '{noteTitle}'";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to delete note", "Error");
            }
        }

        // Expansion state saving now handled automatically by TreeController

        // Removed: OnOpenTabsChanged tracking; tracking handled by workspace service

        public ConfigurationService GetConfigService()
        {
            return _configService;
        }

        public async Task<bool> MoveNoteToCategory(NoteTreeItem noteItem, CategoryTreeItem targetCategory)
        {
            if (noteItem == null || targetCategory == null) return false;
            
            var result = await GetTreeOperationAdapter().MoveNoteAsync(noteItem, targetCategory);
            
            if (result.Success)
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
                // Note: Tab should update itself automatically when the underlying NoteModel changes
                
                _stateManager.StatusMessage = result.StatusMessage ?? $"Moved '{noteItem.Title}' to '{targetCategory.Name}'";
            }
            else
            {
                _dialogService.ShowError(result.ErrorMessage ?? "Failed to move note", "Error");
            }
            
            return result.Success;
        }

        #endregion

        #region Position Migration

        /// <summary>
        /// Migrates note positions for existing notes that don't have positions assigned
        /// Safe to call multiple times - only assigns positions to notes without them
        /// </summary>
        private async Task MigrateNotePositionsIfNeededAsync()
        {
            try
            {
                StatusMessage = "Checking note positions...";
                
                var positionService = _serviceProvider.GetService<NoteNest.Core.Utils.NotePositionService>();
                if (positionService == null)
                {
                    _logger.Warning("NotePositionService not available, skipping position migration");
                    return;
                }
                
                int totalAssigned = 0;
                
                // Process each category to assign positions to notes without them
                foreach (var categoryItem in Categories)
                {
                    var assignedInCategory = await ProcessCategoryForPositionMigration(categoryItem, positionService);
                    totalAssigned += assignedInCategory;
                }
                
                if (totalAssigned > 0)
                {
                    _logger.Info($"Position migration completed: assigned positions to {totalAssigned} notes");
                    StatusMessage = $"Assigned positions to {totalAssigned} notes";
                    await Task.Delay(1500); // Show the message briefly
                }
                else
                {
                    _logger.Debug("Position migration completed: no notes needed position assignment");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to migrate note positions");
                // Don't throw - this is not critical for app functionality
            }
        }
        
        /// <summary>
        /// Recursively processes a category and its subcategories for position migration
        /// </summary>
        private async Task<int> ProcessCategoryForPositionMigration(CategoryTreeItem categoryItem, 
            NoteNest.Core.Utils.NotePositionService positionService)
        {
            int totalAssigned = 0;
            
            try
            {
                // Ensure the category's notes are loaded
                if (!categoryItem.IsLoaded)
                {
                    await categoryItem.LoadChildrenAsync();
                }
                
                // Process notes in this category
                if (categoryItem.Notes?.Count > 0)
                {
                    var noteModels = categoryItem.Notes.Select(ni => ni.Model).ToList();
                    var assignedCount = await positionService.AssignInitialPositionsAsync(noteModels);
                    totalAssigned += assignedCount;
                    
                    if (assignedCount > 0)
                    {
                        _logger.Debug($"Assigned {assignedCount} positions in category '{categoryItem.Name}'");
                    }
                }
                
                // Process subcategories recursively
                if (categoryItem.SubCategories?.Count > 0)
                {
                    foreach (var subCategory in categoryItem.SubCategories)
                    {
                        var subCategoryCount = await ProcessCategoryForPositionMigration(subCategory, positionService);
                        totalAssigned += subCategoryCount;
                    }
                }
                
                return totalAssigned;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to process category '{categoryItem.Name}' for position migration: {ex.Message}");
                // Return whatever we managed to assign before the error
                return totalAssigned;
            }
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
                    }
                    if (_stateManager != null)
                    {
                        _stateManager.PropertyChanged -= OnStateManagerPropertyChanged;
                    }
                    if (_saveManager != null)
                    {
                        _saveManager.ExternalChangeDetected -= OnExternalChangeDetected;
                        _saveManager.SaveCompleted -= OnSaveCompleted;
                    }

                    // Dispose only what was actually created
                    _fileWatcher?.Dispose();
                    _fileSystemEventCoordinator?.Dispose();
                    _tabPersistenceCoordinator?.Dispose();
                    (_workspaceViewModel as IDisposable)?.Dispose();
                    (_workspaceService as IDisposable)?.Dispose();

                    _cancellationTokenSource?.Dispose();
                    // Auto-save timer removed - per-tab timers handle disposal
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