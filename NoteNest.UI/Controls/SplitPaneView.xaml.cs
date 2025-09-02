using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;
using NoteNest.Core.Events;
using NoteNest.Core.Services;

namespace NoteNest.UI.Controls
{
    public partial class SplitPaneView : UserControl
    {
        public SplitPane? Pane { get; private set; }
        public event EventHandler<ITabItem>? SelectedTabChanged;
        private System.Windows.Threading.DispatcherTimer? _idleSaveTimer;
        private DateTime _lastTextChangedAt;
        private int _typingBurstCount;
        private bool _isDropHighlight;
        
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool),
                typeof(SplitPaneView), new PropertyMetadata(false, OnIsActiveChanged));
        
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }
        
        private readonly IWorkspaceService? _workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
        private readonly NoteNest.Core.Services.IWorkspaceStateService? _stateService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
        private readonly NoteNest.Core.Services.ConfigurationService? _configService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
        private readonly NoteNest.Core.Services.Logging.IAppLogger? _logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;

        public SplitPaneView()
        {
            InitializeComponent();
            try
            {
                var bus = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IEventBus)) as IEventBus;
                bus?.Subscribe<AppSettingsChangedEvent>(_ =>
                {
                    // Apply to current active editor when settings change
                    Dispatcher.Invoke(() => TryApplyEditorSettingsToActiveEditor());
                });
            }
            catch { }
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

            // Ensure overflow behavior works after dynamic creation
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    PaneTabControl.ApplyTemplate();
                    PaneTabControl.UpdateLayout();
                    if (NoteNest.UI.Behaviors.TabOverflowBehavior.GetEnableOverflow(PaneTabControl))
                    {
                        NoteNest.UI.Behaviors.TabOverflowBehavior.SetEnableOverflow(PaneTabControl, false);
                        NoteNest.UI.Behaviors.TabOverflowBehavior.SetEnableOverflow(PaneTabControl, true);
                    }
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            // Subscribe to collection changes
            if (pane.Tabs is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnTabsCollectionChanged;
            }
            
            // Set initial selection
            if (pane.SelectedTab != null)
            {
                PaneTabControl.SelectedItem = pane.SelectedTab;
                SelectedTabChanged?.Invoke(this, pane.SelectedTab);
                TryApplyEditorSettingsToActiveEditor();
                TryFocusActiveEditor();
            }
            else if (pane.Tabs.Count > 0)
            {
                PaneTabControl.SelectedItem = pane.Tabs[0];
                pane.SelectedTab = pane.Tabs[0];
                SelectedTabChanged?.Invoke(this, pane.SelectedTab);
                TryApplyEditorSettingsToActiveEditor();
                TryFocusActiveEditor();
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
            
            // If this pane just became empty, request close of the pane (if there is another sibling pane)
            if (Pane != null && Pane.Tabs.Count == 0)
            {
                try
                {
                    var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                    if (workspaceService != null)
                    {
                        _ = workspaceService.ClosePaneAsync(Pane);
                    }
                }
                catch { }
                return;
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
            if (_isDropHighlight)
            {
                PaneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                PaneBorder.Background = new SolidColorBrush(Color.FromArgb(32, 0, 122, 204));
            }
            else
            {
                PaneBorder.BorderBrush = IsActive 
                    ? new SolidColorBrush(Colors.DodgerBlue)
                    : new SolidColorBrush(Colors.LightGray);
                PaneBorder.Background = Brushes.Transparent;
            }
        }

        public void SetDropHighlight(bool isHighlighted)
        {
            _isDropHighlight = isHighlighted;
            UpdateVisualState();
        }
        
        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem newTab)
            {
                // Auto-save previous tab if it was dirty
                var workspaceService = _workspaceService;
                var state = _stateService;
                
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
                            var editor = FindVisualChild<FormattedTextEditor>(presenter);
                            var binding = editor?.GetBindingExpression(FormattedTextEditor.MarkdownContentProperty);
                            binding?.UpdateSource();
                        }
                        catch (Exception ex)
                        {
                            try { _logger?.Warning($"Failed to flush binding on tab switch: {ex.Message}"); } catch { }
                        }
                        // Push latest content from ITabItem into state before saving
                        try { state.UpdateNoteContent(oldTab.Note.Id, oldTab.Content ?? string.Empty); } catch { }
                        // Save using state service
                        var result = await state.SaveNoteAsync(oldTab.Note.Id);
                        System.Diagnostics.Debug.WriteLine($"[UI] Tab switch auto-save END noteId={oldTab?.Note?.Id} success={result?.Success} at={DateTime.Now:HH:mm:ss.fff}");
                        try
                        {
                            _logger?.Info($"Tab switch auto-saved: {oldTab?.Title}");
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
                try { SelectedTabChanged?.Invoke(this, newTab); } catch { }
                TryApplyEditorSettingsToActiveEditor();
                TryFocusActiveEditor();
                
                // Sync note dirty flag with tab (for tree view indicator)
                try
                {
                    if (newTab.Note != null)
                    {
                        newTab.Note.IsDirty = newTab.IsDirty;
                        try
                        {
                            var presenterNew = FindVisualChild<ContentPresenter>(PaneTabControl.ItemContainerGenerator.ContainerFromItem(newTab) as TabItem);
                            var editorNew = FindVisualChild<FormattedTextEditor>(presenterNew);
                            var shownLen = editorNew?.MarkdownContent?.Length ?? -1;
                            var stateNew = _stateService;
                            var stateLen = (stateNew != null && stateNew.OpenNotes.TryGetValue(newTab.Note.Id, out var wn2)) ? (wn2.CurrentContent?.Length ?? 0) : -1;
                            System.Diagnostics.Debug.WriteLine($"[UI] Switched TO tab id={newTab.Note.Id} shownLen={shownLen} stateLen={stateLen}");
                        }
                        catch (Exception ex)
                        {
                            try { _logger?.Warning($"Failed to inspect editor on tab switch: {ex.Message}"); } catch { }
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][WARN] Sync dirty flag failed: {ex.Message}"); }
            }
        }

        private void TryApplyEditorSettingsToActiveEditor()
        {
            try
            {
                var config = _configService;
                var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(Pane?.SelectedTab) as TabItem;
                var presenter = FindVisualChild<ContentPresenter>(container);
                var editor = FindVisualChild<FormattedTextEditor>(presenter);
                if (editor != null && config?.Settings != null)
                {
                    try { SpellCheck.SetIsEnabled(editor, config.Settings.EnableSpellCheck); } catch { }
                    try { editor.Document.FontFamily = new System.Windows.Media.FontFamily(config.Settings.FontFamily); } catch { }
                    try { editor.Document.FontSize = config.Settings.FontSize > 0 ? config.Settings.FontSize : editor.Document.FontSize; } catch { }
                }
            }
            catch { }
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

        private void TryFocusActiveEditor()
        {
            try
            {
                var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(Pane?.SelectedTab) as TabItem;
                var presenter = FindVisualChild<ContentPresenter>(container);
                var editor = FindVisualChild<FormattedTextEditor>(presenter);
                editor?.Focus();
            }
            catch { }
        }

        private async void SmartEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var configService = _configService;
            if (configService?.Settings?.AutoSaveIdleMs > 0)
            {
                _idleSaveTimer ??= new System.Windows.Threading.DispatcherTimer();
                // Smart idle interval: base on settings, adapt on rapid typing and large notes
                var baseMs = configService.Settings.AutoSaveIdleMs;
                var now = DateTime.UtcNow;
                var sinceLast = (now - _lastTextChangedAt).TotalMilliseconds;
                var adaptiveEnabled = configService.Settings.AdaptiveAutoSaveEnabled;

                if (adaptiveEnabled && sinceLast < 500)
                {
                    _typingBurstCount = Math.Min(_typingBurstCount + 1, 5);
                }
                else
                {
                    _typingBurstCount = 0;
                }

                var adaptiveMs = baseMs;
                // If rapidly typing, extend debounce modestly
                if (adaptiveEnabled && _typingBurstCount >= 2)
                {
                    var preset = (configService.Settings.AdaptiveAutoSavePreset ?? "Balanced").ToLowerInvariant();
                    var bump = preset == "aggressive" ? 150 : preset == "conservative" ? 500 : 300;
                    adaptiveMs += bump;
                }

                // If note is very large, increase debounce
                try
                {
                    var tab = Pane?.SelectedTab;
                    var contentLen = tab?.Content?.Length ?? 0;
                    if (adaptiveEnabled)
                    {
                        if (contentLen > 50000) adaptiveMs = (int)(adaptiveMs * 1.5);
                        if (contentLen > 200000) adaptiveMs = (int)(adaptiveMs * 2);
                    }
                }
                catch { }

                // Clamp interval
                if (adaptiveMs < 250) adaptiveMs = 250;
                if (adaptiveMs > 60000) adaptiveMs = 60000;

                _idleSaveTimer.Interval = TimeSpan.FromMilliseconds(adaptiveMs);
                _idleSaveTimer.Tick -= IdleSaveTimer_Tick;
                _idleSaveTimer.Tick += IdleSaveTimer_Tick;
                _idleSaveTimer.Stop();
                _idleSaveTimer.Start();
                _lastTextChangedAt = now;
            }
            // Do not push content here; rely on binding to NoteTabItem.Content to update state.
            try
            {
                var workspaceService = _workspaceService;
                var tab = Pane?.SelectedTab ?? workspaceService?.SelectedTab;
                var state = _stateService;
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
                var workspaceService = _workspaceService;
                var state = _stateService;
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
                            var editor = FindVisualChild<FormattedTextEditor>(presenter);
                            var binding = editor?.GetBindingExpression(FormattedTextEditor.MarkdownContentProperty);
                            binding?.UpdateSource();
                        }
                        catch (Exception ex)
                        {
                            try { _logger?.Warning($"Failed to flush binding on idle save: {ex.Message}"); } catch { }
                        }
                        // Push latest content from ITabItem into state before saving
                        try { state.UpdateNoteContent(tab.Note.Id, tab.Content ?? string.Empty); } catch { }
                        // Save
                        var result = await state.SaveNoteAsync(tab.Note.Id);
                        System.Diagnostics.Debug.WriteLine($"[UI] IdleSave END noteId={tab?.Note?.Id} success={result?.Success} at={DateTime.Now:HH:mm:ss.fff}");
                        try
                        {
                            if (result?.Success == true) _logger?.Info($"Idle auto-saved: {tab?.Title}");
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
            // Guard: only traverse visuals
            if (obj is not Visual && obj is not System.Windows.Media.Media3D.Visual3D)
                return null;
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
                        var editor = FindVisualChild<FormattedTextEditor>(presenter);
                        var binding = editor?.GetBindingExpression(FormattedTextEditor.MarkdownContentProperty);
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
                        var editor = FindVisualChild<FormattedTextEditor>(presenter);
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
                        var workspaceService = _workspaceService;
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
            // Only Visual/Visual3D are valid for VisualTreeHelper
            if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    // Skip document model elements which are not visuals
                    if (child is FlowDocument || child is Block || child is Inline)
                        continue;

                    if (child is T typedChild)
                        return typedChild;

                    var result = FindVisualChild<T>(child);
                    if (result != null)
                        return result;
                }
            }
            catch
            {
                return null;
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
            var editor = ResolveEditorFromSender(sender as DependencyObject)
                         ?? ResolveEditorFromSelectedTab()
                         ?? FindVisualChild<FormattedTextEditor>(this);
            editor?.Focus();
            editor?.InsertBulletList();
        }
        private void Toolbar_NumberedList_Click(object sender, RoutedEventArgs e)
        {
            var editor = ResolveEditorFromSender(sender as DependencyObject)
                         ?? ResolveEditorFromSelectedTab()
                         ?? FindVisualChild<FormattedTextEditor>(this);
            editor?.Focus();
            editor?.InsertNumberedList();
        }

        private FormattedTextEditor? ResolveEditorFromSender(DependencyObject? sender)
        {
            if (sender == null) return null;
            var current = sender;
            // Walk up until we find the FormattedTextEditor within the same tab content
            while (current != null)
            {
                if (current is FormattedTextEditor fe) return fe;
                // If we hit the content presenter of the tab content, try to find editor within
                if (current is ContentPresenter cp)
                {
                    var fe2 = FindVisualChild<FormattedTextEditor>(cp);
                    if (fe2 != null) return fe2;
                }
                try { current = System.Windows.Media.VisualTreeHelper.GetParent(current); }
                catch { break; }
            }
            return null;
        }

        private FormattedTextEditor? ResolveEditorFromSelectedTab()
        {
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(Pane?.SelectedTab) as TabItem;
            var presenter = FindVisualChild<ContentPresenter>(container);
            var editor = FindVisualChild<FormattedTextEditor>(presenter)
                         ?? FindVisualChild<FormattedTextEditor>(container);
            return editor;
        }
        private void Toolbar_TaskList_Click(object sender, RoutedEventArgs e)
        {
            // Task list support not yet implemented for formatted editor
        }
        private void Toolbar_Indent_Click(object sender, RoutedEventArgs e)
        {
            // Indent handled by list structure; no-op for now
        }
        private void Toolbar_Outdent_Click(object sender, RoutedEventArgs e)
        {
            // Outdent handled by list structure; no-op for now
        }
        private void Toolbar_Bold_Click(object sender, RoutedEventArgs e)
        {
            // Not used in formatted mode; bound through EditingCommands in XAML
        }
        private void Toolbar_Italic_Click(object sender, RoutedEventArgs e)
        {
            // Not used in formatted mode; bound through EditingCommands in XAML
        }
        private void Toolbar_Underline_Click(object sender, RoutedEventArgs e)
        {
            // Not used in formatted mode
        }

        // Exposed for shutdown: flush editor bindings for all tabs in this pane
        public void FlushAllEditors()
        {
            try
            {
                foreach (var tab in Pane?.Tabs ?? System.Linq.Enumerable.Empty<ITabItem>())
                {
                    try
                    {
                        var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
                        var presenter = FindVisualChild<ContentPresenter>(container);
                        var editor = FindVisualChild<FormattedTextEditor>(presenter);
                        var binding = editor?.GetBindingExpression(FormattedTextEditor.MarkdownContentProperty);
                        binding?.UpdateSource();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}


