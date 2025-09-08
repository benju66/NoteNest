using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Commands;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels
{
    public class SearchViewModel : ViewModelBase, IDisposable
    {
        private readonly ISearchService _searchService;
        private readonly IAppLogger _logger;
        private DispatcherTimer _debounceTimer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private string _searchQuery = string.Empty;
        private bool _isSearching;
        private bool _hasResults;
        private string _statusText = "Start typing to search...";
        
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

        // Events
        public event EventHandler<SearchResultSelectedEventArgs>? ResultSelected;

        // Commands
        public ICommand SearchCommand { get; private set; }
        public ICommand ClearSearchCommand { get; private set; }
        public ICommand SelectResultCommand { get; private set; }

        public SearchViewModel(ISearchService searchService, IAppLogger logger)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _cancellationTokenSource = new CancellationTokenSource();
            _searchResults = new ObservableCollection<SearchResultViewModel>();
            
            // Fast 200ms debounce for responsive search
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
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
            SelectResultCommand = new RelayCommand<SearchResultViewModel>(OnResultSelected);
        }

        private void OnSearchQueryChanged()
        {
            _debounceTimer.Stop();
            
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                ClearSearch();
                return;
            }
            
            // Start debounce timer
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
                var results = await _searchService.SearchAsync(SearchQuery.Trim(), _cancellationTokenSource.Token);
                
                _logger.Debug($"SearchService returned {results.Count} results");
                
                // Update results on UI thread
                SearchResults.Clear();
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                    _logger.Debug($"  Added result: {result.Title}");
                }
                
                HasResults = SearchResults.Count > 0;
                StatusText = HasResults 
                    ? $"Found {SearchResults.Count} result{(SearchResults.Count == 1 ? "" : "s")}"
                    : "No results found";
                    
                _logger.Debug($"Search completed: '{SearchQuery}' returned {SearchResults.Count} results, HasResults={HasResults}");
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Search operation was cancelled");
                StatusText = "Search cancelled";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Search failed for query: '{SearchQuery}'");
                StatusText = "Search failed";
                HasResults = false;
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void ClearSearch()
        {
            SearchResults.Clear();
            HasResults = false;
            StatusText = "Start typing to search...";
            IsSearching = false;
        }

        private void OnResultSelected(SearchResultViewModel? result)
        {
            if (result == null) return;
            
            try
            {
                _logger.Debug($"Search result selected: {result.Title}");
                ResultSelected?.Invoke(this, new SearchResultSelectedEventArgs(result));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling search result selection");
            }
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
