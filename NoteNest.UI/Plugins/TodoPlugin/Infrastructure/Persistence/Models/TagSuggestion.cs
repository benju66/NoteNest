namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Models
{
    /// <summary>
    /// DTO for tag suggestions (autocomplete).
    /// Includes usage count for ranking.
    /// </summary>
    public class TagSuggestion
    {
        public string Tag { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public string? Color { get; set; }
        public string? Icon { get; set; }
        public string? Category { get; set; }
    }
}

