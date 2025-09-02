using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;

namespace NoteNest.Core.Services
{
    public class DefaultFileSystemProvider : IFileSystemProvider
    {
        public async Task<string> ReadTextAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            return await Task.Run(() => File.ReadAllText(path));
        }

        public async Task WriteTextAsync(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            await Task.Run(() => File.WriteAllText(path, content ?? string.Empty));
        }

        public async Task<bool> ExistsAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return await Task.Run(() => File.Exists(path) || Directory.Exists(path));
        }

        public async Task DeleteAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            await Task.Run(() =>
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            });
        }

        public async Task<FileInfo> GetFileInfoAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            return await Task.Run(() => new FileInfo(path));
        }

        public async Task<IEnumerable<string>> GetFilesAsync(string directory, string searchPattern = "*.*")
        {
            return await Task.Run(() => 
            {
                if (!Directory.Exists(directory))
                    return new string[0];
                return Directory.GetFiles(directory, searchPattern);
            });
        }

        public async Task<IEnumerable<string>> GetDirectoriesAsync(string directory)
        {
            return await Task.Run(() => 
            {
                if (!Directory.Exists(directory))
                    return new string[0];
                return Directory.GetDirectories(directory);
            });
        }

        public async Task CreateDirectoryAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            await Task.Run(() => Directory.CreateDirectory(path));
        }

        public async Task<Stream> OpenReadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            return await Task.Run(() => File.OpenRead(path) as Stream);
        }

        public async Task<Stream> OpenWriteAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            return await Task.Run(() => File.OpenWrite(path) as Stream);
        }

        public async Task CopyAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));
            await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite));
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                    throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
                if (string.IsNullOrWhiteSpace(destinationPath))
                    throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));
                if (overwrite && File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                File.Move(sourcePath, destinationPath);
            });
        }

        public async Task ReplaceAsync(string sourceFileName, string destinationFileName, string? backupFileName)
        {
            if (string.IsNullOrWhiteSpace(sourceFileName))
                throw new ArgumentException("Source path cannot be null or empty", nameof(sourceFileName));
            if (string.IsNullOrWhiteSpace(destinationFileName))
                throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationFileName));
            await Task.Run(() => File.Replace(sourceFileName, destinationFileName, backupFileName));
        }
    }
}
