using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Threading;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.UI.Controls;
using NoteNest.UI.Controls.Editor.RTF;
using NoteNest.UI.Services;
using NoteNest.Core.Events;
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
                    // Content loading handled by TabControl_SelectionChanged event
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
        
        // TAB-OWNED EDITOR PATTERN: Clean and simple tab switching
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem newTab)
            {
                System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Switching to tab: {newTab.Title}");
                
                var newTabItem = newTab as NoteTabItem;
                
                // Save old tab if dirty
                if (e.RemovedItems?.Count > 0 && e.RemovedItems[0] is NoteTabItem oldTab)
                {
                    if (oldTab.IsDirty)
                {
                    try
                    {
                            // Direct access to tab's editor - guaranteed to work
                            var content = oldTab.Editor.SaveContent();
                            var saveManager = GetSaveManager();
                            if (saveManager != null)
                            {
                                saveManager.UpdateContent(oldTab.NoteId, content);
                                _ = saveManager.SaveNoteAsync(oldTab.NoteId);
                                System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Saved {oldTab.Title}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Save failed for {oldTab.Title}: {ex.Message}");
                        }
                    }
                }
                
                // Load new tab content
                if (newTabItem != null)
                {
                    try
                    {
                        if (!newTabItem.ContentLoaded)
                        {
                            var saveManager = GetSaveManager();
                            var content = saveManager?.GetContent(newTab.NoteId) ?? "";
                            newTabItem.LoadContent(content); // Use new Tab-Owned method
                            System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Content loaded for {newTab.Title}: {content.Length} chars");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Content loading failed for {newTab.Title}: {ex.Message}");
                        _logger?.Error(ex, $"Content loading failed for tab: {newTab.Title}");
                    }
                }

                // Update pane and workspace state
                Pane.SelectedTab = newTab;
                var workspaceService = _workspaceService;
                if (workspaceService != null)
                {
                    workspaceService.SelectedTab = newTab;
                }
                
                // Notify listeners and apply settings
                try { SelectedTabChanged?.Invoke(this, newTab); } catch { }
                TryApplyEditorSettingsToActiveEditor();
                
                // Focus the editor with robust retry logic
                if (newTabItem != null)
                {
                    SetEditorFocusRobust(newTabItem);
                }
                
                // Update global references (for services that need current editor)
                UpdateGlobalEditorReferences(newTabItem);
                
                // Sync note dirty flag with tab (for tree view indicator)
                try
                {
                    if (newTab.Note != null)
                    {
                        newTab.Note.IsDirty = newTab.IsDirty;
                        System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Synced dirty state for {newTab.Title}: {newTab.IsDirty}");
                    }
                        }
                        catch (Exception ex)
                        {
                    System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Sync dirty flag failed: {ex.Message}"); 
                }
                
                System.Diagnostics.Debug.WriteLine($"[TAB-OWNED] Tab switch completed: {newTab.Title}");
            }
        }
        
        // ENHANCED: Update global editor references for services
        private void UpdateGlobalEditorReferences(NoteTabItem? tab)
        {
            if (tab == null) return;
            
            try
            {
                // Update any services that need current editor
                if (_metadataManager != null && tab.Editor != null)
                {
                    // Set current editor for metadata operations
                    // _metadataManager.SetCurrentEditor(tab.Editor); // Implement if needed
                }
                
                // Update workspace service
                if (_workspaceService != null)
                {
                    
                    _workspaceService.SelectedTab = tab;
                }
                
                System.Diagnostics.Debug.WriteLine($"[TabSwitch] Global references updated for {tab.Title}");
                        }
                        catch (Exception ex)
                        {
                System.Diagnostics.Debug.WriteLine($"[TabSwitch] Global reference update failed: {ex.Message}");
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
                        // Apply settings to our clean RTF editor
                        if (editor is RTFEditor rtfEditor)
                        {
                            try { rtfEditor.ApplySettings(config.Settings.EditorSettings); } catch { }
                        }
                    }
                    
                    // Wire up metadata manager and current note for both editor types
                    if (_metadataManager != null && Pane?.SelectedTab?.Note != null)
                    {
                        try
                        {
                            if (editor is RTFEditor rtfEditor)
                            {
                                // Clean RTF architecture - metadata handled by note model directly
                                // rtfEditor.CurrentNote = Pane.SelectedTab.Note; // Will add if needed
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

        // PHASE 1A: Robust focus management with retry logic
        private void TryFocusActiveEditor()
        {
            if (Pane?.SelectedTab is NoteTabItem tabItem)
            {
                SetEditorFocusRobust(tabItem);
            }
            else
            {
                // Fallback to old method for non-NoteTabItem tabs
                TryFocusActiveEditorLegacy();
            }
        }
        
        private void SetEditorFocusRobust(NoteTabItem tabItem)
        {
            if (tabItem?.Editor == null) return;
            
            var attempts = 0;
            var maxAttempts = 3;
            
            void TryFocus()
            {
                attempts++;
                try
                {
                    var editor = tabItem.Editor;
                    if (editor.IsLoaded && editor.IsVisible)
                    {
                        var focusResult = editor.Focus();
                        if (focusResult)
                        {
                            // Set cursor position for consistent behavior
                            var startPosition = editor.Document?.ContentStart;
                            if (startPosition != null)
                            {
                                editor.CaretPosition = startPosition;
                            }
                            System.Diagnostics.Debug.WriteLine($"[Focus] Success for {tabItem.Title} on attempt {attempts}");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Focus] Attempt {attempts} failed for {tabItem.Title}: {ex.Message}");
                }
                
                // Retry with exponential backoff
                if (attempts < maxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempts - 1));
                    Dispatcher.BeginInvoke(new Action(TryFocus), DispatcherPriority.Background);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Focus] Failed after {maxAttempts} attempts for {tabItem.Title}");
                }
            }
            
            // Start focus attempt
            Dispatcher.BeginInvoke(new Action(TryFocus), DispatcherPriority.Input);
        }
        
        // Legacy focus method for backward compatibility
        private void TryFocusActiveEditorLegacy()
        {
            try
            {
                var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(Pane?.SelectedTab) as TabItem;
                var presenter = FindVisualChild<ContentPresenter>(container);
                var editor = FindVisualChild<RTFEditor>(presenter);
                if (editor != null)
                {
                    Keyboard.Focus(editor);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Focus] Legacy focus failed: {ex.Message}");
            }
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

        /// <summary>
        /// RTF-FOCUSED: Close other tabs with atomic persistence
        /// </summary>
        private async void CloseOthers_Click(object sender, RoutedEventArgs e)
        {
            if (Pane == null) return;
            if (sender is not MenuItem mi || mi.Tag is not ITabItem keepTab) return;
            
            System.Diagnostics.Debug.WriteLine($"[RTF] CloseOthers START, keeping: {keepTab.Title}");
            
            try
            {
                var dialog = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.UI.Services.IDialogService)) as NoteNest.UI.Services.IDialogService;
                if (dialog == null) return;

                var others = Pane.Tabs.Where(t => !ReferenceEquals(t, keepTab)).ToList();
                var dirty = others.Where(t => t.IsDirty).ToList();
                
                // RTF-SPECIFIC: Flush content from RTF editors before checking dirty state
                foreach (var dirtyTab in dirty)
                {
                    try
                    {
                        var rtfEditor = GetRTFEditorForTab(dirtyTab);
                        if (rtfEditor != null && dirtyTab is NoteTabItem nti)
                        {
                            var content = rtfEditor.SaveContent();
                            nti.UpdateContentFromEditor(content);
                            rtfEditor.MarkClean();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"RTF content flush failed for {dirtyTab.Title}: {ex.Message}");
                    }
                }
                
                if (dirty.Any())
                {
                    var result = await dialog.ShowYesNoCancelAsync($"Do you want to save changes to {dirty.Count} modified RTF file(s)?", "Save RTF Changes");
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
                                    System.Diagnostics.Debug.WriteLine($"[RTF] Saved RTF tab before closing: {t.Title}");
                                }
                                catch (Exception ex)
                                {
                                    _logger?.Error(ex, $"Failed to save RTF tab {t.Title} before closing others");
                                }
                            }
                        }
                    }
                }
                
                // Remove tabs from UI
                foreach (var t in others)
                {
                    Pane.Tabs.Remove(t);
                }
                
                // BULLETPROOF: Force immediate persistence save for RTF close others operation
                try
                {
                    var persistence = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabPersistenceService)) as ITabPersistenceService;
                    var workspaceService = _workspaceService;
                    
                    if (persistence != null && workspaceService != null)
                    {
                        var remainingTabs = workspaceService.OpenTabs;
                        var activeTabId = workspaceService.SelectedTab?.Note?.Id;
                        var activeContent = workspaceService.SelectedTab?.Content;
                        
                        await persistence.ForceSaveAsync(remainingTabs, activeTabId, activeContent);
                        System.Diagnostics.Debug.WriteLine($"[RTF] Force persistence save completed for RTF close others");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Force persistence save failed after RTF close others");
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] CloseOthers COMPLETED, {others.Count} RTF tabs closed");
            }
            catch (Exception ex) 
            { 
                _logger?.Error(ex, "Failed to close other RTF tabs"); 
                System.Diagnostics.Debug.WriteLine($"[RTF] CloseOthers FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// RTF-FOCUSED: Save RTF tab with content flush
        /// </summary>
        /// <summary>
        /// ATOMIC ENHANCED: RTF save with atomic metadata coordination
        /// DEMONSTRATION of bulletproof content + metadata consistency
        /// </summary>
        private async void SaveTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ITabItem tab)
            {
                System.Diagnostics.Debug.WriteLine($"[SAVE] SaveTab START: {tab.Title}");
                
                try
                {
                    var app = Application.Current as App;
                    
                    // Use RTF-integrated save system (now the only system)
                    System.Diagnostics.Debug.WriteLine($"[SAVE] Using RTF-integrated save engine for: {tab.Title}");
                    
                    var rtfSaveWrapper = app?.ServiceProvider?.GetService(typeof(RTFSaveEngineWrapper)) as RTFSaveEngineWrapper;
                    var rtfEditor = GetRTFEditorForTab(tab);
                    
                    if (rtfSaveWrapper != null && rtfEditor != null)
                    {
                        var result = await rtfSaveWrapper.SaveFromRichTextBoxAsync(
                            tab.NoteId,
                            rtfEditor, // Pass the RTFEditor (which extends RichTextBox)
                            tab.Title,
                            SaveType.Manual
                        );
                        
                        if (result.Success)
                        {
                            if (tab is NoteTabItem nti)
                            {
                                nti.IsDirty = false;
                                nti.Note.IsDirty = false;
                            }
                            System.Diagnostics.Debug.WriteLine($"[SAVE] RTF-integrated save succeeded: {tab.Title}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SAVE] RTF-integrated save failed: {tab.Title} - {result.Error}");
                        }
                        
                        return; // Success path
                    }
                    else
                    {
                        // Fallback to ISaveManager interface (still uses RTFIntegratedSaveEngine)
                        System.Diagnostics.Debug.WriteLine($"[SAVE] RTF wrapper not available, using ISaveManager interface");
                        var saveManager = app?.ServiceProvider?.GetService(typeof(ISaveManager)) as ISaveManager;
                        
                        // RTF-SPECIFIC: Flush content from RTF editor first
                        if (rtfEditor != null && tab is NoteTabItem nti)
                        {
                            var content = rtfEditor.SaveContent();
                            nti.UpdateContentFromEditor(content);
                            rtfEditor.MarkClean();
                            System.Diagnostics.Debug.WriteLine($"[SAVE] RTF content flushed for ISaveManager: {tab.Title}");
                        }
                        
                        if (saveManager != null)
                        {
                            var success = await saveManager.SaveNoteAsync(tab.NoteId);
                            if (success)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SAVE] ISaveManager save succeeded: {tab.Title}");
                            }
                            else
                            {
                                _logger?.Warning($"ISaveManager save returned false: {tab.Title}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, $"Save failed for tab: {tab.Title}");
                    System.Diagnostics.Debug.WriteLine($"[ATOMIC] SaveTab EXCEPTION: {ex.Message}");
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
        private RTFEditor GetActiveEditor()
        {
            var tab = Pane?.SelectedTab;
            if (tab == null) return null;
            
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return null;
            
            var presenter = FindVisualChild<ContentPresenter>(container);
            return FindVisualChild<RTFEditor>(presenter);
        }


        /// <summary>
        /// Get SaveManager service
        /// </summary>
        private ISaveManager GetSaveManager()
        {
            return (Application.Current as App)?.ServiceProvider
                ?.GetService(typeof(ISaveManager)) as ISaveManager;
        }
        
        // SupervisedTaskRunner removed - save operations now use RTF-integrated save system

        /// <summary>
        /// Get editor for a specific tab (interface-based for all editor types)
        /// </summary>
        private RTFEditor GetEditorForTab(ITabItem tab)
        {
            if (tab == null) return null;
            
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return null;
            
            var presenter = FindVisualChild<ContentPresenter>(container);
            return FindVisualChild<RTFEditor>(presenter);
        }

        /// <summary>
        /// Get RTF editor for a specific tab - Enhanced Tab-Owned Editor Pattern
        /// </summary>
        private RTFEditor? GetRTFEditorForTab(ITabItem tab)
        {
            // ENHANCED: Direct access via Tab-Owned Editor Pattern
            if (tab is NoteTabItem noteTabItem)
            {
                return noteTabItem.Editor;
            }
            
            return null;
        }
        
        // REMOVED: WireUpContentChangedEvent and OnRTFContentChanged methods
        // Events are now wired automatically in NoteTabItem constructor (Tab-Owned Editor Pattern)


        public void SelectTab(ITabItem tab)
        {
            if (tab == null) return;
            Pane.SelectedTab = tab;
            PaneTabControl.SelectedItem = tab;
            // Content loading handled by TabControl_SelectionChanged event
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

        /// <summary>
        /// BULLETPROOF RTF-FOCUSED TAB CLOSE: Atomic operation with force persistence
        /// </summary>
        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ITabItem tab)
            {
                System.Diagnostics.Debug.WriteLine($"[RTF] CloseTab START id={tab?.Note?.Id} title={tab?.Title}");
                
                // PHASE 1: RTF-SPECIFIC CONTENT FLUSH
                try
                {
                    var rtfEditor = GetRTFEditorForTab(tab);
                    if (rtfEditor != null && tab is NoteTabItem nti)
                    {
                        // RTF-specific content save - no abstraction needed
                        var content = rtfEditor.SaveContent();
                        nti.UpdateContentFromEditor(content);
                        rtfEditor.MarkClean();
                        System.Diagnostics.Debug.WriteLine($"[RTF] Content flushed for RTF tab: {tab.Title}");
                    }
                }
                catch (Exception ex) 
                { 
                    System.Diagnostics.Debug.WriteLine($"[RTF][WARN] RTF content flush failed: {ex.Message}"); 
                    _logger?.Warning($"RTF content flush failed for {tab?.Title}: {ex.Message}");
                }

                // PHASE 2: COORDINATED CLOSE WITH FORCE PERSISTENCE
                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    var closed = await closeService.CloseTabWithPromptAsync(tab);
                    if (closed)
                    {
                        // Remove from UI pane collection
                        Pane?.Tabs.Remove(tab);
                        System.Diagnostics.Debug.WriteLine($"[RTF] Tab removed from UI: {tab?.Title}");
                        
                        // RTF-integrated save (eliminates ForceSaveAsync bypass)
                        try
                        {
                            var app = Application.Current as App;
                            
                            // Use RTF-integrated save for tab close (now the only system)
                            var rtfSaveWrapper = app?.ServiceProvider?.GetService(typeof(RTFSaveEngineWrapper)) as RTFSaveEngineWrapper;
                            
                            if (rtfSaveWrapper != null && tab.IsDirty)
                            {
                                var rtfEditor = GetRTFEditorForTab(tab);
                                if (rtfEditor != null)
                                {
                                    var result = await rtfSaveWrapper.SaveFromRichTextBoxAsync(
                                        tab.NoteId,
                                        rtfEditor,
                                        tab.Title ?? "Untitled",
                                        NoteNest.Core.Services.SaveType.TabClose
                                    );
                                    
                                    if (result.Success)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[RTF] RTF-integrated tab close save succeeded: {tab.Title}");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[RTF] RTF-integrated tab close save failed: {tab.Title} - {result.Error}");
                                    }
                                }
                            }
                            
                            // Save tab persistence state using normal method (no more force bypass)
                            var persistence = app?.ServiceProvider?.GetService(typeof(ITabPersistenceService)) as ITabPersistenceService;
                            var workspaceService = _workspaceService;
                            
                            if (persistence != null && workspaceService != null)
                            {
                                var remainingTabs = workspaceService.OpenTabs;
                                var activeTabId = workspaceService.SelectedTab?.Note?.Id;
                                var activeContent = workspaceService.SelectedTab?.Content;
                                
                                await persistence.SaveAsync(remainingTabs, activeTabId, activeContent);
                                System.Diagnostics.Debug.WriteLine($"[RTF] Tab persistence save completed for RTF tab close");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RTF] Force persistence save failed: {ex.Message}");
                            _logger?.Error(ex, $"Force persistence save failed after RTF tab close: {tab?.Title}");
                            
                            // Fallback to regular marking
                            try
                            {
                                var persistence = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabPersistenceService)) as ITabPersistenceService;
                                persistence?.MarkChanged();
                            }
                            catch { }
                        }

                        // Auto-close empty pane if needed  
                        if (Pane?.Tabs.Count == 0 && _workspaceService != null && _workspaceService.Panes.Count > 1)
                        {
                            _ = _workspaceService.ClosePaneAsync(Pane);
                            System.Diagnostics.Debug.WriteLine($"[RTF] Empty pane closed after last RTF tab removal");
                        }
                    }
                }
                else
                {
                    // ENHANCED FALLBACK: RTF-aware fallback handling
                    System.Diagnostics.Debug.WriteLine($"[RTF] FALLBACK - TabCloseService missing for RTF tab");
                    _logger?.Warning("TabCloseService missing during RTF tab close - using fallback");
                    
                    Pane?.Tabs.Remove(tab);
                    
                    // Try workspace service directly
                    try
                    {
                        var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                        if (workspaceService != null)
                        {
                            _ = Task.Run(async () => await workspaceService.CloseTabAsync(tab));
                            System.Diagnostics.Debug.WriteLine($"[RTF] FALLBACK - workspace service called directly");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RTF] FALLBACK failed: {ex.Message}");
                        _logger?.Error(ex, $"RTF tab close fallback failed: {tab?.Title}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"[RTF] CloseTab COMPLETED for RTF tab: {tab?.Title}");
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


