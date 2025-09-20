using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Models.Search;

namespace NoteNest.UI.ViewModels
{
    /// <summary>
    /// View model for search results in the UI layer
    /// Provides UI-specific properties and formatting for search results
    /// </summary>
    public class SearchResultViewModel : ViewModelBase
    {
        /// <summary>
        /// Note unique identifier
        /// </summary>
        public string NoteId { get; set; } = string.Empty;

        /// <summary>
        /// Note title for display
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Full file path for opening the note
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Category identifier
        /// </summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>
        /// Preview text with highlighted search terms
        /// </summary>
        public string? Preview { get; set; }

        /// <summary>
        /// Search relevance score (higher = better match)
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Raw FTS5 relevance (for debugging)
        /// </summary>
        public float Relevance { get; set; }

        /// <summary>
        /// Result type (Note, Category, etc.)
        /// </summary>
        public SearchResultType ResultType { get; set; } = SearchResultType.Note;

        /// <summary>
        /// Original search query that produced this result
        /// </summary>
        public string SearchQuery { get; set; } = string.Empty;

        /// <summary>
        /// Last modification time
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Highlighted snippet from FTS5
        /// </summary>
        public string HighlightedSnippet { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the category (human-readable)
        /// </summary>
        public string CategoryName { get; set; } = "Uncategorized";

        /// <summary>
        /// User-friendly modification time description
        /// </summary>
        public string ModifiedTimeDescription { get; set; } = string.Empty;

        /// <summary>
        /// Icon or visual indicator for the result type
        /// </summary>
        public string ResultIcon { get; set; } = "📄"; // Default note icon

        /// <summary>
        /// Create SearchResultViewModel from SearchResultDto
        /// </summary>
        /// <param name="dto">Data transfer object from Core layer</param>
        /// <returns>UI-ready view model</returns>
        public static SearchResultViewModel FromDto(SearchResultDto dto)
        {
            return new SearchResultViewModel
            {
                NoteId = dto.NoteId,
                Title = dto.Title,
                FilePath = dto.FilePath,
                CategoryId = dto.CategoryId,
                Preview = dto.Preview,
                Score = dto.Score,
                Relevance = dto.Relevance,
                ResultType = dto.ResultType,
                SearchQuery = dto.SearchQuery,
                LastModified = dto.LastModified,
                HighlightedSnippet = dto.HighlightedSnippet,
                CategoryName = GetCategoryDisplayName(dto.CategoryId),
                ModifiedTimeDescription = GetModifiedTimeDescription(dto.LastModified),
                ResultIcon = GetResultIcon(dto.ResultType)
            };
        }

        /// <summary>
        /// Create list of ViewModels from DTOs
        /// </summary>
        /// <param name="dtos">List of DTOs from Core layer</param>
        /// <returns>List of UI-ready view models</returns>
        public static List<SearchResultViewModel> FromDtos(List<SearchResultDto> dtos)
        {
            return dtos.Select(FromDto).ToList();
        }

        #region Private Helper Methods

        private static string GetCategoryDisplayName(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
                return "Uncategorized";

            // Simple display - could be enhanced to lookup actual category names
            return categoryId.Length > 8 
                ? categoryId.Substring(0, 8) + "..."
                : categoryId;
        }

        private static string GetModifiedTimeDescription(DateTime lastModified)
        {
            if (lastModified == DateTime.MinValue)
                return "Unknown";

            var now = DateTime.Now;
            var diff = now - lastModified;

            if (diff.TotalMinutes < 1)
                return "Just now";
            
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} min ago";
            
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours} hr ago";
            
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays} day{(diff.TotalDays >= 2 ? "s" : "")} ago";
            
            if (diff.TotalDays < 30)
            {
                var weeks = (int)(diff.TotalDays / 7);
                return $"{weeks} week{(weeks > 1 ? "s" : "")} ago";
            }
            
            if (diff.TotalDays < 365)
            {
                var months = (int)(diff.TotalDays / 30);
                return $"{months} month{(months > 1 ? "s" : "")} ago";
            }

            var years = (int)(diff.TotalDays / 365);
            return $"{years} year{(years > 1 ? "s" : "")} ago";
        }

        private static string GetResultIcon(SearchResultType resultType)
        {
            return resultType switch
            {
                SearchResultType.Note => "📄",
                SearchResultType.Category => "📁",
                SearchResultType.Tag => "🏷️",
                _ => "📄"
            };
        }

        #endregion

        /// <summary>
        /// String representation for debugging
        /// </summary>
        /// <returns>Human-readable description</returns>
        public override string ToString()
        {
            return $"SearchResult: '{Title}' (Score: {Score}, File: {FilePath})";
        }
    }
}
