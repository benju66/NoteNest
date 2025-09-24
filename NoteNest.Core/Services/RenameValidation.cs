using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Unified validation service for rename operations across all contexts
    /// Eliminates inconsistent validation between TreeView, Context Menu, and Tab rename operations
    /// </summary>
    public static class RenameValidation
    {
        /// <summary>
        /// Validates a new note name with comprehensive checks
        /// </summary>
        /// <param name="newName">The proposed new name</param>
        /// <param name="currentName">The current name (for change detection)</param>
        /// <param name="siblingNames">Names of other notes in the same location</param>
        /// <param name="maxLength">Maximum allowed name length (default 255)</param>
        /// <returns>Error message if invalid, null if valid</returns>
        public static string ValidateNoteName(string newName, string currentName, 
                                            IEnumerable<string> siblingNames, 
                                            int maxLength = 255)
        {
            // Check for empty name
            if (string.IsNullOrWhiteSpace(newName))
                return "Note name cannot be empty.";
                
            // Check if name actually changed
            if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                return null; // No change - this is valid
                
            // Check length limit
            if (newName.Length > maxLength)
                return $"Note name too long (max {maxLength} characters).";
                
            // Check for invalid file system characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (newName.IndexOfAny(invalidChars) >= 0)
                return "Note name contains invalid characters.";
                
            // Check for Windows reserved names
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
                                 "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", 
                                 "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(newName.ToUpperInvariant()))
                return "Note name cannot be a Windows reserved name.";
                
            // Check for duplicates in same location
            if (siblingNames?.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)) == true)
                return "A note with this name already exists in this location.";
                
            return null; // Valid
        }
        
        /// <summary>
        /// Validates a new category name with comprehensive checks
        /// </summary>
        /// <param name="newName">The proposed new name</param>
        /// <param name="currentName">The current name (for change detection)</param>
        /// <param name="siblingNames">Names of other categories at the same level</param>
        /// <param name="maxLength">Maximum allowed name length (default 255)</param>
        /// <returns>Error message if invalid, null if valid</returns>
        public static string ValidateCategoryName(string newName, string currentName, 
                                                IEnumerable<string> siblingNames, 
                                                int maxLength = 255)
        {
            // Check for empty name
            if (string.IsNullOrWhiteSpace(newName))
                return "Category name cannot be empty.";
                
            // Check if name actually changed
            if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                return null; // No change - this is valid
                
            // Check length limit
            if (newName.Length > maxLength)
                return $"Category name too long (max {maxLength} characters).";
                
            // Check for invalid path characters (more restrictive than file names)
            var invalidChars = Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();
            if (newName.IndexOfAny(invalidChars) >= 0)
                return "Category name contains invalid characters.";
                
            // Check for Windows reserved names
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
                                 "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", 
                                 "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(newName.ToUpperInvariant()))
                return "Category name cannot be a Windows reserved name.";
                
            // Check for duplicates at same level
            if (siblingNames?.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)) == true)
                return "A category with this name already exists at this level.";
                
            return null; // Valid
        }
        
    }
}
