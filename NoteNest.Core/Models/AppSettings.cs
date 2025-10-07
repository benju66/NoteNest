using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    public enum StorageMode
    {
        Local,
        OneDrive,
        Custom
    }

    public class AppSettings
    {
        public string DefaultNotePath { get; set; }
        public string MetadataPath { get; set; }
        public bool AutoSave { get; set; }
        public int AutoSaveInterval { get; set; } // in seconds
        public string Theme { get; set; }
        public bool ShowStatusBar { get; set; }
        public bool CreateBackup { get; set; }
        public int MaxBackups { get; set; }
        public bool ShowWelcomeScreen { get; set; }
        public bool CheckForUpdates { get; set; }
        public List<string> RecentFiles { get; set; }
        public WindowSettings WindowSettings { get; set; }
        public int MaxRecentFiles { get; set; } = 20;

        public StorageMode StorageMode { get; set; } = StorageMode.Local;
        public string CustomNotesPath { get; set; }
        public bool AutoDetectOneDrive { get; set; } = true;
        // Format settings - RTF-only architecture
        public NoteFormat DefaultNoteFormat { get; set; } = NoteFormat.RTF;


        // Safety settings
        public bool RequireBackupBeforeConversion { get; set; } = true;
        public bool ShowConversionPreview { get; set; } = true;
        public int MaxConversionBatchSize { get; set; } = 50;
        public bool EnableTaskPanel { get; set; } = true;
        public bool ParseMarkdownCheckboxes { get; set; } = true;
        public string QuickNoteHotkey { get; set; } = "Win+N";
        public string QuickTaskHotkey { get; set; } = "Win+T";

        // UX preferences
        public bool SingleClickOpenNotes { get; set; } = true;
        public bool AutoSaveOnTabSwitch { get; set; } = true;
        public bool AutoSaveOnClose { get; set; } = true;
        public bool AutoSaveOnFocusLost { get; set; } = true;
        public bool ForceSaveOnExit { get; set; } = true;
        public int AutoSaveIdleMs { get; set; } = 2000;
        public bool ShowTreeDirtyDot { get; set; } = true;
        public bool RightPanelSplitEnabled { get; set; } = false;
        public double RightPanelTopHeight { get; set; } = 250;
        public double RightPanelBottomHeight { get; set; } = 250;
        public bool IsEditorCollapsed { get; set; } = false;
        public double EditorColumnWidth { get; set; } = 1.0; // star size placeholder

        // File watcher & caching configuration
        public int FileWatcherDebounceMs { get; set; } = 500;
        public int FileWatcherBufferKB { get; set; } = 64;
        public int ContentCacheMaxMB { get; set; } = 50;
        public int ContentCacheExpirationMinutes { get; set; } = 10;
        public int ContentCacheCleanupMinutes { get; set; } = 5;
        public int SearchIndexContentWordLimit { get; set; } = 500;
        public int SettingsSaveDebounceMs { get; set; } = 5000;

        // Adaptive auto-save behavior
        public bool AdaptiveAutoSaveEnabled { get; set; } = true;
        public string AdaptiveAutoSavePreset { get; set; } = "Balanced"; // Conservative | Balanced | Aggressive


        // Session persistence
        public bool RestoreTabs { get; set; } = true;
        public List<string> ExpandedCategoryIds { get; set; } = new();

        // Tree configuration settings
        public int MaxTreeDepth { get; set; } = 10;
        public bool EnableTreeValidation { get; set; } = true;
        public bool EnableDragDrop { get; set; } = true;
        public bool EnableTreeUndo { get; set; } = false;  // Start disabled as recommended
        public bool CacheTreeData { get; set; } = true;
        public int TreeCacheMinutes { get; set; } = 5;
        public bool WarmTreeCacheOnStartup { get; set; } = false;  // Conservative default

        // Feature flags
        public EditorSettings EditorSettings { get; set; } = new EditorSettings();
        
        // RTF-integrated save system is now the only system (feature flag removed)

        public AppSettings()
        {
            // Initialize collections/objects to prevent null reference issues.
            RecentFiles = new List<string>();
            WindowSettings = new WindowSettings();
            // Do not set paths or other defaults here.
        }
    }

    public class WindowSettings
    {
        public double Width { get; set; } = 900;
        public double Height { get; set; } = 600;
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public bool IsMaximized { get; set; }
    }
}
