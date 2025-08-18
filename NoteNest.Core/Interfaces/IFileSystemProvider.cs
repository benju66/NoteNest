using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces
{
    public interface IFileSystemProvider
    {
        Task<string> ReadTextAsync(string path);
        Task WriteTextAsync(string path, string content);
        Task<bool> ExistsAsync(string path);
        Task DeleteAsync(string path);
        Task<FileInfo> GetFileInfoAsync(string path);
        Task<IEnumerable<string>> GetFilesAsync(string directory, string searchPattern = "*.*");
        Task<IEnumerable<string>> GetDirectoriesAsync(string directory);
        Task CreateDirectoryAsync(string path);
        Task<Stream> OpenReadAsync(string path);
        Task<Stream> OpenWriteAsync(string path);

        // Added for atomic save support and comprehensive IO abstraction
        Task CopyAsync(string sourcePath, string destinationPath, bool overwrite);
        Task MoveAsync(string sourcePath, string destinationPath, bool overwrite);
        Task ReplaceAsync(string sourceFileName, string destinationFileName, string? backupFileName);
    }
}
