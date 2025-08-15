using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    public interface IWorkspaceService
    {
        ObservableCollection<ITabItem> OpenTabs { get; }
        ITabItem? SelectedTab { get; set; }
        
        event EventHandler<TabChangedEventArgs>? TabSelectionChanged;
        event EventHandler<TabEventArgs>? TabOpened;
        event EventHandler<TabEventArgs>? TabClosed;
        
        Task<ITabItem> OpenNoteAsync(NoteModel note);
        Task<bool> CloseTabAsync(ITabItem tab);
        Task<bool> CloseAllTabsAsync();
        Task SaveAllTabsAsync();
        
        bool HasUnsavedChanges { get; }
        ITabItem? FindTabByNote(NoteModel note);
    }
    
    public interface ITabItem
    {
        string Id { get; }
        string Title { get; }
        NoteModel Note { get; }
        bool IsDirty { get; set; }
        string Content { get; set; }
    }
    
    public class TabChangedEventArgs : EventArgs
    {
        public ITabItem? OldTab { get; set; }
        public ITabItem? NewTab { get; set; }
    }
    
    public class TabEventArgs : EventArgs
    {
        public ITabItem? Tab { get; set; }
    }
}