using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public class FileFormatService
	{
		private readonly IAppLogger _logger;

		public FileFormatService(IAppLogger logger)
		{
			_logger = logger ?? AppLogger.Instance;
		}

		public string GetExtensionForFormat(NoteFormat format)
		{
			return format switch
			{
				NoteFormat.Markdown => ".md",
				NoteFormat.PlainText => ".txt",
				NoteFormat.RTF => ".rtf",
				_ => ".txt"
			};
		}

		public NoteFormat DetectFormatFromPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return NoteFormat.PlainText;

			var extension = Path.GetExtension(path)?.ToLowerInvariant();
			return extension switch
			{
				".md" => NoteFormat.Markdown,
				".markdown" => NoteFormat.Markdown,
				".mdown" => NoteFormat.Markdown,
				".rtf" => NoteFormat.RTF,
				".txt" => NoteFormat.PlainText,
				".text" => NoteFormat.PlainText,
				_ => NoteFormat.PlainText
			};
		}

		public string ChangeExtension(string path, NoteFormat format)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			var directory = Path.GetDirectoryName(path) ?? string.Empty;
			var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
			var newExtension = GetExtensionForFormat(format);

			return Path.Combine(directory, nameWithoutExt + newExtension);
		}

		public bool RequiresConversion(string path, NoteFormat targetFormat)
		{
			var currentFormat = DetectFormatFromPath(path);
			return currentFormat != targetFormat;
		}

		public bool IsMarkdownFile(string path)
		{
			return DetectFormatFromPath(path) == NoteFormat.Markdown;
		}

		public bool IsTextFile(string path)
		{
			return DetectFormatFromPath(path) == NoteFormat.PlainText;
		}

		/// <summary>
		/// Dynamically discover all supported note files in a directory
		/// Future-proof: automatically includes any new formats added to NoteFormat enum
		/// </summary>
		public async Task<List<string>> GetAllNoteFilesAsync(IFileSystemProvider fileSystem, string directoryPath)
		{
			var allFiles = new List<string>();
			var supportedFormats = Enum.GetValues<NoteFormat>();
			
			foreach (var format in supportedFormats)
			{
				try 
				{
					var extension = GetExtensionForFormat(format);
					var pattern = $"*{extension}";
					var formatFiles = await fileSystem.GetFilesAsync(directoryPath, pattern);
					allFiles.AddRange(formatFiles);
					
					_logger.Debug($"Found {formatFiles.Count()} {format} files in {directoryPath}");
				}
				catch (Exception ex)
				{
					_logger.Debug($"Failed to scan for {format} files in {directoryPath}: {ex.Message}");
					// Continue with other formats - don't fail completely
				}
			}
			
			_logger.Debug($"Total files discovered: {allFiles.Count} in {directoryPath}");
			return allFiles;
		}
	}
}


