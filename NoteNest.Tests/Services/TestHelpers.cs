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
			if (!Files.ContainsKey(path))
				return Task.FromResult<Stream>(Stream.Null);
			var bytes = System.Text.Encoding.UTF8.GetBytes(Files[path]);
			return Task.FromResult<Stream>(new MemoryStream(bytes));
		}

		public Task<Stream> OpenWriteAsync(string path)
		{
			var inner = new MemoryStream();
			return Task.FromResult<Stream>(new WriteCapturingStream(inner, path, this));
		}

		private sealed class WriteCapturingStream : Stream
		{
			private readonly MemoryStream _inner;
			private readonly string _path;
			private readonly SharedMockFileSystemProvider _provider;

			public WriteCapturingStream(MemoryStream inner, string path, SharedMockFileSystemProvider provider)
			{
				_inner = inner;
				_path = path;
				_provider = provider;
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing)
				{
					_provider.Files[_path] = System.Text.Encoding.UTF8.GetString(_inner.ToArray());
					_inner.Dispose();
				}
				base.Dispose(disposing);
			}

			public override bool CanRead => _inner.CanRead;
			public override bool CanSeek => _inner.CanSeek;
			public override bool CanWrite => _inner.CanWrite;
			public override long Length => _inner.Length;
			public override long Position { get => _inner.Position; set => _inner.Position = value; }
			public override void Flush() => _inner.Flush();
			public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
			public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
			public override void SetLength(long value) => _inner.SetLength(value);
			public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
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
