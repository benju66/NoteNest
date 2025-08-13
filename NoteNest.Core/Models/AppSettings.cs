using System.Collections.Generic;

namespace NoteNest.Core.Models
{
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

        public AppSettings()
        {
            DefaultNotePath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "NoteNest");
            MetadataPath = System.IO.Path.Combine(DefaultNotePath, ".metadata");
            AutoSave = true;
            AutoSaveInterval = 30;
            WordWrap = true;
            Theme = "System";
            FontSize = 14;
            FontFamily = "Consolas";
            ShowLineNumbers = false;
            ShowStatusBar = true;
            HighlightCurrentLine = true;
            TabSize = 4;
            InsertSpaces = true;
            CreateBackup = true;
            MaxBackups = 5;
            ShowWelcomeScreen = true;
            CheckForUpdates = true;
            RecentFiles = new List<string>();
            WindowSettings = new WindowSettings();
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
