using System;
using System.Collections.ObjectModel;
using System.Linq;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Workspace
{
    /// <summary>
    /// Represents a single pane that can contain multiple tabs
    /// Used for split view support (future Milestone 2)
    /// </summary>
    public class PaneViewModel : ViewModelBase
    {
        private TabViewModel _selectedTab;
        private bool _isActive;
        
        public string Id { get; }
        public ObservableCollection<TabViewModel> Tabs { get; }
        
        public TabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                var previousTab = _selectedTab;
                if (SetProperty(ref _selectedTab, value))
                {
                    // Save previous tab if it was dirty before switching
                    if (previousTab != null && previousTab.IsDirty)
                    {
                        _ = previousTab.SaveAsync(); // Fire and forget
                        System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Auto-saving dirty tab on switch: {previousTab.Title}");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Selected tab changed: {value?.Title ?? "null"}");
                }
            }
        }
        
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
        
        public bool HasTabs => Tabs?.Count > 0;
        
        public PaneViewModel() : this(Guid.NewGuid().ToString())
        {
        }
        
        public PaneViewModel(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Tabs = new ObservableCollection<TabViewModel>();
            
            // Subscribe to collection changes to update HasTabs
            Tabs.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasTabs));
                
                // Auto-select first tab if nothing selected
                if (SelectedTab == null && Tabs.Count > 0)
                {
                    SelectedTab = Tabs.First();
                }
            };
            
            System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Created: {Id}");
        }
        
        /// <summary>
        /// Add tab and optionally select it
        /// </summary>
        public void AddTab(TabViewModel tab, bool select = true)
        {
            if (tab == null || Tabs.Contains(tab)) return;
            
            Tabs.Add(tab);
            
            if (select)
            {
                SelectedTab = tab;
            }
            
            System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Added tab: {tab.Title} (Total: {Tabs.Count})");
        }
        
        /// <summary>
        /// Insert tab at specific index (used for drag & drop)
        /// Part of Milestone 2B: Drag & Drop
        /// </summary>
        public void InsertTab(int index, TabViewModel tab, bool select = true)
        {
            if (tab == null || Tabs.Contains(tab)) return;
            
            // Clamp index to valid range
            index = Math.Max(0, Math.Min(index, Tabs.Count));
            
            Tabs.Insert(index, tab);
            
            if (select)
            {
                SelectedTab = tab;
            }
            
            System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Inserted tab: {tab.Title} at index {index} (Total: {Tabs.Count})");
        }
        
        /// <summary>
        /// Remove tab and handle selection
        /// </summary>
        public void RemoveTab(TabViewModel tab)
        {
            if (tab == null || !Tabs.Contains(tab)) return;
            
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            
            // Select adjacent tab if the removed tab was selected
            if (SelectedTab == tab && Tabs.Count > 0)
            {
                // Select previous tab, or first tab if we removed the first one
                var newIndex = Math.Max(0, index - 1);
                SelectedTab = Tabs[newIndex];
            }
            
            // Dispose the tab
            tab.Dispose();
            
            System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Removed tab: {tab.Title} (Remaining: {Tabs.Count})");
        }
        
        /// <summary>
        /// Remove tab WITHOUT disposing (used for move operations between panes)
        /// The tab remains alive and functional in the target pane
        /// Part of Tier 1 Features: Bug fix for cross-pane moves
        /// </summary>
        public void RemoveTabWithoutDispose(TabViewModel tab)
        {
            if (tab == null || !Tabs.Contains(tab)) return;
            
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            
            // Select adjacent tab if the removed tab was selected
            if (SelectedTab == tab && Tabs.Count > 0)
            {
                // Select previous tab, or first tab if we removed the first one
                var newIndex = Math.Max(0, index - 1);
                SelectedTab = Tabs[newIndex];
            }
            
            // NOTE: Do NOT dispose - tab is being moved, not closed
            
            System.Diagnostics.Debug.WriteLine($"[PaneViewModel] Removed tab for move: {tab.Title} (Remaining: {Tabs.Count})");
        }
        
        /// <summary>
        /// Find tab by TabId
        /// </summary>
        public TabViewModel FindTabById(string tabId)
        {
            return Tabs.FirstOrDefault(t => t.TabId == tabId);
        }
        
        /// <summary>
        /// Find tab by file path
        /// </summary>
        public TabViewModel FindTabByPath(string filePath)
        {
            return Tabs.FirstOrDefault(t => 
                t.Note?.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);
        }
    }
}

