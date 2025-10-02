using System.Threading.Tasks;

namespace NoteNest.Application.Common.Interfaces
{
    public interface IFileService
    {
        Task WriteNoteAsync(string filePath, string content);
        Task<string> ReadNoteAsync(string filePath);
        string GenerateNoteFilePath(string categoryPath, string title);
        Task<bool> FileExistsAsync(string filePath);
        Task DeleteFileAsync(string filePath);
        Task MoveFileAsync(string oldPath, string newPath);
        
        // Directory operations for category management
        Task<bool> DirectoryExistsAsync(string path);
        Task CreateDirectoryAsync(string path);
        Task DeleteDirectoryAsync(string path, bool recursive = false);
        Task MoveDirectoryAsync(string oldPath, string newPath);
    }
}
