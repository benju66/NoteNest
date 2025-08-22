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

		public async Task<MigrationResult> MigrateAsync(
			string rootPath,
			NoteFormat targetFormat,
			bool createBackups = true,
			IProgress<MigrationProgress>? progress = null)
		{
			var result = new MigrationResult();
			if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			{
				_logger.Warning($"Format migration root missing: {rootPath}");
				return result;
			}

			var allFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
				.Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
				.ToList();
			result.TotalFiles = allFiles.Count;

			for (int i = 0; i < allFiles.Count; i++)
			{
				var file = allFiles[i];
				try
				{
					var currentFormat = _markdownService.DetectFormat(file);
					if (currentFormat == targetFormat)
					{
						result.SkippedFiles++;
						continue;
					}

					var content = await _fileSystem.ReadTextAsync(file);
					var converted = targetFormat == NoteFormat.Markdown
						? _markdownService.ConvertToMarkdown(content)
						: _markdownService.ConvertToPlainText(content);

					if (createBackups)
					{
						var backupPath = file + ".bak";
						await _fileSystem.CopyAsync(file, backupPath, overwrite: true);
					}

					var newPath = _markdownService.UpdateFileExtension(file, targetFormat);
					await _fileSystem.WriteTextAsync(file, converted);
					if (!string.Equals(newPath, file, StringComparison.OrdinalIgnoreCase))
					{
						await _fileSystem.MoveAsync(file, newPath, overwrite: false);
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
	}

	public class MigrationResult
	{
		public bool Success { get; set; }
		public int TotalFiles { get; set; }
		public int ConvertedFiles { get; set; }
		public int SkippedFiles { get; set; }
		public int FailedFiles { get; set; }
		public List<string> Errors { get; set; } = new List<string>();
	}

	public class MigrationProgress
	{
		public string CurrentFile { get; set; } = string.Empty;
		public int ProcessedCount { get; set; }
		public int TotalCount { get; set; }
		public int PercentComplete { get; set; }
	}
}


