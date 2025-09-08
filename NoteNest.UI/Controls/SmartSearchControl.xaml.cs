using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ModernWpf.Controls;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Controls
{
    public partial class SmartSearchControl : UserControl
    {
        private readonly IAppLogger _logger;

        // Dependency property for ViewModel
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SearchViewModel),
                typeof(SmartSearchControl),
                new PropertyMetadata(null, OnViewModelChanged));

        public SearchViewModel? ViewModel
        {
            get => (SearchViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        // Event for result selection
        public event EventHandler<SearchResultSelectedEventArgs>? ResultSelected;

        public SmartSearchControl()
        {
            InitializeComponent();
            _logger = AppLogger.Instance; // Fallback logger
            
            // Set up data context binding
            DataContextChanged += OnDataContextChanged;
        }

        public SmartSearchControl(IAppLogger logger) : this()
        {
            _logger = logger ?? AppLogger.Instance;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SmartSearchControl control)
            {
                control.OnViewModelChanged(e.OldValue as SearchViewModel, e.NewValue as SearchViewModel);
            }
        }

        private void OnViewModelChanged(SearchViewModel? oldViewModel, SearchViewModel? newViewModel)
        {
            _logger.Debug($"SmartSearchControl ViewModel changing from {oldViewModel?.GetType().Name ?? "null"} to {newViewModel?.GetType().Name ?? "null"}");
            
            // Unsubscribe from old ViewModel
            if (oldViewModel != null)
            {
                oldViewModel.ResultSelected -= OnViewModelResultSelected;
            }

            // Subscribe to new ViewModel
            if (newViewModel != null)
            {
                newViewModel.ResultSelected += OnViewModelResultSelected;
                DataContext = newViewModel;
                _logger.Debug($"SmartSearchControl DataContext set to SearchViewModel");
            }
            else
            {
                _logger.Warning("SmartSearchControl ViewModel set to null");
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Auto-wire ViewModel if DataContext is SearchViewModel
            if (e.NewValue is SearchViewModel viewModel && ViewModel != viewModel)
            {
                ViewModel = viewModel;
            }
        }

        private void OnViewModelResultSelected(object? sender, SearchResultSelectedEventArgs e)
        {
            try
            {
                // Forward the event to external subscribers
                ResultSelected?.Invoke(this, e);
                _logger.Debug($"Search result forwarded: {e.Result.Title}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error forwarding search result selection");
            }
        }

        // AutoSuggestBox event handlers
        private void OnQuerySubmitted(object sender, AutoSuggestBoxQuerySubmittedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.QueryText))
                    return;

                _logger.Debug($"Search query submitted: {e.QueryText}");
                
                // The SearchViewModel will handle the actual search via binding
                // This event is just for logging/tracking
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling query submitted");
            }
        }

        private bool _isNavigatingWithArrows = false;
        
        private void OnSuggestionChosen(object sender, AutoSuggestBoxSuggestionChosenEventArgs e)
        {
            try
            {
                // Ignore selection if navigating with arrow keys
                if (_isNavigatingWithArrows)
                {
                    _logger.Debug("Ignoring suggestion chosen during arrow navigation");
                    return;
                }
                
                if (e.SelectedItem is SearchResultViewModel result)
                {
                    _logger.Debug($"Search result chosen: {result.Title}");
                    
                    // Trigger the result selection
                    if (ViewModel?.SelectResultCommand?.CanExecute(result) == true)
                    {
                        ViewModel.SelectResultCommand.Execute(result);
                    }
                }
                else if (e.SelectedItem is string suggestion)
                {
                    _logger.Debug($"Search suggestion chosen: {suggestion}");
                    
                    // Update the search query to the chosen suggestion
                    if (ViewModel != null)
                    {
                        ViewModel.SearchQuery = suggestion;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling suggestion chosen");
            }
        }

        private void OnTextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            try
            {
                // Handle user input vs programmatic changes
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    _logger.Debug($"Search text changed by user: {SearchBox.Text}");
                    
                    // Reset arrow navigation when user types
                    _isNavigatingWithArrows = false;
                    
                    if (ViewModel == null)
                    {
                        _logger.Warning("TextChanged but ViewModel is null!");
                    }
                    else
                    {
                        _logger.Debug($"ViewModel available, SearchQuery = '{ViewModel.SearchQuery}'");
                    }
                    
                    // The binding will handle updating the ViewModel
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling text changed");
            }
        }
        
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Handle arrow keys to navigate without selecting
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    // Set flag to prevent selection when navigating with arrows
                    _isNavigatingWithArrows = true;
                    _logger.Debug($"Arrow key pressed: {e.Key}, preventing auto-selection");
                    
                    // Don't mark as handled - let it navigate the dropdown
                }
                else if (e.Key == Key.Enter)
                {
                    // Reset flag on Enter - allow selection
                    _isNavigatingWithArrows = false;
                }
                else if (e.Key == Key.Escape)
                {
                    // Close the dropdown and clear focus
                    SearchBox.IsSuggestionListOpen = false;
                    _isNavigatingWithArrows = false;
                    e.Handled = true;
                }
                else
                {
                    // Reset flag for any other key
                    _isNavigatingWithArrows = false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling preview key down");
            }
        }
        
        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Reset flag when clicking - allow selection
            _isNavigatingWithArrows = false;
        }


        // Public methods for external control
        public void FocusSearchBox()
        {
            try
            {
                SearchBox?.Focus();
                Keyboard.Focus(SearchBox);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error focusing search box");
            }
        }

        public void ClearSearch()
        {
            try
            {
                if (ViewModel != null)
                {
                    ViewModel.SearchQuery = string.Empty;
                }
                SearchBox?.Focus();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error clearing search");
            }
        }

        // Keyboard shortcuts
        protected override void OnKeyDown(KeyEventArgs e)
        {
            try
            {
                // Escape to clear search
                if (e.Key == Key.Escape)
                {
                    ClearSearch();
                    e.Handled = true;
                    return;
                }

                // Enter to search (if not already handled by AutoSuggestBox)
                if (e.Key == Key.Enter && ViewModel?.SearchCommand?.CanExecute(null) == true)
                {
                    ViewModel.SearchCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling key down");
            }

            base.OnKeyDown(e);
        }
    }
}
