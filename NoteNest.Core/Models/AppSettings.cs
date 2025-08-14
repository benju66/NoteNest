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

        public StorageMode StorageMode { get; set; } = StorageMode.Local;
        public string CustomNotesPath { get; set; }
        public bool AutoDetectOneDrive { get; set; } = true;
        public string DefaultNoteFormat { get; set; } = ".txt";
        public bool EnableTaskPanel { get; set; } = true;
        public bool ParseMarkdownCheckboxes { get; set; } = true;
        public string QuickNoteHotkey { get; set; } = "Win+N";
        public string QuickTaskHotkey { get; set; } = "Win+T";

        public AppSettings()
        {
            // Only set defaults if not already set (for new installations)
            if (string.IsNullOrEmpty(DefaultNotePath))
            {
                DefaultNotePath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
            }
            
            if (string.IsNullOrEmpty(MetadataPath))
            {
                MetadataPath = System.IO.Path.Combine(DefaultNotePath, ".metadata");
            }
            
            // Initialize other defaults only if not set
            AutoSave = AutoSave == default ? true : AutoSave;
            AutoSaveInterval = AutoSaveInterval == 0 ? 30 : AutoSaveInterval;
            WordWrap = WordWrap == default ? true : WordWrap;
            Theme = Theme ?? "System";
            FontSize = FontSize == 0 ? 14 : FontSize;
            FontFamily = FontFamily ?? "Consolas";
            ShowLineNumbers = ShowLineNumbers;
            ShowStatusBar = ShowStatusBar == default ? true : ShowStatusBar;
            HighlightCurrentLine = HighlightCurrentLine == default ? true : HighlightCurrentLine;
            TabSize = TabSize == 0 ? 4 : TabSize;
            InsertSpaces = InsertSpaces == default ? true : InsertSpaces;
            CreateBackup = CreateBackup == default ? true : CreateBackup;
            MaxBackups = MaxBackups == 0 ? 5 : MaxBackups;
            ShowWelcomeScreen = ShowWelcomeScreen == default ? true : ShowWelcomeScreen;
            CheckForUpdates = CheckForUpdates == default ? true : CheckForUpdates;
            RecentFiles = RecentFiles ?? new List<string>();
            WindowSettings = WindowSettings ?? new WindowSettings();
            
            // New settings defaults
            StorageMode = StorageMode == default ? StorageMode.Local : StorageMode;
            DefaultNoteFormat = DefaultNoteFormat ?? ".txt";
            EnableTaskPanel = EnableTaskPanel == default ? true : EnableTaskPanel;
            ParseMarkdownCheckboxes = ParseMarkdownCheckboxes == default ? true : ParseMarkdownCheckboxes;
            QuickNoteHotkey = QuickNoteHotkey ?? "Win+N";
            QuickTaskHotkey = QuickTaskHotkey ?? "Win+T";
            AutoDetectOneDrive = AutoDetectOneDrive == default ? true : AutoDetectOneDrive;
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
