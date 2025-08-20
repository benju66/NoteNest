using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;

namespace NoteNest.Tests.Services
{
	public class SharedMockFileSystemProvider : IFileSystemProvider
	{
		public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();
		public HashSet<string> Directories { get; } = new HashSet<string>();

		public Task<string> ReadTextAsync(string path)
		{
			return Task.FromResult(Files.ContainsKey(path) ? Files[path] : string.Empty);
		}

		public Task WriteTextAsync(string path, string content)
		{
			Files[path] = content;
			return Task.CompletedTask;
		}

		public Task<bool> ExistsAsync(string path)
		{
			return Task.FromResult(Files.ContainsKey(path) || Directories.Contains(path));
		}

		public Task DeleteAsync(string path)
		{
			Files.Remove(path);
			Directories.Remove(path);
			return Task.CompletedTask;
		}

		public Task<FileInfo> GetFileInfoAsync(string path)
		{
			return Task.FromResult(new FileInfo(path));
		}

		public Task<IEnumerable<string>> GetFilesAsync(string directory, string searchPattern = "*.*")
		{
			return Task.FromResult(Files.Keys.Where(k => k.StartsWith(directory)));
		}

		public Task<IEnumerable<string>> GetDirectoriesAsync(string directory)
		{
			return Task.FromResult(Directories.Where(d => d.StartsWith(directory)));
		}

		public Task CreateDirectoryAsync(string path)
		{
			Directories.Add(path);
			return Task.CompletedTask;
		}

		public Task<Stream> OpenReadAsync(string path)
		{
			throw new NotImplementedException();
		}

		public Task<Stream> OpenWriteAsync(string path)
		{
			throw new NotImplementedException();
		}

		public Task CopyAsync(string sourcePath, string destinationPath, bool overwrite)
		{
			if (Files.ContainsKey(sourcePath))
			{
				if (!overwrite && Files.ContainsKey(destinationPath))
					throw new IOException("Destination exists");
				Files[destinationPath] = Files[sourcePath];
			}
			return Task.CompletedTask;
		}

		public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite)
		{
			if (Files.ContainsKey(sourcePath))
			{
				if (!overwrite && Files.ContainsKey(destinationPath))
					throw new IOException("Destination exists");
				Files[destinationPath] = Files[sourcePath];
				Files.Remove(sourcePath);
			}
			return Task.CompletedTask;
		}

		public Task ReplaceAsync(string sourceFileName, string destinationFileName, string? backupFileName)
		{
			if (Files.ContainsKey(sourceFileName))
			{
				if (!string.IsNullOrEmpty(backupFileName) && Files.ContainsKey(destinationFileName))
				{
					Files[backupFileName] = Files[destinationFileName];
				}
				Files[destinationFileName] = Files[sourceFileName];
				Files.Remove(sourceFileName);
			}
			return Task.CompletedTask;
		}
	}
}
