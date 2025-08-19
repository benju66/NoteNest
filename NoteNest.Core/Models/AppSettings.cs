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
        public bool WordWrap { get; set; }
        public string Theme { get; set; }
        public int FontSize { get; set; }
        public string FontFamily { get; set; }
        public bool ShowLineNumbers { get; set; }
        public bool ShowStatusBar { get; set; }
        public bool HighlightCurrentLine { get; set; }
        public int TabSize { get; set; }
        public bool InsertSpaces { get; set; }
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
        public string DefaultNoteFormat { get; set; } = ".txt";
        public bool EnableTaskPanel { get; set; } = true;
        public bool ParseMarkdownCheckboxes { get; set; } = true;
        public string QuickNoteHotkey { get; set; } = "Win+N";
        public string QuickTaskHotkey { get; set; } = "Win+T";

        // UX preferences
        public bool SingleClickOpenNotes { get; set; } = true;
        public bool AutoSaveOnTabSwitch { get; set; } = true;
        public bool AutoSaveOnClose { get; set; } = true;
        public bool AutoSaveOnFocusLost { get; set; } = true;
        public int AutoSaveIdleMs { get; set; } = 2000;
        public bool ShowTreeDirtyDot { get; set; } = true;

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
