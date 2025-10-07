using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Windows;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates theme synchronization across main window and all detached windows
    /// Ensures consistent theme application when user changes themes
    /// </summary>
    public interface IMultiWindowThemeCoordinator : IDisposable
    {
        /// <summary>
        /// Initialize theme coordination (subscribe to ThemeService events)
        /// </summary>
        void Initialize();
        
        /// <summary>
        /// Register a detached window for theme coordination
        /// </summary>
        void RegisterDetachedWindow(DetachedWindow window);
        
        /// <summary>
        /// Unregister a detached window from theme coordination
        /// </summary>
        void UnregisterDetachedWindow(DetachedWindow window);
        
        /// <summary>
        /// Apply current theme to a specific window
        /// </summary>
        void ApplyThemeToWindow(Window window, ThemeType theme);
        
        /// <summary>
        /// Apply current theme to all registered windows
        /// </summary>
        void ApplyThemeToAllWindows(ThemeType theme);
    }
    
    public class MultiWindowThemeCoordinator : IMultiWindowThemeCoordinator
    {
        private readonly IThemeService _themeService;
        private readonly IWindowManager _windowManager;
        private readonly IAppLogger _logger;
        private readonly Dispatcher _dispatcher;
        
        private readonly List<WeakReference<DetachedWindow>> _detachedWindows = new();
        private readonly object _windowsLock = new object();
        private bool _isInitialized = false;
        private bool _disposed = false;
        
        public MultiWindowThemeCoordinator(
            IThemeService themeService,
            IWindowManager windowManager,
            IAppLogger logger)
        {
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            
            _logger.Debug("[MultiWindowThemeCoordinator] Created");
        }
        
        public void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                // Subscribe to theme changes
                _themeService.ThemeChanged += OnThemeChanged;
                
                // Subscribe to window lifecycle events
                if (_windowManager != null)
                {
                    _windowManager.WindowCreated += OnDetachedWindowCreated;
                    _windowManager.WindowClosed += OnDetachedWindowClosed;
                }
                
                _isInitialized = true;
                _logger.Info("[MultiWindowThemeCoordinator] Initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Failed to initialize");
                throw;
            }
        }
        
        public void RegisterDetachedWindow(DetachedWindow window)
        {
            if (window == null) return;
            
            try
            {
                lock (_windowsLock)
                {
                    // Remove any dead references first
                    CleanupDeadReferences();
                    
                    // Add new window reference
                    _detachedWindows.Add(new WeakReference<DetachedWindow>(window));
                }
                
                // Apply current theme to the new window
                ApplyThemeToWindow(window, _themeService.CurrentTheme);
                
                _logger.Debug($"[MultiWindowThemeCoordinator] Registered detached window: {window.GetHashCode()}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Failed to register detached window");
            }
        }
        
        public void UnregisterDetachedWindow(DetachedWindow window)
        {
            if (window == null) return;
            
            try
            {
                lock (_windowsLock)
                {
                    // Remove references to this window
                    for (int i = _detachedWindows.Count - 1; i >= 0; i--)
                    {
                        if (_detachedWindows[i].TryGetTarget(out var existingWindow) && 
                            ReferenceEquals(existingWindow, window))
                        {
                            _detachedWindows.RemoveAt(i);
                        }
                        else if (!_detachedWindows[i].TryGetTarget(out _))
                        {
                            // Remove dead reference while we're at it
                            _detachedWindows.RemoveAt(i);
                        }
                    }
                }
                
                _logger.Debug($"[MultiWindowThemeCoordinator] Unregistered detached window: {window.GetHashCode()}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Failed to unregister detached window");
            }
        }
        
        public void ApplyThemeToWindow(Window window, ThemeType theme)
        {
            if (window == null) return;
            
            try
            {
                // Ensure we're on the UI thread
                if (!_dispatcher.CheckAccess())
                {
                    _dispatcher.BeginInvoke(() => ApplyThemeToWindow(window, theme));
                    return;
                }
                
                // Get theme URI
                var themeUri = GetThemeUri(theme);
                
                // Remove existing theme dictionaries from window
                var themeDicts = window.Resources.MergedDictionaries
                    .Where(d => d.Source?.OriginalString?.Contains("/Themes/") == true)
                    .ToList();
                    
                foreach (var dict in themeDicts)
                {
                    window.Resources.MergedDictionaries.Remove(dict);
                }
                
                // Load and apply new theme to window
                var themeDict = new ResourceDictionary { Source = themeUri };
                window.Resources.MergedDictionaries.Add(themeDict);
                
                _logger.Debug($"[MultiWindowThemeCoordinator] Applied theme {theme} to window: {window.GetType().Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[MultiWindowThemeCoordinator] Failed to apply theme {theme} to window");
            }
        }
        
        public void ApplyThemeToAllWindows(ThemeType theme)
        {
            try
            {
                // Apply to main window (handled by ThemeService automatically)
                // No need to apply to Application.Current.MainWindow as ThemeService handles that
                
                // Apply to all detached windows
                lock (_windowsLock)
                {
                    var validWindows = new List<DetachedWindow>();
                    
                    // Get all valid detached windows
                    foreach (var weakRef in _detachedWindows.ToList())
                    {
                        if (weakRef.TryGetTarget(out var window))
                        {
                            validWindows.Add(window);
                        }
                    }
                    
                    // Clean up dead references
                    CleanupDeadReferences();
                    
                    // Apply theme to all valid windows
                    foreach (var window in validWindows)
                    {
                        ApplyThemeToWindow(window, theme);
                    }
                }
                
                _logger.Info($"[MultiWindowThemeCoordinator] Applied theme {theme} to all windows");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[MultiWindowThemeCoordinator] Failed to apply theme {theme} to all windows");
            }
        }
        
        #region Event Handlers
        
        private void OnThemeChanged(object sender, ThemeType newTheme)
        {
            try
            {
                _logger.Info($"[MultiWindowThemeCoordinator] Theme changed to: {newTheme}");
                
                // Apply new theme to all detached windows
                // (Main window is already handled by ThemeService)
                ApplyThemeToAllWindows(newTheme);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Error handling theme change");
            }
        }
        
        private void OnDetachedWindowCreated(object sender, ViewModels.Windows.DetachedWindowViewModel windowViewModel)
        {
            try
            {
                // Find the actual WPF window for this ViewModel
                // This integration will be completed in Phase 7
                var wpfWindows = System.Windows.Application.Current.Windows.OfType<DetachedWindow>();
                var detachedWindow = wpfWindows.FirstOrDefault(w => 
                    (w.DataContext as ViewModels.Windows.DetachedWindowViewModel)?.WindowId == windowViewModel.WindowId);
                
                if (detachedWindow != null)
                {
                    RegisterDetachedWindow(detachedWindow);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Error handling window creation");
            }
        }
        
        private void OnDetachedWindowClosed(object sender, ViewModels.Windows.DetachedWindowViewModel windowViewModel)
        {
            try
            {
                // Window is already closing/closed, so we just clean up references
                // The UnregisterDetachedWindow will be called by the window itself during closure
                lock (_windowsLock)
                {
                    CleanupDeadReferences();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiWindowThemeCoordinator] Error handling window closure");
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private void CleanupDeadReferences()
        {
            // Remove weak references that no longer have valid targets
            for (int i = _detachedWindows.Count - 1; i >= 0; i--)
            {
                if (!_detachedWindows[i].TryGetTarget(out _))
                {
                    _detachedWindows.RemoveAt(i);
                }
            }
        }
        
        private Uri GetThemeUri(ThemeType theme)
        {
            // Handle System theme by detecting OS preference (same logic as ThemeService)
            if (theme == ThemeType.System)
            {
                theme = ThemeType.Light; // Default for now
            }
            
            return theme switch
            {
                ThemeType.Light => new Uri("pack://application:,,,/Resources/Themes/LightTheme.xaml"),
                ThemeType.Dark => new Uri("pack://application:,,,/Resources/Themes/DarkTheme.xaml"),
                ThemeType.SolarizedLight => new Uri("pack://application:,,,/Resources/Themes/SolarizedLightTheme.xaml"),
                ThemeType.SolarizedDark => new Uri("pack://application:,,,/Resources/Themes/SolarizedDarkTheme.xaml"),
                _ => new Uri("pack://application:,,,/Resources/Themes/LightTheme.xaml")
            };
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                // Unsubscribe from events
                if (_themeService != null)
                {
                    _themeService.ThemeChanged -= OnThemeChanged;
                }
                
                if (_windowManager != null)
                {
                    _windowManager.WindowCreated -= OnDetachedWindowCreated;
                    _windowManager.WindowClosed -= OnDetachedWindowClosed;
                }
                
                // Clear window references
                lock (_windowsLock)
                {
                    _detachedWindows.Clear();
                }
                
                _logger?.Debug("[MultiWindowThemeCoordinator] Disposed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[MultiWindowThemeCoordinator] Error during disposal");
            }
            finally
            {
                _disposed = true;
            }
        }
        
        #endregion
    }
}
