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
    public class WorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly ObservableCollection<NoteTabItem> _uiTabs;
        private NoteTabItem? _selectedTab;
        private bool _disposed;
        
        public ObservableCollection<NoteTabItem> OpenTabs => _uiTabs;
        
        public NoteTabItem? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    // Sync selection with service
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

            // Initialize from any existing service tabs
            SyncFromService();
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
            // If service opened a UI-backed tab (adapter), mirror it in UI list
            if (e.Tab is TabItemAdapter adapter && !_uiTabs.Contains(adapter.UnderlyingTab))
            {
                _uiTabs.Add(adapter.UnderlyingTab);
            }
        }
        
        private void OnServiceTabClosed(object sender, TabEventArgs e)
        {
            if (e.Tab is TabItemAdapter adapter && _uiTabs.Contains(adapter.UnderlyingTab))
            {
                _uiTabs.Remove(adapter.UnderlyingTab);
            }
        }
        
        private void OnServiceTabSelectionChanged(object sender, TabChangedEventArgs e)
        {
            // Sync selection from service to UI
            if (e.NewTab is TabItemAdapter adapter)
            {
                SelectedTab = adapter.UnderlyingTab;
                return;
            }

            // Fallback: match by note if service sent a different ITabItem implementation
            var serviceTab = e.NewTab;
            if (serviceTab?.Note != null)
            {
                var match = _uiTabs.FirstOrDefault(t =>
                    t.Note?.FilePath?.Equals(serviceTab.Note.FilePath, StringComparison.OrdinalIgnoreCase) == true);
                if (match != null)
                {
                    SelectedTab = match;
                }
            }
        }
        
        private void OnServiceTabsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Keep UI collection in sync with service collection
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is TabItemAdapter adapter && !_uiTabs.Contains(adapter.UnderlyingTab))
                    {
                        _uiTabs.Add(adapter.UnderlyingTab);
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is TabItemAdapter adapter && _uiTabs.Contains(adapter.UnderlyingTab))
                    {
                        _uiTabs.Remove(adapter.UnderlyingTab);
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _uiTabs.Clear();
                SyncFromService();
            }
        }

        private void SyncFromService()
        {
            foreach (var adapter in _workspaceService.OpenTabs.OfType<TabItemAdapter>())
            {
                if (!_uiTabs.Contains(adapter.UnderlyingTab))
                {
                    _uiTabs.Add(adapter.UnderlyingTab);
                }
            }
        }

        public NoteTabItem? FindTabByNote(NoteModel note)
        {
            if (note == null) return null;
            return _uiTabs.FirstOrDefault(t =>
                t.Note?.FilePath?.Equals(note.FilePath, StringComparison.OrdinalIgnoreCase) == true);
        }

        public NoteTabItem? FindTabByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            return _uiTabs.FirstOrDefault(t =>
                t.Note?.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_workspaceService != null)
                {
                    _workspaceService.TabOpened -= OnServiceTabOpened;
                    _workspaceService.TabClosed -= OnServiceTabClosed;
                    _workspaceService.TabSelectionChanged -= OnServiceTabSelectionChanged;
                    if (_workspaceService.OpenTabs is INotifyCollectionChanged ncc)
                    {
                        ncc.CollectionChanged -= OnServiceTabsCollectionChanged;
                    }
                }
                _uiTabs.Clear();
                _disposed = true;
            }
        }
    }
}