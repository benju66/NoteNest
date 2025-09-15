using System;
using System.IO;
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
	}
}


