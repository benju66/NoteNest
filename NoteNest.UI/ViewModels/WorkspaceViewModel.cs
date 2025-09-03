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
                    // New architecture: SelectedTab is UI-only; service selection sync not needed
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

            // Subscribe to WorkspaceStateService events
            try
            {
                var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                if (state != null)
                {
                    state.NoteStateChanged += (s, e) =>
                    {
                        var match = _uiTabs.FirstOrDefault(t => t.Note?.Id == e.NoteId);
                        if (match != null)
                        {
                            match.IsDirty = e.IsDirty;
                            // Keep model dirty indicator in sync for tree dot
                            try { match.Note.IsDirty = e.IsDirty; } catch { }
                            System.Diagnostics.Debug.WriteLine($"[VM] NoteStateChanged noteId={e.NoteId} isDirty={e.IsDirty}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[VM][WARN] NoteStateChanged for unknown UI tab noteId={e.NoteId}");
                        }
                    };
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[VM][ERROR] Subscribing NoteStateChanged failed: {ex.Message}"); }
            
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
            // Keep service collection in sync using UI NoteTabItem (implements ITabItem)
            if (!_workspaceService.OpenTabs.Contains(noteTab))
            {
                _workspaceService.OpenTabs.Add(noteTab);
            }
        }
        
        public void RemoveTab(NoteTabItem noteTab)
        {
            _uiTabs.Remove(noteTab);
            try
            {
                if (noteTab is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch { }
            
            if (_workspaceService.OpenTabs.Contains(noteTab))
            {
                _workspaceService.OpenTabs.Remove(noteTab);
            }
        }
        
        private void OnServiceTabOpened(object sender, TabEventArgs e)
        {
            // Map service ITabItem to UI NoteTabItem where possible
            if (e.Tab?.Note != null)
            {
                var existing = _uiTabs.FirstOrDefault(t => ReferenceEquals(t.Note, e.Tab.Note));
                if (existing == null)
                {
                    var uiTab = new NoteTabItem(e.Tab.Note);
                try
                {
                    var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    if (state != null && state.OpenNotes.TryGetValue(e.Tab.Note.Id, out var wn))
                    {
                        uiTab.IsDirty = wn.IsDirty;
                    }
                }
                catch { }
                    _uiTabs.Add(uiTab);
                    System.Diagnostics.Debug.WriteLine($"[VM] TabOpened noteId={e.Tab.Note.Id} title={e.Tab.Note.Title}");
                }
            }
        }
        
        private void OnServiceTabClosed(object sender, TabEventArgs e)
        {
            if (e.Tab?.Note != null)
            {
                var ui = _uiTabs.FirstOrDefault(t => ReferenceEquals(t.Note, e.Tab.Note));
                if (ui != null) _uiTabs.Remove(ui);
                System.Diagnostics.Debug.WriteLine($"[VM] TabClosed noteId={e.Tab.Note.Id} title={e.Tab.Note.Title}");
            }
        }
        
        private void OnServiceTabSelectionChanged(object sender, TabChangedEventArgs e)
        {
            // Sync selection from service to UI by note identity
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
            // Keep UI collection in sync with service collection by note reference
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is ITabItem tab && tab.Note != null)
                    {
                        var existing = _uiTabs.FirstOrDefault(t => ReferenceEquals(t.Note, tab.Note));
                        if (existing == null)
                        {
                            _uiTabs.Add(new NoteTabItem(tab.Note));
                        }
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is ITabItem tab && tab.Note != null)
                    {
                        var existing = _uiTabs.FirstOrDefault(t => ReferenceEquals(t.Note, tab.Note));
                        if (existing != null)
                        {
                            _uiTabs.Remove(existing);
                        }
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
            foreach (var tab in _workspaceService.OpenTabs)
            {
                var existing = _uiTabs.FirstOrDefault(t => ReferenceEquals(t.Note, tab.Note));
                if (existing == null)
                {
                    _uiTabs.Add(new NoteTabItem(tab.Note));
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