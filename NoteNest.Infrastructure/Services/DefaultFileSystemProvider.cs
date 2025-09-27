using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;

namespace NoteNest.Infrastructure.Services
{
    public class DefaultFileSystemProvider : IFileSystemProvider
    {
        public Task<bool> ExistsAsync(string path)
        {
            return Task.FromResult(File.Exists(path) || Directory.Exists(path));
        }

        public Task CreateDirectoryAsync(string path)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetDirectoriesAsync(string path)
        {
            if (!Directory.Exists(path))
                return Task.FromResult(Enumerable.Empty<string>());
                
            return Task.FromResult(Directory.GetDirectories(path).AsEnumerable());
        }

        public Task<IEnumerable<string>> GetFilesAsync(string path, string searchPattern = "*.*")
        {
            if (!Directory.Exists(path))
                return Task.FromResult(Enumerable.Empty<string>());
                
            return Task.FromResult(Directory.GetFiles(path, searchPattern).AsEnumerable());
        }

        public Task<string> ReadTextAsync(string filePath)
        {
            return File.ReadAllTextAsync(filePath);
        }

        public Task WriteTextAsync(string filePath, string content)
        {
            return File.WriteAllTextAsync(filePath, content);
        }

        public Task DeleteAsync(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, true);
            return Task.CompletedTask;
        }

        public Task<FileInfo> GetFileInfoAsync(string filePath)
        {
            return Task.FromResult(new FileInfo(filePath));
        }

        public Task CopyAsync(string sourcePath, string destinationPath, bool overwrite = false)
        {
            File.Copy(sourcePath, destinationPath, overwrite);
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false)
        {
            if (overwrite && File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(sourcePath, destinationPath);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
        {
            File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string filePath)
        {
            return Task.FromResult<Stream>(File.OpenRead(filePath));
        }

        public Task<Stream> OpenWriteAsync(string filePath)
        {
            return Task.FromResult<Stream>(File.OpenWrite(filePath));
        }
    }
}
