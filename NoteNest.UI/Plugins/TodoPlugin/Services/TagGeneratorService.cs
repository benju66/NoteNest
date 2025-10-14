using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Generates auto-tags from folder paths using the 2-tag project-only strategy.
    /// 
    /// Strategy:
    /// 1. Scan path for project pattern: "NN-NNN - Project Name"
    /// 2. If found: Generate 2 tags (full project tag + quick search code), STOP
    /// 3. If not found: Generate 1 tag (top-level category), STOP
    /// 4. Skip everything else (no subfolder clutter)
    /// 
    /// Examples:
    /// - "Projects/25-117 - OP III/Daily Notes/Meeting.rtf" → ["25-117-OP-III", "25-117"]
    /// - "Personal/Goals/2025/Q1.rtf" → ["Personal"]
    /// - "Quick-Notes.rtf" → []
    /// </summary>
    public class TagGeneratorService : ITagGeneratorService
    {
        // Project pattern: "NN-NNN - Name" (e.g., "25-117 - OP III")
        // Groups: (1) Year/Series (25), (2) Number (117), (3) Project Name (OP III)
        private static readonly Regex ProjectPattern = 
            new Regex(@"^(\d{2})-(\d{3})\s*-\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Generate auto-tags from display path.
        /// </summary>
        public List<string> GenerateFromPath(string displayPath)
        {
            var tags = new List<string>();

            if (string.IsNullOrWhiteSpace(displayPath))
                return tags;

            // Remove filename, get folder path
            var folderPath = Path.GetDirectoryName(displayPath);
            if (string.IsNullOrEmpty(folderPath))
                return tags;  // No folders = no tags

            // Split into folders
            var folders = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (folders.Length == 0)
                return tags;

            // STRATEGY: Find FIRST project pattern, tag it, STOP
            bool projectFound = false;

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;

                // Check if project pattern
                var match = ProjectPattern.Match(folder);
                if (match.Success)
                {
                    // Extract components
                    var projectCode = $"{match.Groups[1].Value}-{match.Groups[2].Value}";
                    var projectName = match.Groups[3].Value.Trim();

                    // Generate two tags:
                    // 1. Full project tag: "25-117-OP-III"
                    var fullTag = $"{projectCode}-{NormalizeName(projectName)}";
                    tags.Add(fullTag);

                    // 2. Quick search code: "25-117"
                    tags.Add(projectCode);

                    projectFound = true;
                    break;  // ← STOP! Don't process more folders
                }
            }

            // If no project found, tag top-level category only
            if (!projectFound && folders.Length > 0)
            {
                var topLevel = folders[0];
                if (!string.IsNullOrWhiteSpace(topLevel))
                {
                    tags.Add(NormalizeName(topLevel));  // "Personal", "Work", etc.
                }
            }

            // Return unique tags (should already be unique, but just in case)
            return tags.Distinct().ToList();
        }

        /// <summary>
        /// Check if folder name matches project pattern.
        /// </summary>
        public bool IsProjectFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return false;

            return ProjectPattern.IsMatch(folderName);
        }

        /// <summary>
        /// Normalize folder/project name for use as tag.
        /// Rules:
        /// - Replace spaces with hyphens
        /// - Keep alphanumerics, ampersands, hyphens
        /// - Remove other special characters
        /// - Collapse multiple hyphens
        /// - Trim leading/trailing hyphens
        /// </summary>
        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // 1. Trim whitespace
            name = name.Trim();

            // 2. Replace spaces with hyphens
            name = name.Replace(' ', '-');

            // 3. Remove/replace special characters (keep alphanumerics, &, -)
            // \w = alphanumerics + underscore
            // We allow: letters, numbers, hyphens, ampersands
            name = Regex.Replace(name, @"[^\w&-]", "-");

            // 4. Replace underscores with hyphens (consistency)
            name = name.Replace('_', '-');

            // 5. Collapse multiple hyphens into one
            name = Regex.Replace(name, @"-+", "-");

            // 6. Remove leading/trailing hyphens
            name = name.Trim('-');

            return name;
        }
    }
}

