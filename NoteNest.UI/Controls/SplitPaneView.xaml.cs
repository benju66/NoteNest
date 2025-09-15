using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Documents;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.Controls;
using NoteNest.UI.Controls.Editor.Core;
using NoteNest.Core.Events;
using NoteNest.Core.Services;
using System.Windows.Input;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Controls
{
    public partial class SplitPaneView : UserControl
    {
        public SplitPane? Pane { get; private set; }
        public event EventHandler<ITabItem>? SelectedTabChanged;
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
        // Removed: IWorkspaceStateService - now using SaveManager
        private readonly NoteNest.Core.Services.ConfigurationService? _configService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
        private readonly NoteNest.Core.Services.Logging.IAppLogger? _logger = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) as NoteNest.Core.Services.Logging.IAppLogger;
        private readonly NoteNest.Core.Services.NoteMetadataManager? _metadataManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.NoteMetadataManager)) as NoteNest.Core.Services.NoteMetadataManager;
        private bool _isDragging;

        // Expose commands for input bindings
        public ICommand RenameSelectedTabCommand => new NoteNest.UI.Commands.AsyncRelayCommand(async _ => await RenameSelectedTabAsync());
        public ICommand CloseSelectedTabCommand => new NoteNest.UI.Commands.AsyncRelayCommand(async _ => await CloseSelectedTabAsync());

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

            // Drag tracking to avoid middle-click during drag
            try
            {
                PaneTabControl.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed) _isDragging = true;
                };
                PaneTabControl.PreviewMouseUp += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Released)
                    {
                        Dispatcher.BeginInvoke(new Action(() => _isDragging = false), System.Windows.Threading.DispatcherPriority.Background);
                    }
                };
                PaneTabControl.Drop += (s, e) => { _isDragging = false; };
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
                // PROPER ARCHITECTURE: Tab switching without complex timer coordination
                
                // Find the previously selected tab from the old selection
                ITabItem? oldTab = null;
                if (e.RemovedItems?.Count > 0 && e.RemovedItems[0] is ITabItem removedTab)
                {
                    oldTab = removedTab;
                }
                
                // PROPER ARCHITECTURE: Force save old tab before switch (if dirty)
                if (oldTab != null && oldTab.IsDirty)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] Tab switch save: {oldTab.Title} → {newTab.Title}");
                        
                        // Get fresh content from editor and save immediately
                        var oldEditor = GetEditorForTab(oldTab);
                        if (oldEditor != null && oldTab is NoteTabItem oldTabItem)
                        {
                            var content = oldEditor.SaveContent(); // Use interface method
                            oldTabItem.UpdateContentFromEditor(content);
                            oldEditor.MarkClean();
                            
                            // Force immediate save (bypass timers for tab switches)
                            var saveManager = GetSaveManager();
                            if (saveManager != null)
                            {
                                _ = Task.Run(async () => await saveManager.SaveNoteAsync(oldTab.NoteId));
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[UI] Tab switch save completed for {oldTab.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] Tab switch save failed: {ex.Message}");
                    }
                }

                // Update pane and workspace state
                Pane.SelectedTab = newTab;
                var workspaceService = _workspaceService;
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
                            var shownLen = editorNew?.IsDirty == true ? -1 : 0; // Track dirty state instead
                            // State tracking removed - SaveManager handles content
                            System.Diagnostics.Debug.WriteLine($"[UI] Switched TO tab id={newTab.Note.Id} shownLen={shownLen}");
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
                var editor = GetActiveEditor();
                
                if (editor != null)
                {
                    // Apply editor settings using interface for all editor types
                    if (config?.Settings != null)
                    {
                        // RTF editor has ApplySettings method, FormattedTextEditor uses manual application
                        if (editor is RTFTextEditor rtfEditor)
                        {
                            try { rtfEditor.ApplySettings(config.Settings.EditorSettings); } catch { }
                        }
                        else
                        {
                            // Manual settings for FormattedTextEditor (legacy approach)
                            try { SpellCheck.SetIsEnabled(editor as TextBoxBase, config.Settings.EditorSettings.EnableSpellCheck); } catch { }
                            try { editor.Document.FontFamily = new System.Windows.Media.FontFamily(config.Settings.EditorSettings.FontFamily); } catch { }
                            try { editor.Document.FontSize = config.Settings.EditorSettings.FontSize > 0 ? config.Settings.EditorSettings.FontSize : editor.Document.FontSize; } catch { }
                        }
                    }
                    
                    // Wire up metadata manager and current note for both editor types
                    if (_metadataManager != null && Pane?.SelectedTab?.Note != null)
                    {
                        try
                        {
                            if (editor is FormattedTextEditor formattedEditor)
                            {
                                formattedEditor.SetMetadataManager(_metadataManager);
                                formattedEditor.CurrentNote = Pane.SelectedTab.Note;
                            }
                            else if (editor is RTFTextEditor rtfEditor)
                            {
                                rtfEditor.SetMetadataManager(_metadataManager);
                                rtfEditor.CurrentNote = Pane.SelectedTab.Note;
                            }
                        }
                        catch { }
                    }
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
                // Try immediate focus
                if (editor != null)
                {
                    Keyboard.Focus(editor);
                }
                // Also schedule focus after current input/close events complete
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var container2 = PaneTabControl.ItemContainerGenerator.ContainerFromItem(Pane?.SelectedTab) as TabItem;
                        var presenter2 = FindVisualChild<ContentPresenter>(container2);
                        var editor2 = FindVisualChild<FormattedTextEditor>(presenter2);
                        if (editor2 != null) Keyboard.Focus(editor2);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch { }
        }

        // Header interactions and commands
        private async System.Threading.Tasks.Task RenameSelectedTabAsync()
        {
            var selected = Pane?.SelectedTab;
            if (selected == null) return;
            await RenameTabAsync(selected);
        }

        private async System.Threading.Tasks.Task CloseSelectedTabAsync()
        {
            var selected = Pane?.SelectedTab;
            if (selected == null) return;
            try
            {
                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    await closeService.CloseTabWithPromptAsync(selected);
                }
            }
            catch (Exception ex) { try { _logger?.Error(ex, "Failed to close selected tab"); } catch { } }
        }

        private async System.Threading.Tasks.Task RenameTabAsync(ITabItem tab)
        {
            if (tab?.Note == null) return;
            try
            {
                var dialog = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.UI.Services.IDialogService)) as NoteNest.UI.Services.IDialogService;
                var ops = (Application.Current as App)?.ServiceProvider?.GetService(typeof(INoteOperationsService)) as INoteOperationsService;
                if (dialog == null || ops == null) return;

                var currentName = tab.Note.Title;
                var newName = await dialog.ShowInputDialogAsync("Rename Note", "Enter new name:", currentName, text =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return "Name cannot be empty";
                    if (text.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return null;
                    return null;
                });
                if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

                var success = await ops.RenameNoteAsync(tab.Note, newName);
                if (!success)
                {
                    dialog.ShowError("A note with this name already exists in the same folder.", "Name Conflict");
                }
                else
                {
                    try { _logger?.Info($"Renamed note from '{currentName}' to '{newName}'"); } catch { }
                    try { NotifyTreeOfRename(tab.Note); } catch { }
                }
            }
            catch (Exception ex) { try { _logger?.Error(ex, "Failed to rename tab"); } catch { } }
            finally
            {
                // Always restore focus to the active editor so toolbar buttons don't retain keyboard focus
                TryFocusActiveEditor();
            }
        }

        private async void RenameTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ITabItem tab)
            {
                await RenameTabAsync(tab);
            }
        }

        private async void CloseAllTabs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    await closeService.CloseAllTabsWithPromptAsync();
                }
            }
            catch (Exception ex) { try { _logger?.Error(ex, "Failed to close all tabs"); } catch { } }
        }

        private async void CloseOthers_Click(object sender, RoutedEventArgs e)
        {
            if (Pane == null) return;
            if (sender is not MenuItem mi || mi.Tag is not ITabItem keepTab) return;
            try
            {
                var dialog = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.UI.Services.IDialogService)) as NoteNest.UI.Services.IDialogService;
                if (dialog == null) return;

                var others = Pane.Tabs.Where(t => !ReferenceEquals(t, keepTab)).ToList();
                var dirty = others.Where(t => t.IsDirty).ToList();
                if (dirty.Any())
                {
                    var result = await dialog.ShowYesNoCancelAsync($"Do you want to save changes to {dirty.Count} modified file(s)?", "Save Changes");
                    if (result == null) return;
                    if (result == true)
                    {
                        var saveManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ISaveManager)) as ISaveManager;
                        if (saveManager != null)
                        {
                            foreach (var t in dirty)
                            {
                                try
                                {
                                    await saveManager.SaveNoteAsync(t.NoteId);
                                }
                                catch { }
                            }
                        }
                    }
                }
                foreach (var t in others)
                {
                    Pane.Tabs.Remove(t);
                }
            }
            catch (Exception ex) { try { _logger?.Error(ex, "Failed to close other tabs"); } catch { } }
        }

        private async void SaveTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ITabItem tab)
            {
                var saveManager = (Application.Current as App)?.ServiceProvider
                    ?.GetService(typeof(ISaveManager)) as ISaveManager;
                
                if (saveManager != null)
                {
                    await saveManager.SaveNoteAsync(tab.NoteId);
                }
            }
        }


        private async System.Threading.Tasks.Task SaveTabAsync(ITabItem tab)
        {
            if (tab?.Note == null) return;
            try
            {
                if (tab is NoteNest.UI.ViewModels.NoteTabItem nti)
                {
                    // Use interface-based editor access for all editor types
                    var editor = GetEditorForTab(tab);
                    if (editor != null)
                    {
                        var content = editor.SaveContent(); // Works for both RTF and Markdown
                        nti.UpdateContentFromEditor(content);
                        editor.MarkClean();
                    }
                }
                var saveManager = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ISaveManager)) as ISaveManager;
                var success = saveManager != null && await saveManager.SaveNoteAsync(tab.NoteId);
                if (success)
                {
                    tab.IsDirty = false;
                    tab.Note.IsDirty = false;
                    try { _logger?.Info($"Saved note: {tab.Title}"); } catch { }
                }
            }
            catch (Exception ex) { try { _logger?.Error(ex, $"Failed to save tab: {tab?.Title}"); } catch { } }
        }

        private void TabHeader_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            if (_isDragging) return;
            if (sender is not FrameworkElement fe) return;
            var tab = fe.DataContext as ITabItem;
            if (tab == null) return;
            e.Handled = true;
            try
            {
                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    _ = closeService.CloseTabWithPromptAsync(tab);
                }
            }
            catch { }
        }

        // REMOVED: FormattedEditor_Loaded - now handled by NoteEditorContainer

        // REMOVED: FormattedEditor_Unloaded - now handled by NoteEditorContainer

        // REMOVED: SmartEditor_DataContextChanged - now handled by NoteEditorContainer

        // REMOVED: LoadContentIntoEditor - now handled by NoteEditorContainer

        // REMOVED: Editor_TextChanged - now handled by NoteEditorContainer

        // Removed: Old timer coordination methods (moved to NoteTabItem for proper architecture)

        /// <summary>
        /// Get the currently active editor for this pane (interface-based)
        /// </summary>
        private NoteNest.UI.Controls.Editor.Core.INotesEditor GetActiveEditor()
        {
            var tab = Pane?.SelectedTab;
            if (tab == null) return null;
            
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return null;
            
            var presenter = FindVisualChild<ContentPresenter>(container);
            var editorContainer = FindVisualChild<NoteEditorContainer>(presenter);
            return editorContainer?.UnderlyingEditor;
        }


        /// <summary>
        /// Get SaveManager service
        /// </summary>
        private ISaveManager GetSaveManager()
        {
            return (Application.Current as App)?.ServiceProvider
                ?.GetService(typeof(ISaveManager)) as ISaveManager;
        }

        /// <summary>
        /// Get editor for a specific tab (interface-based for all editor types)
        /// </summary>
        private NoteNest.UI.Controls.Editor.Core.INotesEditor GetEditorForTab(ITabItem tab)
        {
            if (tab == null) return null;
            
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return null;
            
            var presenter = FindVisualChild<ContentPresenter>(container);
            var editorContainer = FindVisualChild<NoteEditorContainer>(presenter);
            return editorContainer?.UnderlyingEditor;
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
        
        private void NotifyTreeOfRename(NoteModel renamedNote)
        {
            if (renamedNote == null) return;
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            if (mainWindow?.DataContext is not MainViewModel vm) return;
            foreach (var category in vm.Categories)
            {
                var item = FindNoteInTree(category, renamedNote.Id);
                if (item != null)
                {
                    item.OnPropertyChanged(nameof(NoteTreeItem.Title));
                    item.OnPropertyChanged(nameof(NoteTreeItem.FilePath));
                    break;
                }
            }
        }

        private NoteTreeItem? FindNoteInTree(CategoryTreeItem category, string noteId)
        {
            if (category == null || string.IsNullOrEmpty(noteId)) return null;
            var found = category.Notes.FirstOrDefault(n => n.Model?.Id == noteId);
            if (found != null) return found;
            foreach (var sub in category.SubCategories)
            {
                var r = FindNoteInTree(sub, noteId);
                if (r != null) return r;
            }
            return null;
        }

        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ITabItem tab)
            {
                // Force flush any pending content from the editor
                try
                {
                    // Use interface-based editor access for all editor types
                    var editor = GetEditorForTab(tab);
                    if (editor != null && tab is NoteTabItem nti)
                    {
                        var content = editor.SaveContent(); // Works for both RTF and Markdown
                        nti.UpdateContentFromEditor(content);
                        editor.MarkClean();
                    }
                    System.Diagnostics.Debug.WriteLine($"[UI] CloseTab force flush for noteId={tab?.Note?.Id} at={DateTime.Now:HH:mm:ss.fff}");
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UI][WARN] CloseTab flush failed: {ex.Message}"); }

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
                            // Removed: old TextChanged handler (no longer needed in new architecture)
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


    }
}


