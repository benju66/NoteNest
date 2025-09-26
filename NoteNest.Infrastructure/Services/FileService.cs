using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Services
{
    public class FileService : IFileService
    {
        private readonly IAppLogger _logger;

        public FileService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task WriteNoteAsync(string filePath, string content)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, content);
                _logger.Debug($"Wrote note to: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to write note to {filePath}");
                throw;
            }
        }

        public async Task<string> ReadNoteAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return string.Empty;

                return await File.ReadAllTextAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to read note from {filePath}");
                throw;
            }
        }

        public string GenerateNoteFilePath(string categoryPath, string title)
        {
            // Sanitize title for filename
            var sanitizedTitle = SanitizeFileName(title);
            
            // Ensure .rtf extension
            if (!sanitizedTitle.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedTitle += ".rtf";
            }

            return Path.Combine(categoryPath, sanitizedTitle);
        }

        public async Task<bool> FileExistsAsync(string filePath)
        {
            return File.Exists(filePath);
        }

        public async Task DeleteFileAsync(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.Debug($"Deleted file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete file {filePath}");
                throw;
            }
        }

        public async Task MoveFileAsync(string oldPath, string newPath)
        {
            try
            {
                if (File.Exists(oldPath))
                {
                    var newDirectory = Path.GetDirectoryName(newPath);
                    if (!Directory.Exists(newDirectory))
                    {
                        Directory.CreateDirectory(newDirectory);
                    }

                    File.Move(oldPath, newPath);
                    _logger.Debug($"Moved file: {oldPath} -> {newPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move file {oldPath} -> {newPath}");
                throw;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;
            
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Remove extra spaces and periods
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            sanitized = sanitized.TrimEnd('.');

            // Ensure not empty
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "Untitled";
            }

            return sanitized;
        }
    }
}
