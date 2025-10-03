using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    /// <summary>
    /// Persisted workspace state for tab and pane layout restoration
    /// Part of NEW clean architecture (Milestone 2A completion)
    /// </summary>
    public class WorkspaceState
    {
        /// <summary>
        /// Schema version for future compatibility
        /// </summary>
        public int Version { get; set; } = 1;
        
        /// <summary>
        /// Number of panes (1 = single, 2 = split)
        /// </summary>
        public int PaneCount { get; set; } = 1;
        
        /// <summary>
        /// Index of the active pane (0 or 1)
        /// </summary>
        public int ActivePaneIndex { get; set; } = 0;
        
        /// <summary>
        /// State of each pane
        /// </summary>
        public List<PaneState> Panes { get; set; } = new();
        
        /// <summary>
        /// When this state was last saved
        /// </summary>
        public DateTime LastSaved { get; set; }
    }
    
    /// <summary>
    /// State of a single pane
    /// </summary>
    public class PaneState
    {
        /// <summary>
        /// All tabs in this pane (in order)
        /// </summary>
        public List<TabState> Tabs { get; set; } = new();
        
        /// <summary>
        /// ID of the selected tab in this pane
        /// </summary>
        public string? ActiveTabId { get; set; }
    }
    
    /// <summary>
    /// State of a single tab
    /// </summary>
    public class TabState
    {
        /// <summary>
        /// Tab ID (SaveManager noteId)
        /// </summary>
        public string TabId { get; set; } = "";
        
        /// <summary>
        /// Full file path to the note
        /// </summary>
        public string FilePath { get; set; } = "";
        
        /// <summary>
        /// Note title (for debugging/logging)
        /// </summary>
        public string Title { get; set; } = "";
    }
}

