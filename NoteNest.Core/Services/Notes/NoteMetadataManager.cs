using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public class NoteMetadataManager
	{
		private readonly IFileSystemProvider _fileSystem;
		private readonly IAppLogger _logger;

		public NoteMetadataManager(IFileSystemProvider fileSystem, IAppLogger logger)
		{
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_logger = logger ?? AppLogger.Instance;
		}

		public class NoteMetadata
		{
			public int Version { get; set; } = 1;
			public string Id { get; set; } = string.Empty;
			public DateTime Created { get; set; } = DateTime.UtcNow;
			public Dictionary<string, object> Extensions { get; set; } = new();
		}

		public async Task<string> GetOrCreateNoteIdAsync(NoteModel note)
		{
			if (note == null) throw new ArgumentNullException(nameof(note));
			var metaPath = GetMetaPath(note.FilePath);
			try
			{
				if (await _fileSystem.ExistsAsync(metaPath))
				{
					var metadata = await ReadMetadataAsync(metaPath);
					if (!string.IsNullOrWhiteSpace(metadata?.Id))
					{
						note.Id = metadata.Id;
						return metadata.Id;
					}
				}

				// Create new metadata with a new ID
				note.Id = Guid.NewGuid().ToString();
				await WriteMetadataAsync(note);
				return note.Id;
			}
			catch (UnauthorizedAccessException)
			{
				// Read-only or protected location; generate deterministic ID as fallback
				var det = GenerateDeterministicId(note.FilePath);
				note.Id = det;
				_logger.Warning($"Cannot write metadata at: {metaPath}. Using deterministic ID.");
				return det;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to obtain/create metadata for: {metaPath}");
				// Fallback: keep existing or generate deterministic
				if (string.IsNullOrWhiteSpace(note.Id))
				{
					note.Id = GenerateDeterministicId(note.FilePath);
				}
				return note.Id;
			}
		}

		public async Task MoveMetadataAsync(string oldPath, string newPath)
		{
			if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return;
			var oldMeta = GetMetaPath(oldPath);
			var newMeta = GetMetaPath(newPath);
			try
			{
				if (!await _fileSystem.ExistsAsync(oldMeta)) return;
				// Copy then delete to accommodate providers without atomic move
				var json = await _fileSystem.ReadTextAsync(oldMeta);
				await _fileSystem.WriteTextAsync(newMeta, json);
				await _fileSystem.DeleteAsync(oldMeta);
				_logger.Debug($"Moved metadata: {oldMeta} -> {newMeta}");
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to move metadata from {oldMeta} to {newMeta}");
			}
		}

		public string GetMetaPath(string notePath)
		{
			if (string.IsNullOrWhiteSpace(notePath)) return string.Empty;
			var idx = notePath.LastIndexOf('.');
			return idx >= 0 ? notePath.Substring(0, idx) + ".meta" : notePath + ".meta";
		}

		private async Task<NoteMetadata?> ReadMetadataAsync(string metaPath)
		{
			try
			{
				var json = await _fileSystem.ReadTextAsync(metaPath);
				var opts = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					ReadCommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true
				};
				return JsonSerializer.Deserialize<NoteMetadata>(json, opts);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to read metadata: {metaPath}");
				return null;
			}
		}

		private async Task WriteMetadataAsync(NoteModel note)
		{
			var meta = new NoteMetadata
			{
				Id = note.Id,
				Created = DateTime.UtcNow
			};
			var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
			var metaPath = GetMetaPath(note.FilePath);
			await _fileSystem.WriteTextAsync(metaPath, json);
		}

		private string GenerateDeterministicId(string path)
		{
			// Normalize path for stability
			var normalized = (path ?? string.Empty).Trim().ToLowerInvariant();
			var bytes = Encoding.UTF8.GetBytes(normalized);
			var hash = SHA256.HashData(bytes);
			return "det_" + Convert.ToBase64String(hash, Base64FormattingOptions.None).Replace('/', '_').Replace('+', '-').Substring(0, 16);
		}
	}
}


