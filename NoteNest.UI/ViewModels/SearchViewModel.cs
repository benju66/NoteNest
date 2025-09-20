using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Commands;
using NoteNest.UI.Services;
using NoteNest.UI.Interfaces;

namespace NoteNest.UI.ViewModels
{
    public class SearchViewModel : ViewModelBase, IDisposable
    {
        private readonly NoteNest.UI.Interfaces.ISearchService _searchService;
        private readonly IWorkspaceService _workspaceService;
        private readonly NoteService _noteService;
        private readonly IAppLogger _logger;
        private DispatcherTimer _debounceTimer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private string _searchQuery = string.Empty;
        private bool _isSearching;
        private bool _hasResults;
        private bool _showDropdown;
        private string _statusText = "Type to search...";
        private SearchResultViewModel? _selectedResult;
        
        // Collections
        private ObservableCollection<SearchResultViewModel> _searchResults;
        
        // Properties
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    OnPropertyChanged(nameof(ShowNoResults));
                    OnPropertyChanged(nameof(HasText));
                    OnSearchQueryChanged();
                }
            }
        }
        
        public ObservableCollection<SearchResultViewModel> SearchResults
        {
            get => _searchResults;
            private set => SetProperty(ref _searchResults, value);
        }
        
        
        public bool IsSearching
        {
            get => _isSearching;
            private set => SetProperty(ref _isSearching, value);
        }
        
        public bool HasResults
        {
            get => _hasResults;
            private set 
            { 
                if (SetProperty(ref _hasResults, value))
                {
                    OnPropertyChanged(nameof(ShowNoResults));
                }
            }
        }
        
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }
        
        public bool ShowNoResults => !IsSearching && !HasResults && !string.IsNullOrWhiteSpace(SearchQuery) && SearchQuery.Length > 2;

        // Add HasText property for clear button visibility
        public bool HasText => !string.IsNullOrWhiteSpace(SearchQuery);
        
        // ADD: Property for dropdown visibility
        public bool ShowDropdown
        {
            get => _showDropdown;
            set => SetProperty(ref _showDropdown, value);
        }
        
        // ADD: Property for keyboard-selected result
        public SearchResultViewModel? SelectedResult
        {
            get => _selectedResult;
            set => SetProperty(ref _selectedResult, value);
        }

        // Events
        public event EventHandler<SearchResultSelectedEventArgs>? ResultSelected;

        // Commands
        public ICommand SearchCommand { get; private set; }
        public ICommand ClearSearchCommand { get; private set; }
        public ICommand SelectResultCommand { get; private set; }
        
        // ADD: Commands for keyboard navigation
        public ICommand NavigateUpCommand { get; private set; }
        public ICommand NavigateDownCommand { get; private set; }
        public ICommand OpenSelectedCommand { get; private set; }

        public SearchViewModel(
            NoteNest.UI.Interfaces.ISearchService searchService,
            IWorkspaceService workspaceService,
            NoteService noteService,
            IAppLogger logger)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _cancellationTokenSource = new CancellationTokenSource();
            _searchResults = new ObservableCollection<SearchResultViewModel>();
            
            // 300ms debounce for smooth typing (industry standard)
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;
            
            InitializeCommands();
            
            // Initialize collections and ensure binding works
            SearchResults = _searchResults;
            
            _logger.Debug("SearchViewModel initialized successfully");
        }

        private void InitializeCommands()
        {
            SearchCommand = new AsyncRelayCommand(async _ => await PerformSearchAsync());
            ClearSearchCommand = new RelayCommand(_ => ClearSearch());
            SelectResultCommand = new RelayCommand<SearchResultViewModel>(OpenSelectedResult);
            
            // ADD: Commands for keyboard navigation
            NavigateUpCommand = new RelayCommand(_ => NavigateResults(-1));
            NavigateDownCommand = new RelayCommand(_ => NavigateResults(1));
            OpenSelectedCommand = new RelayCommand(_ => OpenSelectedResult(SelectedResult));
        }

        private void OnSearchQueryChanged()
        {
            _debounceTimer.Stop();
            
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                ClearSearch();
                return;
            }
            
            // Start debounce timer for real search
            _debounceTimer.Start();
        }

        private async void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            await PerformSearchAsync();
        }

        private async Task PerformSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                ClearSearch();
                return;
            }

            IsSearching = true;
            StatusText = "Searching...";
            
            try
            {
                _logger.Debug($"PerformSearchAsync calling SearchService with query: '{SearchQuery.Trim()}'");
                _logger.Debug($"SearchService.IsIndexReady: {_searchService.IsIndexReady}");
                
                var results = await _searchService.SearchAsync(SearchQuery.Trim(), _cancellationTokenSource.Token);
                
                _logger.Debug($"SearchService returned {results.Count} results");
                if (results.Count > 0)
                {
                    foreach (var result in results.Take(3)) // Log first 3 results
                    {
                        _logger.Debug($"Result: '{result.Title}' at '{result.FilePath}' with preview: '{result.Preview?.Substring(0, Math.Min(50, result.Preview?.Length ?? 0))}...'");
                    }
                }
                
                // Update results on UI thread
                SearchResults.Clear();
                SelectedResult = null;  // Reset selection
                
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }
                
                // Auto-select first result for keyboard navigation
                if (SearchResults.Any())
                {
                    SelectedResult = SearchResults.First();
                }
                
                HasResults = SearchResults.Count > 0;
                ShowDropdown = HasResults;  // Show dropdown when results exist
                
                StatusText = HasResults 
                    ? $"Found {SearchResults.Count} result{(SearchResults.Count == 1 ? "" : "s")}"
                    : "No results found";
                    
                _logger.Debug($"Search completed: {SearchResults.Count} results, ShowDropdown={ShowDropdown}, HasResults={HasResults}");
                
                // Force property change notifications
                OnPropertyChanged(nameof(ShowDropdown));
                OnPropertyChanged(nameof(HasResults));
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Search operation was cancelled");
                StatusText = "Search cancelled";
                ShowDropdown = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Search failed for query: '{SearchQuery}'");
                StatusText = "Search failed";
                HasResults = false;
                ShowDropdown = false;
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void ClearSearch()
        {
            SearchResults.Clear();
            SelectedResult = null;
            HasResults = false;
            ShowDropdown = false;
            StatusText = "Type to search...";
            IsSearching = false;
            SearchQuery = string.Empty;  // Clear the search box too
        }

        // Navigate through results with arrow keys
        private void NavigateResults(int direction)
        {
            if (!HasResults || SearchResults.Count == 0) return;
            
            var currentIndex = SelectedResult != null ? SearchResults.IndexOf(SelectedResult) : -1;
            var newIndex = currentIndex + direction;
            
            // Wrap around
            if (newIndex < 0) newIndex = SearchResults.Count - 1;
            if (newIndex >= SearchResults.Count) newIndex = 0;
            
            SelectedResult = SearchResults[newIndex];
        }

        // Open the selected search result
        private async void OpenSelectedResult(SearchResultViewModel? result)
        {
            if (result == null) return;
            
            _logger.Debug($"Opening search result: {result.Title} at {result.FilePath}");
            
            try
            {
                // Load the note
                var note = await _noteService.LoadNoteAsync(result.FilePath);
                if (note != null)
                {
                    // Open in workspace
                    await _workspaceService.OpenNoteAsync(note);
                    
                    // Clear search after successful open
                    ClearSearch();
                    ShowDropdown = false;
                    
                    _logger.Info($"Opened note from search: {result.Title}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to open search result: {result.FilePath}");
                StatusText = "Failed to open note";
            }
        }

        // Handle escape key
        public void CloseDropdown()
        {
            ShowDropdown = false;
            SelectedResult = null;
        }

        public void Dispose()
        {
            try
            {
                _debounceTimer?.Stop();
                _debounceTimer = null;
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                
                _logger.Debug("SearchViewModel disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during SearchViewModel disposal");
            }
        }
    }

    public class SearchResultSelectedEventArgs : EventArgs
    {
        public SearchResultViewModel Result { get; }

        public SearchResultSelectedEventArgs(SearchResultViewModel result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }
    }
}
