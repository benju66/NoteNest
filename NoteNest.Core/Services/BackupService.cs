using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
	public class BackupService
	{
		private readonly IFileSystemProvider _fileSystem;
		private readonly IAppLogger _logger;
		private readonly string _backupRoot;

		public BackupService(IFileSystemProvider fileSystem, IAppLogger logger)
		{
			_fileSystem = fileSystem;
			_logger = logger;
			_backupRoot = Path.Combine(PathService.RootPath, ".backups");
		}

		public async Task<BackupResult> CreateBackup(string filePath)
		{
			try
			{
				var backupId = $"{Path.GetFileName(filePath)}_{DateTime.Now:yyyyMMdd_HHmmss}";
				var backupPath = Path.Combine(_backupRoot, backupId);

				await _fileSystem.CreateDirectoryAsync(_backupRoot);
				await _fileSystem.CopyAsync(filePath, backupPath, overwrite: false);

				return new BackupResult 
				{ 
					Success = true, 
					BackupId = backupId,
					BackupPath = backupPath,
					OriginalPath = filePath,
					Timestamp = DateTime.Now
				};
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to backup {filePath}");
				return new BackupResult { Success = false, Error = ex.Message };
			}
		}

		public async Task<RestoreResult> RestoreBackup(string backupId, string targetPath)
		{
			try
			{
				var backupPath = Path.Combine(_backupRoot, backupId);
				if (!await _fileSystem.ExistsAsync(backupPath))
				{
					return new RestoreResult { Success = false, Error = "Backup not found" };
				}

				// Create restore point of current file
				if (await _fileSystem.ExistsAsync(targetPath))
				{
					await CreateBackup(targetPath);
				}

				await _fileSystem.CopyAsync(backupPath, targetPath, overwrite: true);

				return new RestoreResult { Success = true, RestoredPath = targetPath };
			}
			catch (Exception ex)
			{
				_logger.Error(ex, $"Failed to restore backup {backupId}");
				return new RestoreResult { Success = false, Error = ex.Message };
			}
		}

		public async Task<List<BackupInfo>> GetBackups(string originalPath)
		{
			var backups = new List<BackupInfo>();
			var fileName = Path.GetFileName(originalPath);

			if (!await _fileSystem.ExistsAsync(_backupRoot))
				return backups;

			var files = await _fileSystem.GetFilesAsync(_backupRoot, $"{fileName}_*");
			foreach (var file in files)
			{
				var info = new FileInfo(file);
				backups.Add(new BackupInfo
				{
					BackupId = Path.GetFileName(file),
					OriginalPath = originalPath,
					BackupPath = file,
					Size = info.Length,
					Timestamp = info.CreationTime
				});
			}

			return backups.OrderByDescending(b => b.Timestamp).ToList();
		}
	}

	public class BackupResult
	{
		public bool Success { get; set; }
		public string BackupId { get; set; }
		public string BackupPath { get; set; }
		public string OriginalPath { get; set; }
		public DateTime Timestamp { get; set; }
		public string Error { get; set; }
	}

	public class RestoreResult
	{
		public bool Success { get; set; }
		public string RestoredPath { get; set; }
		public string Error { get; set; }
	}

	public class BackupInfo
	{
		public string BackupId { get; set; }
		public string OriginalPath { get; set; }
		public string BackupPath { get; set; }
		public long Size { get; set; }
		public DateTime Timestamp { get; set; }
	}
}


