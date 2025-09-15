using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public class MarkdownService : IMarkdownService
	{
		private readonly MarkdownPipeline _pipeline;
		private readonly IAppLogger _logger;
		private readonly HashSet<string> _allowedFeatures;
		private readonly ConcurrentDictionary<string, (string content, DateTime timestamp)> _cache;
		private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

		public MarkdownService(IAppLogger logger)
		{
			_logger = logger ?? AppLogger.Instance;
			_cache = new ConcurrentDictionary<string, (string, DateTime)>();

			_pipeline = new MarkdownPipelineBuilder()
				.DisableHtml()
				.UseTaskLists()
				.UsePipeTables()
				.UseEmphasisExtras()
				.UseListExtras()
				.UseFootnotes()
				.UseAutoLinks()
				.UseDefinitionLists()
				.Build();

			_allowedFeatures = new HashSet<string>
			{
				"emphasis", "strong", "lists", "tasklists",
				"tables", "links", "codeblocks", "footnotes",
				"strikethrough", "highlight", "autolinks"
			};
		}

		public HashSet<string> GetAllowedFeatures() => new(_allowedFeatures);

		public bool IsContentSafe(string content)
		{
			if (string.IsNullOrEmpty(content)) return true;
			var dangerousPatterns = new[]
			{
				@"<script", @"</script", @"javascript:",
				@"<iframe", @"<embed", @"<object",
				@"on\w+\s*=", @"data:text/html"
			};
			foreach (var pattern in dangerousPatterns)
			{
				if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
				{
					_logger.Warning($"Dangerous pattern detected: {pattern}");
					return false;
				}
			}
			return true;
		}

		public string SanitizeMarkdown(string content)
		{
			if (string.IsNullOrEmpty(content)) return content;
			content = Regex.Replace(content, @"<script[^>]*>[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"<iframe[^>]*>[\s\S]*?</iframe>", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"javascript:", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"on\w+\s*=", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"<embed[^>]*>", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"<object[^>]*>[\s\S]*?</object>", string.Empty, RegexOptions.IgnoreCase);
			content = Regex.Replace(content, @"<(?!/?(?:p|br|strong|em|code|pre|blockquote|ul|ol|li|h[1-6])\b)[^>]+>", string.Empty, RegexOptions.IgnoreCase);
			return content;
		}

		public ValidationResult ValidateForConversion(string content, NoteFormat targetFormat)
		{
			var result = new ValidationResult { IsValid = true };
			if (string.IsNullOrEmpty(content)) return result;
			if (targetFormat == NoteFormat.PlainText)
			{
				if (content.Contains("```")) result.Warnings.Add("Code blocks will lose formatting");
				if (Regex.IsMatch(content, @"\[.+\]\(.+\)")) result.Warnings.Add("Links will be converted to plain text");
				if (content.Contains("|") && Regex.IsMatch(content, @"^\s*\|.+\|", RegexOptions.Multiline)) result.Warnings.Add("Tables will lose structure");
			}
			if (!IsContentSafe(content))
			{
				result.IsValid = false;
				result.Errors.Add("Content contains potentially unsafe elements");
			}
			return result;
		}

		public string GetCachedProcessedContent(string content, string cacheKey)
		{
			if (_cache.TryGetValue(cacheKey, out var cached))
			{
				if (DateTime.Now - cached.timestamp < _cacheExpiration) return cached.content;
			}
			var processed = StripMarkdownForIndex(content);
			_cache[cacheKey] = (processed, DateTime.Now);
			if (_cache.Count > 100) ClearCache();
			return processed;
		}

		public void ClearCache()
		{
			var expired = _cache.Where(kvp => DateTime.Now - kvp.Value.timestamp > _cacheExpiration).Select(kvp => kvp.Key).ToList();
			foreach (var key in expired) _cache.TryRemove(key, out _);
		}

	public NoteFormat DetectFormat(string filePath)
	{
		if (string.IsNullOrEmpty(filePath)) return NoteFormat.Markdown;
		var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
		return ext switch
		{
			".md" => NoteFormat.Markdown,
			".markdown" => NoteFormat.Markdown,
			".mdown" => NoteFormat.Markdown,
			".rtf" => NoteFormat.RTF,  // BULLETPROOF RTF SUPPORT - matches FileFormatService
			".txt" => NoteFormat.PlainText,
			".text" => NoteFormat.PlainText,
			_ => DetectFormatFromContent(File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty)
		};
	}

		public NoteFormat DetectFormatFromContent(string content)
		{
			if (string.IsNullOrEmpty(content)) return NoteFormat.PlainText;
			var patterns = new[]
			{
				@"^#{1,6}\s", @"\*\*[^*]+\*\*", @"__[^_]+__", @"\*[^*\n]+\*",
				@"_[^_\n]+_", @"\[.+\]\(.+\)", @"!\[.*\]\(.+\)", @"^\s*[-*+]\s+\[[ xX]\]",
				@"^\s*\|.+\|", @"^>\s", @"```[\s\S]*```", @"`[^`]+`", @"^\s*[-*+]\s{2,}", @"^\d+\.\s",
				@"\[^\d+\]"
			};
			int matches = patterns.Count(p => Regex.IsMatch(content, p, RegexOptions.Multiline));
			if (matches >= 2)
			{
				_logger.Debug($"Detected markdown content (matched {matches} patterns)");
				return NoteFormat.Markdown;
			}
			return NoteFormat.PlainText;
		}

		public string ConvertToMarkdown(string plainText)
		{
			if (string.IsNullOrEmpty(plainText)) return plainText;
			var lines = plainText.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				if (Regex.IsMatch(line, @"^(\s*)•\s+(.*)$")) lines[i] = Regex.Replace(line, @"^(\s*)•\s+", "$1- ");
				if (!line.Contains("](")) lines[i] = Regex.Replace(lines[i], @"(?<![(\[])(https?://[^\s\)]+)(?![)\]])", "[$1]($1)");
				lines[i] = Regex.Replace(lines[i], @"(?<![(\[])\b([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,})\b(?![)\]])", "[$1](mailto:$1)");
			}
			return string.Join('\n', lines);
		}

		public string ConvertToPlainText(string markdown)
		{
			if (string.IsNullOrEmpty(markdown)) return markdown;
			markdown = Regex.Replace(markdown, @"^(\s*)[-*+]\s+", "$1• ", RegexOptions.Multiline);
			markdown = Regex.Replace(markdown, @"\*\*([^*]+)\*\*", "$1");
			markdown = Regex.Replace(markdown, @"__([^_]+)__", "$1");
			markdown = Regex.Replace(markdown, @"\*([^*\n]+)\*", "$1");
			markdown = Regex.Replace(markdown, @"_([^_\n]+)_", "$1");
			markdown = Regex.Replace(markdown, @"\[([^\]]+)\]\([^)]+\)", "$1");
			markdown = Regex.Replace(markdown, @"!\[([^\]]*)\]\([^)]+\)", "[$1]");
			markdown = Regex.Replace(markdown, @"^#{1,6}\s+", string.Empty, RegexOptions.Multiline);
			markdown = Regex.Replace(markdown, @"`([^`]+)`", "$1");
			markdown = Regex.Replace(markdown, @"```[^`]*```", "[Code Block]", RegexOptions.Singleline);
			markdown = Regex.Replace(markdown, @"^\>\s+", string.Empty, RegexOptions.Multiline);
			markdown = Regex.Replace(markdown, @"~~([^~]+)~~", "$1");
			markdown = Regex.Replace(markdown, @"==([^=]+)==", "$1");
			return markdown;
		}

		public string UpdateFileExtension(string filePath, NoteFormat format)
		{
			if (string.IsNullOrEmpty(filePath)) return filePath;
			var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
			var name = Path.GetFileNameWithoutExtension(filePath);
			var ext = format == NoteFormat.Markdown ? ".md" : ".txt";
			return Path.Combine(dir, name + ext);
		}

		public bool ValidateMarkdown(string content)
		{
			try { return Markdown.Parse(content, _pipeline) != null; }
			catch (Exception ex) { _logger.Warning($"Markdown validation failed: {ex.Message}"); return false; }
		}

		public string ProcessTaskListSyntax(string content, bool toMarkdown)
		{
			if (string.IsNullOrEmpty(content)) return content;
			return toMarkdown
				? Regex.Replace(content, @"^(\s*)[-*+•]\s+\[([ xX])\]\s+", "$1- [$2] ", RegexOptions.Multiline)
				: Regex.Replace(content, @"^(\s*)[-*+]\s+\[([ xX])\]\s+", "$1- [$2] ", RegexOptions.Multiline);
		}

		public string StripMarkdownForIndex(string markdown)
		{
			var plain = ConvertToPlainText(markdown);
			plain = Regex.Replace(plain, @"\s+", " ");
			plain = Regex.Replace(plain, @"^\s+", string.Empty, RegexOptions.Multiline);
			return plain.Trim();
		}

		/// <summary>
		/// Strip RTF formatting for search indexing - ENHANCED for bulletproof RTF priority
		/// </summary>
		public string StripRTFForIndex(string rtfContent)
		{
			if (string.IsNullOrEmpty(rtfContent)) return string.Empty;

			try
			{
				var plain = rtfContent;

				// ENHANCED REGEX-BASED RTF STRIPPING (RTF PRIORITY)
				
				// Remove RTF document structure elements
				plain = Regex.Replace(plain, @"\\rtf\d+", "", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\ansi", "", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\deff\d+", "", RegexOptions.IgnoreCase);
				
				// Remove font table and color table completely
				plain = Regex.Replace(plain, @"\\fonttbl[^}]*}", "", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\colortbl[^}]*}", "", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\stylesheet[^}]*}", "", RegexOptions.IgnoreCase);
				
				// ENHANCED: Remove specific encoding artifacts like "cpg1252"
				plain = Regex.Replace(plain, @"\\?cpg\d+", "", RegexOptions.IgnoreCase);
				
				// ENHANCED: Remove common font family names that slip through
				plain = Regex.Replace(plain, @"\b(Calibri|Segoe\s+UI|Arial|Times|Verdana|Tahoma|Georgia|Comic\s+Sans)\b", "", RegexOptions.IgnoreCase);
				
				// ENHANCED: Remove font declarations and references
				plain = Regex.Replace(plain, @"\\f\d+\s*[A-Za-z\s]*", "", RegexOptions.IgnoreCase);
				
				// Remove all RTF control words with optional parameters (more aggressive)
				plain = Regex.Replace(plain, @"\\[a-z]+[-]?\d*\s*", " ", RegexOptions.IgnoreCase);
				
				// Remove RTF control symbols and escape sequences
				plain = Regex.Replace(plain, @"\\['\n\r\\{}~\-_]", "");
				
				// Remove all curly braces (RTF grouping)
				plain = Regex.Replace(plain, @"[{}]", "");
				
				// Remove RTF hexadecimal codes (\0x patterns)
				plain = Regex.Replace(plain, @"\\0x[0-9a-f]+", "", RegexOptions.IgnoreCase);
				
				// Remove generator information and other metadata
				plain = Regex.Replace(plain, @"\\[\*]\\[^}]*}", "", RegexOptions.IgnoreCase);
				
				// Clean up paragraph markers and line breaks
				plain = Regex.Replace(plain, @"\\par\b", "\n", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\line\b", "\n", RegexOptions.IgnoreCase);
				plain = Regex.Replace(plain, @"\\tab\b", " ", RegexOptions.IgnoreCase);
				
				// Normalize whitespace and line breaks
				plain = Regex.Replace(plain, @"\s+", " ");
				plain = Regex.Replace(plain, @"\n\s*\n", "\n");
				plain = Regex.Replace(plain, @"^\s+", "", RegexOptions.Multiline);
				plain = Regex.Replace(plain, @"\s+$", "", RegexOptions.Multiline);
				
				// ENHANCED: Final cleanup - remove any remaining leading artifacts
				plain = Regex.Replace(plain, @"^[\s\W]*(?=[A-Za-z])", ""); // Remove leading non-word chars before first letter
				plain = Regex.Replace(plain, @"^\s*[^\w]*\s*", ""); // Clean any remaining prefix junk

				var result = plain.Trim();
				_logger.Debug($"Enhanced RTF extraction: {rtfContent.Length} RTF chars → {result.Length} searchable text chars");
				return result;
			}
			catch (Exception ex)
			{
				_logger.Warning($"RTF text extraction failed: {ex.Message}");
				return string.Empty;
			}
		}
	}
}


