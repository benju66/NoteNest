using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Enhanced workspace service interface prepared for split view implementation
    /// </summary>
    public interface IWorkspaceService
    {
        #region Current Tab Management
        
        ObservableCollection<ITabItem> OpenTabs { get; }
        ITabItem? SelectedTab { get; set; }
        bool HasUnsavedChanges { get; }
        
        #endregion
        
        #region Events
        
        event EventHandler<TabChangedEventArgs>? TabSelectionChanged;
        event EventHandler<TabEventArgs>? TabOpened;
        event EventHandler<TabEventArgs>? TabClosed;
        
        #endregion
        
        #region Tab Operations
        
        Task<ITabItem> OpenNoteAsync(NoteModel note);
        Task<bool> CloseTabAsync(ITabItem tab);
        Task<bool> CloseAllTabsAsync();
        Task SaveAllTabsAsync();
        
        #endregion
        
        #region Tab Queries
        
        ITabItem? FindTabByNote(NoteModel note);
        ITabItem? FindTabByPath(string filePath);
        
        #endregion
        
        #region Future Split View Support (Interface prepared)
        
        // These methods will be implemented when split view is added
        // For now, they provide a stable interface
        
        /// <summary>
        /// Gets all active workspace panes (for future split view)
        /// Currently returns single pane equivalent
        /// </summary>
        System.Collections.Generic.IEnumerable<object> GetActivePanes();
        
        /// <summary>
        /// Moves tab to different pane (for future split view)
        /// Currently no-op but interface stable
        /// </summary>
        Task<bool> MoveTabToPaneAsync(ITabItem tab, object targetPane);
        
        #endregion
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