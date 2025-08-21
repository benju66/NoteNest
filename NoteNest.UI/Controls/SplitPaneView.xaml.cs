using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;

namespace NoteNest.UI.Controls
{
    public partial class SplitPaneView : UserControl
    {
        public SplitPane? Pane { get; private set; }
        private System.Windows.Threading.DispatcherTimer? _idleSaveTimer;
        
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool),
                typeof(SplitPaneView), new PropertyMetadata(false, OnIsActiveChanged));
        
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }
        
        public SplitPaneView()
        {
            InitializeComponent();
        }
        
        public void BindToPane(SplitPane pane)
        {
            // Unbind from previous pane if any
            if (Pane != null && Pane.Tabs is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnTabsCollectionChanged;
            }
            
            Pane = pane;
            PaneTabControl.ItemsSource = pane.Tabs;
            
            // Subscribe to collection changes
            if (pane.Tabs is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnTabsCollectionChanged;
            }
            
            // Set initial selection
            if (pane.SelectedTab != null)
            {
                PaneTabControl.SelectedItem = pane.SelectedTab;
            }
            else if (pane.Tabs.Count > 0)
            {
                PaneTabControl.SelectedItem = pane.Tabs[0];
                pane.SelectedTab = pane.Tabs[0];
            }
            
            IsActive = pane.IsActive;
            UpdateEmptyState();
        }

        private void OnTabsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();

            // If a new tab was added, auto-select it so content loads without extra click
            if (Pane != null && e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null && e.NewItems.Count > 0)
            {
                var newTab = e.NewItems[e.NewItems.Count - 1] as ITabItem;
                if (newTab != null)
                {
                    Pane.SelectedTab = newTab;
                    PaneTabControl.SelectedItem = newTab;
                    // Content loading handled by XAML binding - no manual loading needed
                    return;
                }
            }
            
            // Auto-select first tab if none selected
            if (Pane != null && Pane.SelectedTab == null && Pane.Tabs.Count > 0)
            {
                Pane.SelectedTab = Pane.Tabs[0];
                PaneTabControl.SelectedItem = Pane.Tabs[0];
            }
        }
        
        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SplitPaneView view)
            {
                view.UpdateVisualState();
            }
        }
        
        private void UpdateVisualState()
        {
            PaneBorder.BorderBrush = IsActive 
                ? new SolidColorBrush(Colors.DodgerBlue)
                : new SolidColorBrush(Colors.LightGray);
        }
        
        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem newTab)
            {
                // Auto-save previous tab if it was dirty
                var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                var state = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                
                // Find the previously selected tab from the old selection
                ITabItem? oldTab = null;
                if (e.RemovedItems?.Count > 0 && e.RemovedItems[0] is ITabItem removedTab)
                {
                    oldTab = removedTab;
                }
                
                // Auto-save old tab if it was dirty
                if (oldTab != null && oldTab.IsDirty && state != null)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] Tab switch auto-save START from={oldTab?.Title} to={newTab?.Title} at={DateTime.Now:HH:mm:ss.fff}");
                        // Flush binding from editor to Content (which updates state via NoteTabItem setter)
                        try
                        {
                            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(oldTab) as TabItem;
                            var presenter = FindVisualChild<ContentPresenter>(container);
                            var editor = FindVisualChild<SmartTextEditor>(presenter);
                            var binding = editor?.GetBindingExpression(TextBox.TextProperty);
                            binding?.UpdateSource();
                        }
                        catch (Exception ex)
                        {
                            try { var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger; logger?.Warning($"Failed to flush binding on tab switch: {ex.Message}"); } catch { }
                        }
                        // Save using state service
                        var result = await state.SaveNoteAsync(oldTab.Note.Id);
                        System.Diagnostics.Debug.WriteLine($"[UI] Tab switch auto-save END noteId={oldTab?.Note?.Id} success={result?.Success} at={DateTime.Now:HH:mm:ss.fff}");
                        try
                        {
                            var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;
                            logger?.Info($"Tab switch auto-saved: {oldTab?.Title}");
                        }
                        catch { }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][ERROR] Tab switch auto-save failed: {ex.Message}"); /* continue */ }
                }

                // Update pane and workspace state
                Pane.SelectedTab = newTab;
                if (workspaceService != null)
                {
                    workspaceService.SelectedTab = newTab;
                }
                
                // Sync note dirty flag with tab (for tree view indicator)
                try
                {
                    if (newTab.Note != null)
                    {
                        newTab.Note.IsDirty = newTab.IsDirty;
                        try
                        {
                            var presenterNew = FindVisualChild<ContentPresenter>(PaneTabControl.ItemContainerGenerator.ContainerFromItem(newTab) as TabItem);
                            var editorNew = FindVisualChild<SmartTextEditor>(presenterNew);
                            var shownLen = editorNew?.Text?.Length ?? -1;
                            var stateNew = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                            var stateLen = (stateNew != null && stateNew.OpenNotes.TryGetValue(newTab.Note.Id, out var wn2)) ? (wn2.CurrentContent?.Length ?? 0) : -1;
                            System.Diagnostics.Debug.WriteLine($"[UI] Switched TO tab id={newTab.Note.Id} shownLen={shownLen} stateLen={stateLen}");
                        }
                        catch (Exception ex)
                        {
                            try { var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger; logger?.Warning($"Failed to inspect editor on tab switch: {ex.Message}"); } catch { }
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][WARN] Sync dirty flag failed: {ex.Message}"); }
            }
        }
        
        private void TabControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // Notify workspace that this pane is now active
            if (Pane != null)
            {
                var mainWindow = Application.Current.MainWindow;
                var panel = FindNoteNestPanel(mainWindow);
                if (panel != null)
                {
                    var splitWorkspace = panel.FindName("SplitWorkspace") as SplitWorkspace;
                    splitWorkspace?.SetActivePane(Pane);
                }
            }
        }

        private async void SmartEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var app = Application.Current as App;
            var configService = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
            if (configService?.Settings?.AutoSaveIdleMs > 0)
            {
                _idleSaveTimer ??= new System.Windows.Threading.DispatcherTimer();
                _idleSaveTimer.Interval = TimeSpan.FromMilliseconds(configService.Settings.AutoSaveIdleMs);
                _idleSaveTimer.Tick -= IdleSaveTimer_Tick;
                _idleSaveTimer.Tick += IdleSaveTimer_Tick;
                _idleSaveTimer.Stop();
                _idleSaveTimer.Start();
            }
            // Do not push content here; rely on binding to NoteTabItem.Content to update state.
            try
            {
                var workspaceService = app?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                var tab = Pane?.SelectedTab ?? workspaceService?.SelectedTab;
                var state = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                if (tab?.Note != null && state != null && !state.OpenNotes.ContainsKey(tab.Note.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"[UI] TextChanged ignored for untracked noteId={tab.Note.Id}");
                }
            }
            catch (Exception ex)
            {
                try { var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger; logger?.Warning($"TextChanged inspection failed: {ex.Message}"); } catch { }
            }
        }

        private async void IdleSaveTimer_Tick(object? sender, EventArgs e)
        {
            _idleSaveTimer?.Stop();
            try
            {
                var app = Application.Current as App;
                var workspaceService = app?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                var state = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                var tab = Pane?.SelectedTab ?? workspaceService?.SelectedTab;
                
                if (tab != null && tab.IsDirty && state != null)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] IdleSave START tab={tab?.Title} at={DateTime.Now:HH:mm:ss.fff}");
                        // Flush binding from editor to Content
                        try
                        {
                            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
                            var presenter = FindVisualChild<ContentPresenter>(container);
                            var editor = FindVisualChild<SmartTextEditor>(presenter);
                            var binding = editor?.GetBindingExpression(TextBox.TextProperty);
                            binding?.UpdateSource();
                        }
                        catch (Exception ex)
                        {
                            try { var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger; logger?.Warning($"Failed to flush binding on idle save: {ex.Message}"); } catch { }
                        }
                        // Save
                        var result = await state.SaveNoteAsync(tab.Note.Id);
                        System.Diagnostics.Debug.WriteLine($"[UI] IdleSave END noteId={tab?.Note?.Id} success={result?.Success} at={DateTime.Now:HH:mm:ss.fff}");
                        try
                        {
                            var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;
                            if (result?.Success == true) logger?.Info($"Idle auto-saved: {tab?.Title}");
                        }
                        catch { }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][ERROR] IdleSave failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][ERROR] IdleSave outer failed: {ex.Message}"); }
        }

        public void SelectTab(ITabItem tab)
        {
            if (tab == null) return;
            Pane.SelectedTab = tab;
            PaneTabControl.SelectedItem = tab;
            // Content loading handled by XAML binding - no manual loading needed
        }

        private NoteNestPanel? FindNoteNestPanel(DependencyObject? obj)
        {
            if (obj == null) return null;
            if (obj is NoteNestPanel panel) return panel;
            
            int childCount = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = FindNoteNestPanel(child);
                if (result != null) return result;
            }
            return null;
        }
        
        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ITabItem tab)
            {
                // Optional: flush binding for the tab editor
                try
                {
                    var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
                    if (container != null)
                    {
                        var presenter = FindVisualChild<ContentPresenter>(container);
                        var editor = FindVisualChild<SmartTextEditor>(presenter);
                        var binding = editor?.GetBindingExpression(TextBox.TextProperty);
                        binding?.UpdateSource();
                        System.Diagnostics.Debug.WriteLine($"[UI] CloseTab flush binding for noteId={tab?.Note?.Id} at={DateTime.Now:HH:mm:ss.fff}");
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][WARN] CloseTab flush binding failed: {ex.Message}"); }

                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UI] CloseTab START id={tab?.Note?.Id} title={tab?.Title}");
                    // Detach TextChanged to avoid post-close events during template teardown
                    try
                    {
                        var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
                        var presenter = FindVisualChild<ContentPresenter>(container);
                        var editor = FindVisualChild<SmartTextEditor>(presenter);
                        if (editor != null)
                        {
                            editor.TextChanged -= SmartEditor_TextChanged;
                        }
                    }
                    catch (Exception ex)
                    {
                        try { var logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger; logger?.Warning($"Failed to detach TextChanged on close: {ex.Message}"); } catch { }
                    }
                    var closed = await closeService.CloseTabWithPromptAsync(tab);
                    if (closed)
                    {
                        Pane?.Tabs.Remove(tab);
                        System.Diagnostics.Debug.WriteLine($"[UI] CloseTab REMOVED id={tab?.Note?.Id}");

                        // If this was the last tab, optionally close empty pane
                        var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                        if (Pane?.Tabs.Count == 0 && workspaceService != null && workspaceService.Panes.Count > 1)
                        {
                            _ = workspaceService.ClosePaneAsync(Pane);
                        }
                    }
                }
            }
        }
        
        // No longer needed with binding

        // LoadTabContent method removed - XAML binding handles content loading automatically

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                    
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdateEmptyState()
        {
            if (Pane != null && Pane.Tabs.Count == 0)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                PaneTabControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyMessage.Visibility = Visibility.Collapsed;
                PaneTabControl.Visibility = Visibility.Visible;
            }
        }

        // Per-tab toolbar handlers
        private void Toolbar_BulletList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertBulletList();
        }
        private void Toolbar_NumberedList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertNumberedList();
        }
        private void Toolbar_TaskList_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.InsertTaskList();
        }
        private void Toolbar_Indent_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.IndentSelection();
        }
        private void Toolbar_Outdent_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            ste?.OutdentSelection();
        }
        private void Toolbar_Bold_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"**{ste.SelectedText}**";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
        private void Toolbar_Italic_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"*{ste.SelectedText}*";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
        private void Toolbar_Underline_Click(object sender, RoutedEventArgs e)
        {
            var ste = FindVisualChild<SmartTextEditor>(this);
            if (ste == null || string.IsNullOrEmpty(ste.SelectedText)) return;
            var start = ste.SelectionStart;
            ste.SelectedText = $"__{ste.SelectedText}__";
            ste.CaretIndex = start + ste.SelectedText.Length;
        }
    }
}


