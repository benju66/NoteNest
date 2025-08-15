using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    /// <summary>
    /// Provides UI-bindable workspace state synchronized with WorkspaceService
    /// </summary>
    public class WorkspaceViewModel : ViewModelBase
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly ObservableCollection<NoteTabItem> _uiTabs;
        private NoteTabItem? _selectedTab;
        
        public ObservableCollection<NoteTabItem> OpenTabs => _uiTabs;
        
        public NoteTabItem? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    // Sync with service
                    if (_workspaceService != null && value != null)
                    {
                        var adapter = _workspaceService.OpenTabs
                            .OfType<TabItemAdapter>()
                            .FirstOrDefault(a => ReferenceEquals(a.UnderlyingTab, value));
                        
                        if (adapter != null)
                        {
                            _workspaceService.SelectedTab = adapter;
                        }
                    }
                }
            }
        }
        
        public WorkspaceViewModel(IWorkspaceService workspaceService)
        {
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _uiTabs = new ObservableCollection<NoteTabItem>();
            
            // Subscribe to service events
            _workspaceService.TabOpened += OnServiceTabOpened;
            _workspaceService.TabClosed += OnServiceTabClosed;
            _workspaceService.TabSelectionChanged += OnServiceTabSelectionChanged;
            
            // Subscribe to service collection changes
            if (_workspaceService.OpenTabs is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OnServiceTabsCollectionChanged;
            }
        }
        
        public void AddTab(NoteTabItem noteTab)
        {
            _uiTabs.Add(noteTab);
            
            // Add adapter to service
            var adapter = new TabItemAdapter(noteTab);
            _workspaceService.OpenTabs.Add(adapter);
        }
        
        public void RemoveTab(NoteTabItem noteTab)
        {
            _uiTabs.Remove(noteTab);
            
            // Remove from service
            var adapter = _workspaceService.OpenTabs
                .OfType<TabItemAdapter>()
                .FirstOrDefault(a => ReferenceEquals(a.UnderlyingTab, noteTab));
            
            if (adapter != null)
            {
                _workspaceService.OpenTabs.Remove(adapter);
                adapter.Dispose();
            }
        }
        
        private void OnServiceTabOpened(object sender, TabEventArgs e)
        {
            // Handle if service opens a tab directly
        }
        
        private void OnServiceTabClosed(object sender, TabEventArgs e)
        {
            // Handle if service closes a tab directly
        }
        
        private void OnServiceTabSelectionChanged(object sender, TabChangedEventArgs e)
        {
            // Sync selection from service to UI
            if (e.NewTab is TabItemAdapter adapter)
            {
                SelectedTab = adapter.UnderlyingTab;
            }
        }
        
        private void OnServiceTabsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Keep UI collection in sync with service collection
        }
    }
}