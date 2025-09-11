using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using ModernWpf.Controls;
using NoteNest.UI.Services;
using NoteNest.UI.Windows;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;
using NoteNest.UI.Services.DragDrop;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Plugins;
using NoteNest.UI.ViewModels;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public static readonly RoutedUICommand ToggleEditorCommand = new RoutedUICommand("ToggleEditor", nameof(ToggleEditorCommand), typeof(MainWindow));
        public static readonly RoutedUICommand ToggleRightPanelCommand = new RoutedUICommand("ToggleRightPanel", nameof(ToggleRightPanelCommand), typeof(MainWindow));
        public static readonly RoutedUICommand FocusSearchCommand = new RoutedUICommand("FocusSearch", nameof(FocusSearchCommand), typeof(MainWindow));

        private bool _isRightPanelVisible;
        private double _lastRightPanelWidth = 320;
        private readonly NoteNest.Core.Services.Logging.IAppLogger _logger;

        public bool IsRightPanelVisible
        {
            get => _isRightPanelVisible;
            set
            {
                if (_isRightPanelVisible == value) return;
                _isRightPanelVisible = value;
                try
                {
                    UpdateRightPanelVisibility(_isRightPanelVisible);
                }
                catch { }
                OnPropertyChanged(nameof(IsRightPanelVisible));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _logger = NoteNest.Core.Services.Logging.AppLogger.Instance;
            UpdateThemeMenuChecks();
            AllowDrop = true;
            try
            {
                // Ensure caption button area is not overlapped by our content
                SourceInitialized += (_, __) => ApplyCaptionButtonsRightInset();
                SizeChanged += (_, __) => ApplyCaptionButtonsRightInset();
                // Recompute on DPI change
                this.DpiChanged += (_, __) => ApplyCaptionButtonsRightInset();
                // Recompute when theme toggles
                ModernWpf.ThemeManager.Current.ActualApplicationThemeChanged += (_, __) => ApplyCaptionButtonsRightInset();
                // Recompute when window state changes (normal/maximized)
                this.StateChanged += (_, __) => ApplyCaptionButtonsRightInset();
            }
            catch { }
            try
            {
                CommandBindings.Add(new CommandBinding(ToggleRightPanelCommand, (s, e) => IsRightPanelVisible = !IsRightPanelVisible));
                InputBindings.Add(new KeyBinding(ToggleRightPanelCommand, new KeyGesture(Key.Oem3, ModifierKeys.Control))); // Ctrl+`
            }
            catch { }

            // Ensure NoteNestPanel uses the same DataContext as the window
            this.Loaded += (sender, e) =>
            {
                if (MainPanel != null && DataContext != null)
                    MainPanel.DataContext = DataContext;

                try
                {
                    var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                    if (config?.Settings != null && FindName("ShowActivityBarMenuItem") is System.Windows.Controls.MenuItem mi)
                    {
                        mi.IsChecked = config.Settings.ShowActivityBar;
                        ActivityBarControl.Visibility = config.Settings.ShowActivityBar
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                        try { ActivityBarControl.Width = Math.Max(36, config.Settings.ActivityBarWidth); } catch { }
                        // Restore last known right panel width and visibility
                        try
                        {
                            if (config.Settings.PluginPanelWidth > 0)
                                _lastRightPanelWidth = config.Settings.PluginPanelWidth;
                        }
                        catch { }
                        // Determine initial visibility based on plugin hosts/content
                        IsRightPanelVisible = PluginPanelContainer.Visibility == Visibility.Visible && (_lastRightPanelWidth > 0);
                        // Restore editor collapsed state
                        if (config.Settings.IsEditorCollapsed)
                        {
                            EditorColumn.Width = new GridLength(0);
                        }
                        try
                        {
                            var pm = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IPluginManager)) as IPluginManager;
                            if (pm != null)
                            {
                                var abvm = new ActivityBarViewModel(pm);
                                abvm.PluginActivated += OnPluginActivated;
                                ActivityBarControl.DataContext = abvm;

                                // Restore last active plugin if any, using preferred slot when available
                                var lastId = config.Settings.LastActivePluginId;
                                if (!string.IsNullOrWhiteSpace(lastId))
                                {
                                    var plugin = pm.GetPlugin(lastId);
                                    if (plugin != null)
                                    {
                                        var preferred = config.Settings.PluginPanelSlotByPluginId.TryGetValue(plugin.Id, out var slot) ? slot : "Primary";
                                        OnPluginActivated(plugin, isSecondary: string.Equals(preferred, "Secondary", StringComparison.OrdinalIgnoreCase));
                                    }
                                }

                                // Restore secondary plugin if split enabled
                                if (config.Settings.RightPanelSplitEnabled)
                                {
                                    var secId = config.Settings.SecondaryActivePluginId;
                                    if (!string.IsNullOrWhiteSpace(secId))
                                    {
                                        var splugin = pm.GetPlugin(secId);
                                        if (splugin != null)
                                        {
                                            EnableRightPanelSplit(true);
                                            OnPluginActivated(splugin, isSecondary:true);
                                            SetRightPanelHeights(config.Settings.RightPanelTopHeight, config.Settings.RightPanelBottomHeight);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // Wire up toast host to service
                try
                {
                    var toast = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ToastNotificationService)) as ToastNotificationService;
                    if (toast != null && ToastHost != null)
                    {
                        ToastHost.DataContext = toast;
                    }
                }
                catch { }

                // Hook errors to toasts with light throttling (5s)
                try
                {
                    var err = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IServiceErrorHandler)) as IServiceErrorHandler;
                    var toast = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ToastNotificationService)) as ToastNotificationService;
                    if (err != null && toast != null)
                    {
                        DateTime lastShown = DateTime.MinValue;
                        err.ErrorOccurred += (s2, args) =>
                        {
                            try
                            {
                                var now = DateTime.UtcNow;
                                if ((now - lastShown).TotalSeconds < 5) return;
                                lastShown = now;
                                var msg = string.IsNullOrWhiteSpace(args?.Context) ? "An error occurred" : args.Context;
                                toast.Error(msg);
                            }
                            catch { }
                        };
                    }
                }
                catch { }
            };
            this.Loaded += async (_, __) =>
            {
                try { await RestoreTreeExpansionOnceAsync(); } catch { }
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); } catch { }
        }

        private void ApplyCaptionButtonsRightInset()
        {
            try
            {
                if (PresentationSource.FromVisual(this) is HwndSource src && CustomTitleBar != null)
                {
                    var hwnd = src.Handle;
                    if (GetCaptionButtonBounds(hwnd, out var bounds))
                    {
                        var dpi = VisualTreeHelper.GetDpi(this);
                        double dpiX = dpi.DpiScaleX;
                        double dpiY = dpi.DpiScaleY;

                        // DWM-aligned insets (device-independent)
                        double rightInset = bounds.Width / dpiX + 6; // small pad from system buttons
                        double topInset = bounds.Top / dpiY;         // caption band top relative to window
                        double captionHeight = Math.Max(0, bounds.Height / dpiY);

                        // Position titlebar exactly over the caption band
                        CustomTitleBar.Margin = new Thickness(0, topInset, rightInset, 0);
                        CustomTitleBar.Height = captionHeight;

                        // Apply a tiny DPI-aware nudge to visually match DWM hover art
                        double visualNudge = Math.Round(1.0 * dpiY) / dpiY; // ~1 DIP adjusted per DPI
                        CustomTitleBar.Margin = new Thickness(CustomTitleBar.Margin.Left, CustomTitleBar.Margin.Top - visualNudge, CustomTitleBar.Margin.Right, CustomTitleBar.Margin.Bottom);

                        // Vertically center children inside the caption band
                        try
                        {
                            if (MainMenu != null)
                            {
                                MainMenu.ClearValue(FrameworkElement.HeightProperty);
                                MainMenu.VerticalAlignment = VerticalAlignment.Center;
                                MainMenu.Margin = new Thickness(MainMenu.Margin.Left, 0, MainMenu.Margin.Right, 0);

                                var style = new Style(typeof(System.Windows.Controls.MenuItem));
                                style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.PaddingProperty, new Thickness(10, 0, 10, 0)));
                                style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.MarginProperty, new Thickness(0)));
                                style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.HeightProperty, captionHeight));
                                style.Setters.Add(new Setter(System.Windows.Controls.MenuItem.VerticalContentAlignmentProperty, VerticalAlignment.Center));
                                MainMenu.ItemContainerStyle = style;

                                // Force normalization for explicitly declared MenuItems as well
                                NormalizeMenuItemHeights(MainMenu, captionHeight);
                            }
                        }
                        catch { }
                        try
                        {
                            if (RightPanelToggle != null)
                            {
                                RightPanelToggle.Height = captionHeight;
                                RightPanelToggle.VerticalAlignment = VerticalAlignment.Center;
                                RightPanelToggle.Padding = new Thickness(0);
                                RightPanelToggle.Margin = new Thickness(RightPanelToggle.Margin.Left, 0, RightPanelToggle.Margin.Right, 0);
                            }
                        }
                        catch { }
                        try
                        {
                            if (AppIcon != null)
                            {
                                AppIcon.VerticalAlignment = VerticalAlignment.Center;
                                // Keep the icon's own size; center it within the caption band
                                AppIcon.Margin = new Thickness(AppIcon.Margin.Left, 0, AppIcon.Margin.Right, 0);
                            }
                        }
                        catch { }

                        return;
                    }
                }
            }
            catch { }
            // Fallback pad (~caption buttons width)
            CustomTitleBar.Margin = new Thickness(0, 0, 140, 0);
        }

        private static bool GetCaptionButtonBounds(IntPtr hwnd, out System.Drawing.Rectangle rect)
        {
            const int DWMWA_CAPTION_BUTTON_BOUNDS = 41;
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out rect, Marshal.SizeOf<System.Drawing.Rectangle>());
            return hr == 0 && rect.Width > 0;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out System.Drawing.Rectangle pvAttribute, int cbAttribute);

        private void UpdateRightPanelVisibility(bool visible)
        {
            var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
            if (visible)
            {
                // Ensure container visible
                PluginPanelContainer.Visibility = Visibility.Visible;
                PluginSplitter.Visibility = Visibility.Visible;
                // Restore width (from config or last cached)
                var target = Math.Max(200, config?.Settings?.PluginPanelWidth ?? _lastRightPanelWidth);
                if (double.IsNaN(target) || target <= 0) target = 320;
                _lastRightPanelWidth = target;
                PluginColumn.Width = new GridLength(target);
            }
            else
            {
                // Cache current width for restore
                var w = PluginColumn.ActualWidth;
                if (!double.IsNaN(w) && w > 0) _lastRightPanelWidth = w;
                PluginSplitter.Visibility = Visibility.Collapsed;
                PluginPanelContainer.Visibility = Visibility.Collapsed;
                PluginColumn.Width = new GridLength(0);
            }
            try
            {
                if (config?.Settings != null)
                {
                    // Persist width when visible
                    if (visible)
                    {
                        config.Settings.PluginPanelWidth = _lastRightPanelWidth;
                    }
                    config.RequestSaveDebounced();
                }
            }
            catch { }
        }

        private void OnPluginActivated(IPlugin plugin) => OnPluginActivated(plugin, false);

        private void OnPluginActivated(IPlugin plugin, bool isSecondary)
        {
            if (plugin == null)
            {
                // Hide panel when no plugin (Explorer) selected
                PluginPanelHostPrimary.Content = null;
                PluginPanelHostPrimary.Visibility = Visibility.Collapsed;
                PluginPanelHostSecondary.Content = null;
                PluginPanelHostSecondary.Visibility = Visibility.Collapsed;
                PluginSplitter.Visibility = Visibility.Collapsed;
                PluginPanelContainer.Visibility = Visibility.Collapsed;
                // Collapse plugin column so editor uses full width
                PluginColumn.Width = new GridLength(0);
                IsRightPanelVisible = false; // sync toggle
                return;
            }
            var panel = plugin.GetPanel();
            if (panel != null)
            {
                if (isSecondary)
                {
                    PluginPanelHostSecondary.Content = panel.Content;
                    PluginPanelHostSecondary.Visibility = Visibility.Visible;
                    InnerPluginSplitter.Visibility = Visibility.Visible;
                }
                else
                {
                    PluginPanelHostPrimary.Content = panel.Content;
                    PluginPanelHostPrimary.Visibility = Visibility.Visible;
                }
                PluginPanelContainer.Visibility = Visibility.Visible;
                PluginSplitter.Visibility = Visibility.Visible;
                // Ensure plugin column has visible width
                try
                {
                    var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                    var targetWidth = (config?.Settings?.PluginPanelWidth ?? 0) > 0 ? config.Settings.PluginPanelWidth : 300;
                    if (PluginColumn.Width.Value <= 0.1)
                    {
                        PluginColumn.Width = new GridLength(targetWidth);
                    }
                    IsRightPanelVisible = true; // sync toggle
                    if (config?.Settings?.CollapseEditorWhenPluginOpens == true)
                    {
                        EditorColumn.Width = new GridLength(0);
                        config.Settings.IsEditorCollapsed = true;
                        config.RequestSaveDebounced();
                    }
                }
                catch { }

                // Persist active plugin id
                try
                {
                    var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                    if (config?.Settings != null)
                    {
                        if (isSecondary)
                            config.Settings.SecondaryActivePluginId = plugin.Id;
                        else
                            config.Settings.LastActivePluginId = plugin.Id;
                        // Persist per-plugin preferred slot
                        config.Settings.PluginPanelSlotByPluginId[plugin.Id] = isSecondary ? "Secondary" : "Primary";
                        config.RequestSaveDebounced();
                    }
                }
                catch { }
            }
        }

        private void PluginSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            try
            {
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (config?.Settings != null)
                {
                    var w = PluginColumn.ActualWidth;
                    if (double.IsNaN(w) || w <= 0) w = 300;
                    config.Settings.PluginPanelWidth = w;
                    _lastRightPanelWidth = w;
                    config.RequestSaveDebounced();
                }
                // update visible flag based on actual width/visibility
                IsRightPanelVisible = PluginPanelContainer.Visibility == Visibility.Visible && PluginColumn.Width.Value > 0.1;
            }
            catch { }
        }

        private void InnerPluginSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            SaveRightPanelHeights();
        }

        private void PluginSplitter_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ToggleEditorCollapsed();
        }

        private void ToggleEditorCollapsed()
        {
            try
            {
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (config?.Settings == null) return;
                var collapsed = EditorColumn.Width.Value <= 0.1;
                if (collapsed)
                {
                    // Expand to stored width or default
                    EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                    config.Settings.IsEditorCollapsed = false;
                }
                else
                {
                    EditorColumn.Width = new GridLength(0);
                    config.Settings.IsEditorCollapsed = true;
                }
                config.RequestSaveDebounced();
            }
            catch { }
        }

        private void EnableRightPanelSplit(bool enable)
        {
            PluginPanelHostSecondary.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
            InnerPluginSplitter.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
            var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
            if (config?.Settings != null)
            {
                config.Settings.RightPanelSplitEnabled = enable;
                config.RequestSaveDebounced();
            }
            // keep toggle in sync if container is visible
            IsRightPanelVisible = PluginPanelContainer.Visibility == Visibility.Visible && PluginColumn.Width.Value > 0.1;
        }

        private void SetRightPanelHeights(double top, double bottom)
        {
            if (top > 0 && bottom > 0)
            {
                PluginPanelContainer.RowDefinitions[0].Height = new GridLength(top, GridUnitType.Star);
                PluginPanelContainer.RowDefinitions[2].Height = new GridLength(bottom, GridUnitType.Star);
            }
            IsRightPanelVisible = PluginPanelContainer.Visibility == Visibility.Visible && PluginColumn.Width.Value > 0.1;
        }

        private void SaveRightPanelHeights()
        {
            try
            {
                var top = PluginPanelContainer.RowDefinitions[0].ActualHeight;
                var bottom = PluginPanelContainer.RowDefinitions[2].ActualHeight;
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (config?.Settings != null)
                {
                    config.Settings.RightPanelTopHeight = top;
                    config.Settings.RightPanelBottomHeight = bottom;
                    config.RequestSaveDebounced();
                }
            }
            catch { }
            IsRightPanelVisible = PluginPanelContainer.Visibility == Visibility.Visible && PluginColumn.Width.Value > 0.1;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                TogglePaneDropHighlight(e.GetPosition(this), true);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                // Spring-load: activate pane under cursor after delay
                var hoveredPaneControl = FindPaneControlAtPoint(e.GetPosition(this));
                if (hoveredPaneControl != null)
                {
                    var spring = (Application.Current as App)?.ServiceProvider?.GetService(typeof(SpringLoadedPaneManager)) as SpringLoadedPaneManager;
                    spring?.BeginHover(hoveredPaneControl);
                    spring!.PaneActivated -= OnPaneActivated;
                    spring.PaneActivated += OnPaneActivated;
                }
                TogglePaneDropHighlight(e.GetPosition(this), true);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        protected override async void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (e.Data.GetDataPresent("NoteNestTab"))
            {
                var tab = e.Data.GetData("NoteNestTab") as ITabItem;
                var sourceWindow = e.Data.GetData("SourceWindow") as Window;
                if (tab == null) return;
                var point = e.GetPosition(this);
                var targetPane = FindPaneAtPoint(point);
                if (targetPane != null)
                {
                    var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                    if (workspace != null)
                    {
                        await workspace.MoveTabToPaneAsync(tab, targetPane);
                        if (sourceWindow is DetachedTabWindow dw)
                        {
                            dw.RemoveTab(tab);
                        }
                        // Window association removed - SaveManager handles note tracking
                    }
                }
                TogglePaneDropHighlight(e.GetPosition(this), false);
                e.Handled = true;
            }
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            TogglePaneDropHighlight(e.GetPosition(this), false);
        }

        private void TogglePaneDropHighlight(Point point, bool on)
        {
            var ctrl = FindPaneControlAtPoint(point) as FrameworkElement;
            var spv = ctrl != null ? FindAncestor<SplitPaneView>(ctrl) : null;
            // Turn off all first
            var root = this;
            TurnAllPaneHighlights(false);
            if (on && spv != null)
            {
                spv.SetDropHighlight(true);
            }
        }

        private void TurnAllPaneHighlights(bool on)
        {
            // Walk visual tree for SplitPaneView and reset
            void ResetHighlights(DependencyObject parent)
            {
                if (parent is not System.Windows.Media.Visual && parent is not System.Windows.Media.Media3D.Visual3D)
                {
                    return;
                }
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is SplitPaneView spv)
                    {
                        spv.SetDropHighlight(on);
                    }
                    ResetHighlights(child);
                }
            }
            ResetHighlights(this);
        }

        private SplitPane FindPaneAtPoint(Point point)
        {
            // Try hit testing for a DraggableTabControl and climb to SplitPaneView
            var element = InputHitTest(point) as DependencyObject;
            while (element != null && element is not DraggableTabControl)
            {
                if (element is not System.Windows.Media.Visual && element is not System.Windows.Media.Media3D.Visual3D)
                {
                    return null;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            if (element is DraggableTabControl dtc)
            {
                var spv = FindAncestor<SplitPaneView>(dtc);
                return spv?.Pane;
            }
            return null;
        }

        private FrameworkElement FindPaneControlAtPoint(Point point)
        {
            var element = InputHitTest(point) as DependencyObject;
            while (element != null && element is not DraggableTabControl)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return element as FrameworkElement;
        }

        private void OnPaneActivated(object? sender, FrameworkElement paneControl)
        {
            var spv = FindAncestor<SplitPaneView>(paneControl);
            var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
            if (spv?.Pane != null && workspace != null)
            {
                workspace.SetActivePane(spv.Pane);
            }
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T t) return t;
                if (current is not System.Windows.Media.Visual && current is not System.Windows.Media.Media3D.Visual3D)
                    return null;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void NewNoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.NewNoteCommand.Execute(null);
        }

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.SaveNoteCommand.Execute(null);
        }

        private void SaveAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.SaveAllCommand.Execute(null);
        }

        private void NewCategoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainPanel?.ViewModel?.NewCategoryCommand.Execute(null);
        }

        public void OpenSettings()
        {
            var viewModel = MainPanel?.ViewModel;
            var configService = viewModel?.GetConfigService();
            
            if (configService == null)
            {
                // Fallback only if MainViewModel isn't available
                var fileSystem = new NoteNest.Core.Services.DefaultFileSystemProvider();
                configService = new NoteNest.Core.Services.ConfigurationService(fileSystem);
            }
            
            var win = new SettingsWindow(configService);
            win.Owner = this;
            win.ShowDialog();
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void ConvertNotesFormatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                var fileSystem = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.IFileSystemProvider)) as NoteNest.Core.Interfaces.IFileSystemProvider;
                var markdown = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IMarkdownService)) as IMarkdownService;
                var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;
                if (config == null || fileSystem == null || markdown == null || logger == null)
                    return;

                // Enhanced dialog window
                var dialog = new FormatMigrationWindow(config, fileSystem, markdown, logger);
                dialog.Owner = this;
                var dlgOk = dialog.ShowDialog();
                if (dlgOk != true) return;

                var options = dialog.Options; // filled by window
                var migration = new FormatMigrationService(fileSystem, markdown, logger);
                var progress = new System.Progress<NoteNest.Core.Services.MigrationProgress>(p =>
                {
                    try { System.Diagnostics.Debug.WriteLine($"[FormatMigration] {p.PercentComplete}% {p.CurrentFile}"); } catch { }
                });
                var migResult = await migration.MigrateAsync(options, progress);
                var ok = migResult.Success;

                var doneDialog = new ContentDialog
                {
                    Title = ok ? "Conversion Complete" : "Conversion Finished with Errors",
                    Content = ok 
                        ? $"Converted {migResult.ConvertedFiles} of {migResult.TotalFiles} files. Skipped {migResult.SkippedFiles}."
                        : $"Converted {migResult.ConvertedFiles} of {migResult.TotalFiles}. Failed {migResult.FailedFiles}. Check logs for details.",
                    PrimaryButtonText = "OK",
                    Owner = this
                };
                await doneDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var err = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.Message,
                    PrimaryButtonText = "OK",
                    Owner = this
                };
                await err.ShowAsync();
            }
        }

        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            FocusSearchBox();
        }

        private void FocusSearchCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                // Find the SmartSearchControl in the visual tree
                var searchControl = FindVisualChild<SmartSearchControl>(MainPanel);
                if (searchControl != null)
                {
                    searchControl.FocusSearchBox();
                    _logger?.Debug("Search box focused via keyboard shortcut");
                }
                else
                {
                    _logger?.Warning("SmartSearchControl not found in visual tree");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to focus search box");
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            // Guard: only Visual/Visual3D are valid for VisualTreeHelper
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }

        private void FocusSearchBox()
        {
            try
            {
                // Focus the search control in the main panel
                if (MainPanel?.SearchControl != null)
                {
                    MainPanel.FocusSearchBox();
                    _logger.Debug("Search box focused via Ctrl+F");
                }
                else
                {
                    _logger.Warning("SearchControl not found in MainPanel");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to focus search box");
            }
        }

        private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Hook up to panel method if present
            // Placeholder per guide; to be implemented in Phase 9
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.Light);
            UpdateThemeMenuChecks();
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.Dark);
            UpdateThemeMenuChecks();
        }

        private void SystemTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetTheme(AppTheme.System);
            UpdateThemeMenuChecks();
        }

        private void ShowActivityBarMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (config?.Settings != null && sender is System.Windows.Controls.MenuItem mi)
                {
                    config.Settings.ShowActivityBar = mi.IsChecked;
                    config.RequestSaveDebounced();
                    ActivityBarControl.Visibility = mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void ToggleEditorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ToggleEditorCollapsed();
        }

        private void ToggleEditorCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleEditorCollapsed();
        }

        private void UpdateThemeMenuChecks()
        {
            var currentTheme = ThemeService.GetSavedTheme();
            LightThemeMenuItem.IsChecked = currentTheme == AppTheme.Light;
            DarkThemeMenuItem.IsChecked = currentTheme == AppTheme.Dark;
            SystemThemeMenuItem.IsChecked = currentTheme == AppTheme.System;
        }

        private async void DocumentationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var guidePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MARKDOWN_GUIDE.md");
                if (!System.IO.File.Exists(guidePath))
                {
                    // try repo root next to exe (dev run)
                    guidePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "MARKDOWN_GUIDE.md");
                    guidePath = System.IO.Path.GetFullPath(guidePath);
                }
                if (System.IO.File.Exists(guidePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = guidePath,
                        UseShellExecute = true
                    });
                    return;
                }
            }
            catch { }

            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = "Documentation",
                Content = "Markdown guide file not found.",
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                Owner = this
            };
            await dialog.ShowAsync();
        }

        private async void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = "About NoteNest",
                Content = "NoteNest v1.0.0\nA modern note-taking application\n\n© 2024 Your Name",
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                Owner = this
            };
            await dialog.ShowAsync();
        }

        private async Task RestoreTreeExpansionOnceAsync()
        {
            var vm = MainPanel?.ViewModel;
            if (vm == null) return;

            for (int i = 0; i < 30; i++)
            {
                if (vm.Categories != null && vm.Categories.Count > 0) break;
                await Task.Delay(100);
            }

            try
            {
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;
                var treeState = new TreeStateService(config, logger);
                var expandedIds = await treeState.LoadExpansionStateAsync();
                if (expandedIds != null && expandedIds.Count > 0)
                {
                    treeState.RestoreExpansionState(vm.Categories, expandedIds);
                }
            }
            catch { }
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            var viewModel = MainPanel.DataContext as MainViewModel;
            if (viewModel == null) return;

            // Branch close behavior based on settings
            bool forceSave = true;
            try
            {
                var cfg = viewModel.GetConfigService();
                forceSave = cfg?.Settings?.ForceSaveOnExit != false; // default true
            }
            catch { }

            if (!forceSave)
            {
                try
                {
                    var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                    var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                    if (workspace?.HasUnsavedChanges == true && closeService != null)
                    {
                        e.Cancel = true;
                        var ok = await closeService.CloseAllTabsWithPromptAsync();
                        if (!ok)
                        {
                            return; // user cancelled
                        }
                        e.Cancel = false; // proceed to shutdown
                    }
                }
                catch { }
            }

            // Save window settings - use fire-and-forget to avoid blocking
            try
            {
                var settings = viewModel.GetConfigService()?.Settings;
                if (settings?.WindowSettings != null)
                {
                    settings.WindowSettings.Width = this.ActualWidth;
                    settings.WindowSettings.Height = this.ActualHeight;
                    settings.WindowSettings.Left = this.Left;
                    settings.WindowSettings.Top = this.Top;
                    settings.WindowSettings.IsMaximized = this.WindowState == WindowState.Maximized;
                    
                    // Request debounced save; final flush happens in App.OnExit
                    viewModel.GetConfigService().RequestSaveDebounced();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preparing settings save during shutdown: {ex.Message}");
            }

            // Persist tree expansion state
            try
            {
                // Expansion state now saved automatically by TreeController
            }
            catch { }

            // Force-save all editors and notes without prompting
            try
            {
                // Flush editors' bindings so latest content is pushed to state
                try
                {
                    // Walk all pane views and flush
                    void FlushSplitPanes(DependencyObject parent)
                    {
                        if (parent is not System.Windows.Media.Visual && parent is not System.Windows.Media.Media3D.Visual3D)
                            return;
                        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                        for (int i = 0; i < count; i++)
                        {
                            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                            if (child is Controls.SplitPaneView spv)
                            {
                                // Content is up-to-date since debouncing was removed
                            }
                            FlushSplitPanes(child);
                        }
                    }
                    FlushSplitPanes(this);
                }
                catch { }

                try
                {
                    // Save all dirty notes via SaveManager
                    var saveManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ISaveManager)) as ISaveManager;
                    if (saveManager != null)
                    {
                        var task = saveManager.SaveAllDirtyAsync();
                        task.Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch { }

                try
                {
                    // Persist session tabs best-effort
                    var persistence = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ITabPersistenceService)) as NoteNest.Core.Services.ITabPersistenceService;
                    var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.IWorkspaceService)) as NoteNest.Core.Interfaces.Services.IWorkspaceService;
                    if (persistence != null && workspace != null)
                    {
                        var activeId = workspace.SelectedTab?.Note?.Id;
                        var embedded = workspace.SelectedTab?.Content;
                        var tabs = workspace.OpenTabs;
                        var task = persistence.SaveAsync(tabs, activeId, embedded);
                        task.Wait(TimeSpan.FromMilliseconds(500));
                    }
                }
                catch { }
            }
            catch { }

            _ = Task.Run(() =>
            {
                try
                {
                    viewModel?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during ViewModel disposal: {ex.Message}");
                }
            });

            try
            {
                // Quick synchronous session save on shutdown (best-effort)
                var persistence = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ITabPersistenceService)) as NoteNest.Core.Services.ITabPersistenceService;
                var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.IWorkspaceService)) as NoteNest.Core.Interfaces.Services.IWorkspaceService;
                if (persistence != null && workspace != null)
                {
                    var activeId = workspace.SelectedTab?.Note?.Id;
                    var embedded = workspace.SelectedTab?.Content;
                    var tabs = workspace.OpenTabs;
                    var task = persistence.SaveAsync(tabs, activeId, embedded);
                    task.Wait(TimeSpan.FromMilliseconds(500));
                }
            }
            catch { }
        }

        private static void NormalizeMenuItemHeights(System.Windows.Controls.ItemsControl itemsRoot, double captionHeight)
        {
            if (itemsRoot == null) return;

            foreach (var obj in itemsRoot.Items)
            {
                if (obj is System.Windows.Controls.MenuItem mi)
                {
                    mi.Height = captionHeight;
                    mi.Padding = new Thickness(10, 0, 10, 0);
                    mi.VerticalContentAlignment = VerticalAlignment.Center;

                    if (mi.HasItems)
                    {
                        NormalizeMenuItemHeights(mi, captionHeight);
                    }
                }
            }
        }


    }
}