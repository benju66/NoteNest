using System.Collections.Generic;

namespace NoteNest.UI.Plugins.TodoPlugin.Models
{
    /// <summary>
    /// Settings for todo extraction from notes.
    /// Future: Will be configurable via Settings UI.
    /// </summary>
    public class TodoExtractionSettings
    {
        // =========================================================================
        // SYNTAX PATTERNS
        // =========================================================================
        
        /// <summary>
        /// Enable bracket syntax: [todo text]
        /// </summary>
        public bool EnableBracketSyntax { get; set; } = true;
        
        /// <summary>
        /// Enable Markdown checkbox syntax: - [ ] todo text
        /// Future feature.
        /// </summary>
        public bool EnableMarkdownCheckboxes { get; set; } = false;
        
        /// <summary>
        /// Custom regex patterns for todo extraction.
        /// Future: Users can add their own patterns via UI.
        /// </summary>
        public List<string> CustomPatterns { get; set; } = new();
        
        // =========================================================================
        // FILTERING
        // =========================================================================
        
        /// <summary>
        /// Enable smart filtering (filter out likely non-todos).
        /// Currently minimal - only filters empty brackets.
        /// Future: More sophisticated filtering options.
        /// </summary>
        public bool EnableSmartFiltering { get; set; } = true;
        
        /// <summary>
        /// Patterns to exclude from extraction.
        /// Future: Configurable via Settings UI.
        /// Examples: ["x", "...", "tbd"]
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new() { "x", " ", "..." };
        
        /// <summary>
        /// Minimum confidence threshold (0.0 - 1.0).
        /// Candidates below this are ignored.
        /// Future: Adjustable slider in Settings UI.
        /// </summary>
        public double MinimumConfidence { get; set; } = 0.5;
        
        // =========================================================================
        // AUTO-CATEGORIZATION
        // =========================================================================
        
        /// <summary>
        /// Automatically categorize todos by note's parent category.
        /// </summary>
        public bool AutoCategorizeByNoteFolder { get; set; } = true;
        
        // =========================================================================
        // SYNC BEHAVIOR
        // =========================================================================
        
        /// <summary>
        /// Auto-sync todos when notes are saved.
        /// </summary>
        public bool AutoSyncOnNoteSave { get; set; } = true;
        
        /// <summary>
        /// Debounce delay for sync (milliseconds).
        /// Prevents excessive syncing during rapid edits.
        /// </summary>
        public int SyncDebounceMs { get; set; } = 500;
        
        // =========================================================================
        // FUTURE FEATURES (Placeholders)
        // =========================================================================
        
        /// <summary>
        /// Extract due dates from natural language (e.g., "tomorrow", "next Monday").
        /// Future feature - requires NLP parsing.
        /// </summary>
        public bool EnableNaturalLanguageDates { get; set; } = false;
        
        /// <summary>
        /// Extract priority from keywords (e.g., "urgent:", "high:").
        /// Future feature.
        /// </summary>
        public bool EnablePriorityKeywords { get; set; } = false;
        
        /// <summary>
        /// Extract tags from hashtags (e.g., #work, #personal).
        /// Future feature.
        /// </summary>
        public bool EnableHashtagTags { get; set; } = false;
        
        // =========================================================================
        // DEFAULT SETTINGS FACTORY
        // =========================================================================
        
        /// <summary>
        /// Create default settings (permissive - accept most brackets).
        /// </summary>
        public static TodoExtractionSettings CreateDefault()
        {
            return new TodoExtractionSettings
            {
                EnableBracketSyntax = true,
                EnableMarkdownCheckboxes = false,
                EnableSmartFiltering = true,
                MinimumConfidence = 0.5,
                AutoCategorizeByNoteFolder = true,
                AutoSyncOnNoteSave = true,
                SyncDebounceMs = 500
            };
        }
        
        /// <summary>
        /// Create strict settings (filters more aggressively).
        /// For users who use brackets for non-todo purposes.
        /// </summary>
        public static TodoExtractionSettings CreateStrict()
        {
            return new TodoExtractionSettings
            {
                EnableBracketSyntax = true,
                EnableSmartFiltering = true,
                MinimumConfidence = 0.8,  // Higher threshold
                ExcludePatterns = new List<string> { "x", " ", "...", "tbd", "n/a", "wip" }
            };
        }
        
        /// <summary>
        /// Create permissive settings (accepts everything).
        /// For users who want full control.
        /// </summary>
        public static TodoExtractionSettings CreatePermissive()
        {
            return new TodoExtractionSettings
            {
                EnableBracketSyntax = true,
                EnableSmartFiltering = false,  // No filtering!
                MinimumConfidence = 0.0,  // Accept all
                ExcludePatterns = new List<string>()  // Empty
            };
        }
    }
}

