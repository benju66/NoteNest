using System.Collections.Generic;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Generates auto-tags from folder paths following the 2-tag project-only strategy.
    /// Strategy: Find first project pattern (NN-NNN - Name), generate 2 tags, stop.
    /// If no project found, tag top-level category only.
    /// </summary>
    public interface ITagGeneratorService
    {
        /// <summary>
        /// Generate auto-tags from a display path.
        /// </summary>
        /// <param name="displayPath">Display path from TreeNode (e.g., "Projects/25-117 - OP III/Daily Notes/Meeting.rtf")</param>
        /// <returns>List of generated tags (typically 2 for projects, 1 for non-projects, 0 for root-level files)</returns>
        /// <example>
        /// Input:  "Projects/25-117 - OP III/Daily Notes/Meeting.rtf"
        /// Output: ["25-117-OP-III", "25-117"]
        /// </example>
        List<string> GenerateFromPath(string displayPath);

        /// <summary>
        /// Check if a folder name matches the project pattern (NN-NNN - Name).
        /// </summary>
        /// <param name="folderName">Folder name to check</param>
        /// <returns>True if matches project pattern</returns>
        bool IsProjectFolder(string folderName);
    }
}

