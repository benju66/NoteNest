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

        private void OnSuggestionChosen(object sender, AutoSuggestBoxSuggestionChosenEventArgs e)
        {
            try
            {
                if (e.SelectedItem is string suggestion)
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
                    // The binding will handle updating the ViewModel
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling text changed");
            }
        }

        // Result click handling
        private void OnResultClicked(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is SearchResultViewModel result)
                {
                    _logger.Debug($"Search result clicked: {result.Title}");
                    
                    // Trigger the selection command
                    if (ViewModel?.SelectResultCommand?.CanExecute(result) == true)
                    {
                        ViewModel.SelectResultCommand.Execute(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling result click");
            }
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
