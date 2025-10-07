using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    /// <summary>
    /// Persisted workspace state for tab and pane layout restoration
    /// Enhanced with detached window support (Tear-Out feature)
    /// Version 2: Adds DetachedWindows collection
    /// </summary>
    public class WorkspaceState
    {
        /// <summary>
        /// Schema version for future compatibility
        /// Version 1: Basic pane/tab support
        /// Version 2: Detached windows support
        /// </summary>
        public int Version { get; set; } = 2;
        
        /// <summary>
        /// Number of panes in main window (1 = single, 2 = split)
        /// </summary>
        public int PaneCount { get; set; } = 1;
        
        /// <summary>
        /// Index of the active pane in main window (0 or 1)
        /// </summary>
        public int ActivePaneIndex { get; set; } = 0;
        
        /// <summary>
        /// State of each pane in main window
        /// </summary>
        public List<PaneState> Panes { get; set; } = new();
        
        /// <summary>
        /// State of detached windows (Version 2+)
        /// </summary>
        public List<DetachedWindowState> DetachedWindows { get; set; } = new();
        
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
    
    /// <summary>
    /// State of a detached window (Version 2+)
    /// </summary>
    public class DetachedWindowState
    {
        /// <summary>
        /// Unique identifier for this detached window
        /// </summary>
        public string WindowId { get; set; } = "";
        
        /// <summary>
        /// Window title (e.g., "NoteNest - Detached")
        /// </summary>
        public string Title { get; set; } = "";
        
        /// <summary>
        /// Window position and size
        /// </summary>
        public WindowBounds Bounds { get; set; } = new();
        
        /// <summary>
        /// Tabs in this detached window (in order)
        /// </summary>
        public List<TabState> Tabs { get; set; } = new();
        
        /// <summary>
        /// ID of the selected tab in this window
        /// </summary>
        public string? ActiveTabId { get; set; }
        
        /// <summary>
        /// Whether this window was maximized when saved
        /// </summary>
        public bool IsMaximized { get; set; }
        
        /// <summary>
        /// Monitor index where window was located (for multi-monitor)
        /// -1 if unknown or primary monitor
        /// </summary>
        public int MonitorIndex { get; set; } = -1;
    }
    
    /// <summary>
    /// Window position and size information
    /// </summary>
    public class WindowBounds
    {
        /// <summary>
        /// Left position (screen coordinates)
        /// </summary>
        public double Left { get; set; } = 100;
        
        /// <summary>
        /// Top position (screen coordinates)
        /// </summary>
        public double Top { get; set; } = 100;
        
        /// <summary>
        /// Window width
        /// </summary>
        public double Width { get; set; } = 800;
        
        /// <summary>
        /// Window height
        /// </summary>
        public double Height { get; set; } = 600;
    }
}

