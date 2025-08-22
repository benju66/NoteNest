using System.Collections.Generic;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
	public interface IMarkdownService
	{
		// Format detection
		NoteFormat DetectFormat(string filePath);
		NoteFormat DetectFormatFromContent(string content);

		// Security
		string SanitizeMarkdown(string content);
		bool IsContentSafe(string content);
		HashSet<string> GetAllowedFeatures();

		// Conversion
		string ConvertToMarkdown(string plainText);
		string ConvertToPlainText(string markdown);
		string UpdateFileExtension(string filePath, NoteFormat format);

		// Validation
		bool ValidateMarkdown(string content);
		ValidationResult ValidateForConversion(string content, NoteFormat targetFormat);

		// Task list handling
		string ProcessTaskListSyntax(string content, bool toMarkdown);

		// Search optimization
		string StripMarkdownForIndex(string markdown);

		// Performance
		string GetCachedProcessedContent(string content, string cacheKey);
		void ClearCache();
	}

	public class ValidationResult
	{
		public bool IsValid { get; set; }
		public List<string> Warnings { get; set; } = new();
		public List<string> Errors { get; set; } = new();
	}
}


