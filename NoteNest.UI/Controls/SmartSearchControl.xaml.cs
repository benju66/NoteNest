using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Controls
{
    public partial class SmartSearchControl : UserControl
    {
        private readonly IAppLogger _logger;
        private bool _suppressFocusChange = false;

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
            _logger = AppLogger.Instance;
            
            // Set up data context binding
            DataContextChanged += OnDataContextChanged;
            
            // Handle popup closing when clicking outside
            AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, 
                      new MouseButtonEventHandler(OnMouseDownOutsidePopup), true);
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
            _logger.Debug($"SmartSearchControl ViewModel changed: old={oldViewModel?.GetType().Name}, new={newViewModel?.GetType().Name}");
            
            if (oldViewModel != null)
            {
                oldViewModel.ResultSelected -= OnResultSelected;
            }

            if (newViewModel != null)
            {
                newViewModel.ResultSelected += OnResultSelected;
                DataContext = newViewModel;
                _logger.Debug($"SmartSearchControl DataContext set to SearchViewModel");
            }
            else
            {
                _logger.Warning("SmartSearchControl ViewModel set to null!");
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is SearchViewModel viewModel)
            {
                ViewModel = viewModel;
            }
        }

        private void OnResultSelected(object sender, SearchResultSelectedEventArgs e)
            {
                ResultSelected?.Invoke(this, e);
            _logger.Debug($"Search result selected: {e.Result.Title}");
            
            // Close popup and clear search
            if (ViewModel != null)
            {
                ViewModel.ShowDropdown = false;
                ViewModel.SearchQuery = string.Empty;
            }
            SearchTextBox.Focus();
        }

        // TextBox event handlers
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
            {
                switch (e.Key)
                {
                    case Key.Down:
                    // Move focus to results list
                    if (ResultsList.Items.Count > 0)
                    {
                        ResultsPopup.IsOpen = true;
                        ResultsList.Focus();
                        if (ResultsList.SelectedIndex < 0)
                        {
                            ResultsList.SelectedIndex = 0;
                        }
                            e.Handled = true;
                        }
                        break;
                        
                    case Key.Enter:
                        // Open selected result
                    if (ViewModel?.SelectedResult != null)
                        {
                        ViewModel.OpenSelectedCommand?.Execute(null);
                            e.Handled = true;
                        }
                        break;
                        
                    case Key.Escape:
                    // Clear search
                    if (!string.IsNullOrEmpty(SearchTextBox.Text))
                        {
                        ClearSearch();
                            e.Handled = true;
                        }
                    break;
            }
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Show dropdown if we have results
            if (ViewModel?.HasResults == true)
            {
                _suppressFocusChange = true;
                ResultsPopup.IsOpen = true;
                _suppressFocusChange = false;
            }
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressFocusChange) return;
            
            // Delay closing to allow click events to process
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!SearchTextBox.IsFocused && !ResultsList.IsFocused && !ResultsPopup.IsKeyboardFocusWithin)
                {
                    ResultsPopup.IsOpen = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // Results list event handlers
        private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    // Open selected result
                    if (ResultsList.SelectedItem is SearchResultViewModel result)
                    {
                        ViewModel?.SelectResultCommand?.Execute(result);
                            e.Handled = true;
                        }
                        break;
                        
                case Key.Escape:
                    // Return focus to search box
                    ResultsPopup.IsOpen = false;
                    SearchTextBox.Focus();
                    e.Handled = true;
                    break;
                    
                case Key.Up:
                    // Return to search box if at top
                    if (ResultsList.SelectedIndex == 0)
                    {
                        SearchTextBox.Focus();
                            e.Handled = true;
                        }
                        break;
                }
            }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Open the double-clicked result
            if (ResultsList.SelectedItem is SearchResultViewModel result)
            {
                ViewModel?.SelectResultCommand?.Execute(result);
            }
        }

        // Clear button handler
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSearch();
        }

        private void ClearSearch()
            {
                if (ViewModel != null)
                {
                    ViewModel.SearchQuery = string.Empty;
                ViewModel.ShowDropdown = false;
            }
            SearchTextBox.Focus();
        }

        // Handle clicking outside the popup
        private void OnMouseDownOutsidePopup(object sender, MouseButtonEventArgs e)
        {
            if (ResultsPopup.IsOpen)
            {
                // Check if click is outside both the search box and popup
                var hitTest = VisualTreeHelper.HitTest(this, e.GetPosition(this));
                if (hitTest == null || !IsDescendantOf(hitTest.VisualHit, ResultsPopup.Child))
                {
                    ResultsPopup.IsOpen = false;
                }
            }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null) return false;
            
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // Public methods for external control
        public void FocusSearchBox()
        {
            SearchTextBox?.Focus();
            Keyboard.Focus(SearchTextBox);
        }

        public void ClearSearchBox()
        {
            ClearSearch();
        }
    }
}