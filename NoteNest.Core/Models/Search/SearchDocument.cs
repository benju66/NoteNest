using System;
using System.Collections.Generic;

namespace NoteNest.Core.Models.Search
{
    /// <summary>
    /// Document model for FTS5 indexing
    /// Represents a note's searchable content and metadata
    /// </summary>
    public class SearchDocument
    {
        /// <summary>
        /// Unique identifier for the note
        /// </summary>
        public string NoteId { get; set; } = string.Empty;

        /// <summary>
        /// Note title - searchable and used for display
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Plain text content extracted from RTF - primary search target
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Category UUID for filtering search results
        /// </summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>
        /// Full file path for result navigation
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Last modification timestamp for sorting
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// File size in bytes for metadata
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Creation timestamp
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Tags associated with the note (for future extensibility)
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Convert to FTS5 insertion parameters
        /// </summary>
        public object[] ToFtsParameters()
        {
            return new object[]
            {
                Title ?? string.Empty,
                Content ?? string.Empty, 
                CategoryId ?? string.Empty,
                FilePath ?? string.Empty,
                NoteId ?? string.Empty,
                LastModified.ToString("O") // ISO 8601 format
            };
        }

        /// <summary>
        /// Create search document from note file information
        /// </summary>
        public static SearchDocument FromNoteModel(NoteModel noteModel, string plainTextContent)
        {
            return new SearchDocument
            {
                NoteId = noteModel.Id ?? Guid.NewGuid().ToString(),
                Title = noteModel.Title ?? System.IO.Path.GetFileNameWithoutExtension(noteModel.FilePath),
                Content = plainTextContent ?? string.Empty,
                CategoryId = noteModel.CategoryId ?? string.Empty,
                FilePath = noteModel.FilePath ?? string.Empty,
                LastModified = noteModel.LastModified,
                FileSize = GetFileSizeOrDefault(noteModel.FilePath),
                CreatedDate = GetFileCreationTimeOrDefault(noteModel.FilePath)
            };
        }

        private static long GetFileSizeOrDefault(string filePath)
        {
            try
            {
                return !string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath) 
                    ? new System.IO.FileInfo(filePath).Length 
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static DateTime GetFileCreationTimeOrDefault(string filePath)
        {
            try
            {
                return !string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath)
                    ? System.IO.File.GetCreationTime(filePath)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
