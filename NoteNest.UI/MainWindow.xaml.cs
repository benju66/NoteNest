using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.ViewModels;
using System.Collections.Generic;
using ModernWpf.Controls;
using NoteNest.UI.Services;
using NoteNest.UI.Windows;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;
using NoteNest.UI.Services.DragDrop;
using NoteNest.Core.Models;

namespace NoteNest.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            UpdateThemeMenuChecks();
            AllowDrop = true;

            // Ensure NoteNestPanel uses the same DataContext as the window
            this.Loaded += (sender, e) =>
            {
                if (MainPanel != null && DataContext != null)
                    MainPanel.DataContext = DataContext;
            };
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
                        var state = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                        state?.AssociateNoteWithWindow(tab.Note.Id, "main", false);
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

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
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
            if (win.ShowDialog() == true)
            {
                // Settings saved - user will restart if migration occurred
                // No need to reload categories here
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Hook up to panel method if present
            // Placeholder per guide; to be implemented in Phase 9
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

        private void UpdateThemeMenuChecks()
        {
            var currentTheme = ThemeService.GetSavedTheme();
            LightThemeMenuItem.IsChecked = currentTheme == AppTheme.Light;
            DarkThemeMenuItem.IsChecked = currentTheme == AppTheme.Dark;
            SystemThemeMenuItem.IsChecked = currentTheme == AppTheme.System;
        }

        private async void DocumentationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = "Documentation",
                Content = "Visit https://github.com/yourusername/NoteNest for documentation.",
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

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            var viewModel = MainPanel.DataContext as MainViewModel;
            if (viewModel == null) return;

            try
            {
                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.ITabCloseService)) as NoteNest.Core.Interfaces.Services.ITabCloseService;
                if (closeService != null)
                {
                    var workspace = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Interfaces.Services.IWorkspaceService)) as NoteNest.Core.Interfaces.Services.IWorkspaceService;
                    if (workspace?.HasUnsavedChanges == true)
                    {
                        e.Cancel = true;
                        var result = await closeService.CloseAllTabsWithPromptAsync();
                        if (result)
                        {
                            // After closing all, resume shutdown
                            Close();
                            return;
                        }
                        else
                        {
                            // User cancelled
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during close-all prompt: {ex.Message}");
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
        }
    }
}