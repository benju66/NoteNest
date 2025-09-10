namespace NoteNest.Core.Services
{
    public class SaveConfiguration
    {
        public int AutoSaveDelayMs { get; set; } = 2000;      // 2 seconds default
        public int MaxAutoSaveDelayMs { get; set; } = 10000;   // 10 seconds max
        public int MaxConcurrentSaves { get; set; } = 3;       // Parallel save limit
        public int InactiveCleanupMinutes { get; set; } = 30;  // Memory cleanup
    }
}
