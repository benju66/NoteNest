namespace NoteNest.Core.Models
{
    public class EditorSettings
    {
        // Visual Settings
        public string FontFamily { get; set; } = "Calibri";
        public double FontSize { get; set; } = 14;
        public bool WordWrap { get; set; } = true;
        public double LineHeight { get; set; } = 1.4;
        public bool ShowLineNumbers { get; set; } = false;
        public bool HighlightCurrentLine { get; set; } = false;
        
        // Formatting Settings
        public int TabSize { get; set; } = 4;
        public bool InsertSpaces { get; set; } = true;
        public bool ShowFormattingToolbar { get; set; } = true;
        
        // List Behavior
        public bool EnhancedListHandling { get; set; } = true;
        public int ListIndentSize { get; set; } = 20;
        public int RenumberingDebounceMs { get; set; } = 500;
        
        // Markdown Settings
        public bool ParseTaskLists { get; set; } = true;
        public bool AutoLinkUrls { get; set; } = true;
        public bool EnableEmphasisExtras { get; set; } = true;
        
        // Performance Settings
        public int MaxDocumentSizeMB { get; set; } = 10;
        public bool EnableSpellCheck { get; set; } = true;
        public string SpellCheckLanguage { get; set; } = "en-US";
    }
}
