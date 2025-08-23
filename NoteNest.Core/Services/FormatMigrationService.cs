using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public class MigrationOptions
	{
		public string RootPath { get; set; } = string.Empty;
		public NoteFormat TargetFormat { get; set; } = NoteFormat.Markdown;
		public bool CreateBackups { get; set; } = true;
		public bool DryRun { get; set; } = false;
		public IEnumerable<string>? ExcludePatterns { get; set; }
			= null; // glob-like patterns relative to RootPath
		public IEnumerable<string>? IncludeExtensions { get; set; }
			= new[] { ".txt", ".md" };
	}

	public class FormatMigrationService
	{
		private readonly IFileSystemProvider _fileSystem;
		private readonly IMarkdownService _markdownService;
		private readonly IAppLogger _logger;

		public FormatMigrationService(
			IFileSystemProvider fileSystem,
			IMarkdownService markdownService,
			IAppLogger logger)
		{
			_fileSystem = fileSystem;
			_markdownService = markdownService;
			_logger = logger;
		}

		public Task<MigrationResult> MigrateAsync(
			string rootPath,
			NoteFormat targetFormat,
			bool createBackups = true,
			IProgress<MigrationProgress>? progress = null)
		{
			var options = new MigrationOptions
			{
				RootPath = rootPath,
				TargetFormat = targetFormat,
				CreateBackups = createBackups
			};
			return MigrateAsync(options, progress);
		}

		public async Task<MigrationResult> MigrateAsync(
			MigrationOptions options,
			IProgress<MigrationProgress>? progress = null)
		{
			var result = new MigrationResult();
			if (string.IsNullOrWhiteSpace(options.RootPath) || !Directory.Exists(options.RootPath))
			{
				_logger.Warning($"Format migration root missing: {options.RootPath}");
				return result;
			}

			var allFiles = Directory.GetFiles(options.RootPath, "*.*", SearchOption.AllDirectories)
				.Where(f => ShouldIncludeFile(f, options))
				.ToList();
			result.TotalFiles = allFiles.Count;

			for (int i = 0; i < allFiles.Count; i++)
			{
				var file = allFiles[i];
				try
				{
					var currentFormat = _markdownService.DetectFormat(file);
					if (currentFormat == options.TargetFormat)
					{
						result.SkippedFiles++;
						continue;
					}

					var content = await _fileSystem.ReadTextAsync(file);
					var converted = options.TargetFormat == NoteFormat.Markdown
						? _markdownService.ConvertToMarkdown(content)
						: _markdownService.ConvertToPlainText(content);

					var newPath = _markdownService.UpdateFileExtension(file, options.TargetFormat);
					// Record planned change
					if (result.ChangedFiles.Count < 200)
					{
						result.ChangedFiles.Add($"{Path.GetFileName(file)} -> {Path.GetFileName(newPath)}");
					}

					if (!options.DryRun)
					{
						if (options.CreateBackups)
						{
							var backupPath = file + ".bak";
							await _fileSystem.CopyAsync(file, backupPath, overwrite: true);
						}

						await _fileSystem.WriteTextAsync(file, converted);
						if (!string.Equals(newPath, file, StringComparison.OrdinalIgnoreCase))
						{
							await _fileSystem.MoveAsync(file, newPath, overwrite: false);
						}
					}

					result.ConvertedFiles++;
				}
				catch (Exception ex)
				{
					_logger.Error(ex, $"Format migration failed for {file}");
					result.FailedFiles++;
					result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
				}

				progress?.Report(new MigrationProgress
				{
					CurrentFile = file,
					ProcessedCount = i + 1,
					TotalCount = allFiles.Count,
					PercentComplete = allFiles.Count == 0 ? 100 : (i + 1) * 100 / allFiles.Count
				});
			}

			result.Success = result.FailedFiles == 0;
			return result;
		}

		private bool ShouldIncludeFile(string filePath, MigrationOptions options)
		{
			// Include by extension
			if (options.IncludeExtensions != null && options.IncludeExtensions.Any())
			{
				var ext = Path.GetExtension(filePath);
				if (!options.IncludeExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
					return false;
			}
			// Exclude by simple glob patterns
			if (options.ExcludePatterns != null)
			{
				var relative = MakeRelativePath(options.RootPath, filePath).Replace('\\', '/');
				foreach (var pat in options.ExcludePatterns)
				{
					if (GlobIsMatch(relative, pat)) return false;
				}
			}
			return true;
		}

		private static string MakeRelativePath(string root, string fullPath)
		{
			if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return fullPath;
			var rel = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return rel;
		}

		private static bool GlobIsMatch(string text, string pattern)
		{
			// Very small glob: * matches any chars, ? matches one char. Case-insensitive.
			var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern.Replace('\\', '/'))
				.Replace(@"\*", ".*")
				.Replace(@"\?", ".") + "$";
			return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		}
	}

	public class MigrationResult
	{
		public bool Success { get; set; }
		public int TotalFiles { get; set; }
		public int ConvertedFiles { get; set; }
		public int SkippedFiles { get; set; }
		public int FailedFiles { get; set; }
		public List<string> Errors { get; set; } = new List<string>();
		public List<string> ChangedFiles { get; set; } = new List<string>();
	}

	public class MigrationProgress
	{
		public string CurrentFile { get; set; } = string.Empty;
		public int ProcessedCount { get; set; }
		public int TotalCount { get; set; }
		public int PercentComplete { get; set; }
	}
}


