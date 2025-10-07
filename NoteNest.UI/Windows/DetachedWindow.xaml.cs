using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NoteNest.UI.ViewModels.Windows;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Detached window for tear-out tab functionality
    /// Contains a single pane with multiple tabs, no tree view
    /// Supports full drag & drop, theme sync, and persistence
    /// </summary>
    public partial class DetachedWindow : Window
    {
        private readonly IAppLogger _logger;
        private DetachedWindowViewModel _viewModel;
        
        public DetachedWindow(IAppLogger logger = null)
        {
            _logger = logger;
            InitializeComponent();
            
            // Subscribe to state changes
            StateChanged += OnWindowStateChanged;
            LocationChanged += OnWindowLocationChanged;
            SizeChanged += OnWindowSizeChanged;
            
            _logger?.Debug($"[DetachedWindow] Created window: {GetHashCode()}");
        }
        
        /// <summary>
        /// Set the ViewModel after construction (DI pattern)
        /// </summary>
        public void SetViewModel(DetachedWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            // Update window title binding
            SetBinding(TitleProperty, new System.Windows.Data.Binding("WindowTitle") 
            { 
                Source = _viewModel 
            });
            
            _logger?.Debug($"[DetachedWindow] ViewModel set: {_viewModel.WindowId}");
        }
        
        #region Title Bar Event Handlers
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                ToggleMaximizeRestore();
            }
            else
            {
                // Single-click to drag
                try 
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore - can happen if mouse is released during drag
                }
            }
        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximizeRestore();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Let ViewModel handle close logic (check for unsaved tabs, etc.)
            _viewModel?.CloseWindowCommand?.Execute(null);
        }
        
        private void ToggleMaximizeRestore()
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
                
            UpdateMaximizeRestoreIcon();
        }
        
        private void UpdateMaximizeRestoreIcon()
        {
            if (MaximizeRestoreIcon != null)
            {
                // Update icon based on window state
                MaximizeRestoreIcon.Data = WindowState == WindowState.Maximized
                    ? System.Windows.Media.Geometry.Parse("M 0,2 H 8 V 10 H 0 Z M 2,0 H 10 V 8 H 8 M 2,0 V 2 M 2,2 H 8")  // Restore icon
                    : System.Windows.Media.Geometry.Parse("M 0,0 H 10 V 10 H 0 Z");  // Maximize icon
                    
                MaximizeRestoreButton.ToolTip = WindowState == WindowState.Maximized 
                    ? "Restore" 
                    : "Maximize";
            }
        }
        
        #endregion
        
        #region Window State Tracking
        
        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeRestoreIcon();
            
            // Notify ViewModel of state change
            _viewModel?.OnWindowStateChanged(WindowState);
            
            _logger?.Debug($"[DetachedWindow] State changed: {WindowState}");
        }
        
        private void OnWindowLocationChanged(object sender, EventArgs e)
        {
            // Notify ViewModel of position change (for persistence)
            _viewModel?.OnWindowBoundsChanged(Left, Top, ActualWidth, ActualHeight);
        }
        
        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Notify ViewModel of size change (for persistence)
            _viewModel?.OnWindowBoundsChanged(Left, Top, ActualWidth, ActualHeight);
        }
        
        #endregion
        
        #region Window Lifecycle
        
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Set initial maximize/restore icon
            UpdateMaximizeRestoreIcon();
            
            _logger?.Debug($"[DetachedWindow] Source initialized");
        }
        
        protected override void OnClosing(CancelEventArgs e)
        {
            // Let ViewModel handle closing validation
            if (_viewModel?.CanCloseWindow() == false)
            {
                e.Cancel = true;
                return;
            }
            
            _logger?.Debug($"[DetachedWindow] Closing window: {_viewModel?.WindowId}");
            
            base.OnClosing(e);
        }
        
        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            StateChanged -= OnWindowStateChanged;
            LocationChanged -= OnWindowLocationChanged;
            SizeChanged -= OnWindowSizeChanged;
            
            // Notify ViewModel of closure
            _viewModel?.OnWindowClosed();
            
            _logger?.Debug($"[DetachedWindow] Window closed: {_viewModel?.WindowId}");
            
            base.OnClosed(e);
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Restore window bounds (for persistence)
        /// </summary>
        public new void RestoreBounds(double left, double top, double width, double height, bool isMaximized)
        {
            try
            {
                // Validate bounds are on screen
                var workingArea = SystemParameters.WorkArea;
                
                // Ensure window is at least partially visible
                left = Math.Max(workingArea.Left - width + 100, 
                              Math.Min(left, workingArea.Right - 100));
                top = Math.Max(workingArea.Top, 
                             Math.Min(top, workingArea.Bottom - 50));
                             
                // Ensure reasonable size
                width = Math.Max(400, Math.Min(width, workingArea.Width));
                height = Math.Max(300, Math.Min(height, workingArea.Height));
                
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                
                if (isMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
                
                _logger?.Debug($"[DetachedWindow] Bounds restored: ({left}, {top}) {width}x{height}, Maximized: {isMaximized}");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[DetachedWindow] Failed to restore bounds, using defaults: {ex.Message}");
                
                // Fallback to safe defaults
                Left = 100;
                Top = 100;
                Width = 800;
                Height = 600;
                WindowState = WindowState.Normal;
            }
        }
        
        /// <summary>
        /// Get current window bounds (for persistence)
        /// </summary>
        public (double Left, double Top, double Width, double Height, bool IsMaximized) GetBounds()
        {
            return (Left, Top, ActualWidth, ActualHeight, WindowState == WindowState.Maximized);
        }
        
        #endregion
    }
}
