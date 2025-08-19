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
                    LoadTabContent(newTab);
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
        
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pane != null && PaneTabControl.SelectedItem is ITabItem tab)
            {
                // Auto-save on tab switch if enabled
                var config = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                var workspaceService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                if (config?.Settings?.AutoSaveOnTabSwitch == true && Pane.SelectedTab is ITabItem oldTab && oldTab.IsDirty)
                {
                    try
                    {
                        if (oldTab.Note != null && oldTab.Content != null)
                        {
                            oldTab.Note.Content = oldTab.Content;
                        }
                        var state = (Application.Current as App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                        if (UI.FeatureFlags.UseNewArchitecture && state != null)
                        {
                            state.SaveNoteAsync(oldTab.Note.Id).ConfigureAwait(false);
                        }
                        else
                        {
                            var noteOps = (Application.Current as App)?.ServiceProvider?.GetService(typeof(INoteOperationsService)) as INoteOperationsService;
                            noteOps?.SaveNoteAsync(oldTab.Note).ConfigureAwait(false);
                        }
                        oldTab.IsDirty = false;
                    }
                    catch { }
                }

                Pane.SelectedTab = tab;
                // Keep workspace SelectedTab in sync so Save commands act on the correct tab
                if (workspaceService != null)
                {
                    workspaceService.SelectedTab = tab;
                }
                
                // Always load content when a tab is selected
                LoadTabContent(tab);
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
            if (configService?.Settings?.AutoSaveIdleMs > 0 && configService.Settings.AutoSave)
            {
                _idleSaveTimer ??= new System.Windows.Threading.DispatcherTimer();
                _idleSaveTimer.Interval = TimeSpan.FromMilliseconds(configService.Settings.AutoSaveIdleMs);
                _idleSaveTimer.Tick -= IdleSaveTimer_Tick;
                _idleSaveTimer.Tick += IdleSaveTimer_Tick;
                _idleSaveTimer.Stop();
                _idleSaveTimer.Start();
            }
        }

        private async void IdleSaveTimer_Tick(object? sender, EventArgs e)
        {
            _idleSaveTimer?.Stop();
            try
            {
                var app = Application.Current as App;
                var configService = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                if (configService?.Settings?.AutoSave == true)
                {
                    var workspaceService = app?.ServiceProvider?.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
                    var tab = Pane?.SelectedTab ?? workspaceService?.SelectedTab;
                    if (tab != null && tab.IsDirty)
                    {
                        if (tab.Note != null && tab.Content != null)
                        {
                            tab.Note.Content = tab.Content;
                        }
                        var state = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                        if (UI.FeatureFlags.UseNewArchitecture && state != null)
                        {
                            await state.SaveNoteAsync(tab.Note.Id);
                        }
                        else
                        {
                            var noteOps = app?.ServiceProvider?.GetService(typeof(INoteOperationsService)) as INoteOperationsService;
                            if (noteOps != null)
                            {
                                await noteOps.SaveNoteAsync(tab.Note);
                            }
                        }
                        tab.IsDirty = false;
                    }
                }
            }
            catch { }
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
                    }
                }
                catch { }

                var closeService = (Application.Current as App)?.ServiceProvider?.GetService(typeof(ITabCloseService)) as ITabCloseService;
                if (closeService != null)
                {
                    var closed = await closeService.CloseTabWithPromptAsync(tab);
                    if (closed)
                    {
                        Pane?.Tabs.Remove(tab);

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

        private void LoadTabContent(ITabItem tab)
        {
            var container = PaneTabControl.ItemContainerGenerator.ContainerFromItem(tab) as TabItem;
            if (container == null) return;
            var contentPresenter = FindVisualChild<ContentPresenter>(container);
            if (contentPresenter == null) return;
            var ste = FindVisualChild<SmartTextEditor>(contentPresenter);
            if (ste == null) return;
            ste.Text = tab.Content ?? string.Empty;
        }

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


